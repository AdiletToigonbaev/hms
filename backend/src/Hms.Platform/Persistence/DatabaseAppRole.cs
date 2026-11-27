using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace Hms.Platform.Persistence;

/// <summary>
/// Роль PostgreSQL, под которой работает API в production (security-architecture §8).
/// Команда <c>migrate</c> (под владельцем схемы) создаёт её и выдаёт только права на данные:
/// без DDL (нельзя отключить триггеры неизменяемости), без UPDATE/DELETE журнала аудита.
/// Не задана — API работает под тем же пользователем, что и миграции (разработка).
/// </summary>
public sealed class DatabaseAppRoleOptions
{
    public const string Section = "Database:AppRole";

    /// <summary>Имя роли (латиница в нижнем регистре, цифры, «_»).</summary>
    public string? Name { get; set; }

    /// <summary>Пароль роли. Только из окружения (<c>Database__AppRole__Password</c>).</summary>
    public string? Password { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Name);
}

public static partial class DatabaseAppRole
{
    /// <summary>Создаёт роль (или обновляет пароль) и перевыдаёт права на все таблицы схем модулей.</summary>
    public static async Task ApplyAsync(DbContext db, DatabaseAppRoleOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(options);
        if (!options.IsConfigured)
        {
            return;
        }

        if (!RoleName().IsMatch(options.Name!))
        {
            throw new InvalidOperationException("Database:AppRole:Name — только латиница в нижнем регистре, цифры и «_», до 63 символов.");
        }

        if (string.IsNullOrWhiteSpace(options.Password) || options.Password.Length < 16)
        {
            throw new InvalidOperationException("Database:AppRole:Password не задан или короче 16 символов (переменная Database__AppRole__Password).");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Имя и пароль передаются параметрами через set_config (локально для транзакции),
        // а в DDL подставляются format('%I'/'%L') внутри БД — без склейки строк в C#.
        await db.Database.ExecuteSqlAsync(
            $"SELECT set_config('hms.app_role_name', {options.Name}, true), set_config('hms.app_role_password', {options.Password}, true)", ct);
        await db.Database.ExecuteSqlRawAsync(GrantSql, ct);
        await tx.CommitAsync(ct);
    }

    // Права перевыдаются при каждом migrate: новые таблицы новых миграций получают их сразу.
    // public не входит: там только функции btree_gist (EXECUTE есть у PUBLIC).
    private const string GrantSql = """
        DO $grant$
        DECLARE
            r text := current_setting('hms.app_role_name');
            p text := current_setting('hms.app_role_password');
            s record;
        BEGIN
            IF r = current_user THEN
                RAISE EXCEPTION 'Database:AppRole:Name совпадает с пользователем миграций (%)', r;
            END IF;

            IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = r) THEN
                EXECUTE format('ALTER ROLE %I WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD %L', r, p);
            ELSE
                EXECUTE format('CREATE ROLE %I WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD %L', r, p);
            END IF;

            EXECUTE format('GRANT CONNECT ON DATABASE %I TO %I', current_database(), r);

            FOR s IN
                SELECT nspname FROM pg_namespace
                WHERE nspname NOT IN ('public', 'information_schema') AND nspname NOT LIKE 'pg\_%'
            LOOP
                EXECUTE format('GRANT USAGE ON SCHEMA %I TO %I', s.nspname, r);
                EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA %I TO %I', s.nspname, r);
                EXECUTE format('GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA %I TO %I', s.nspname, r);
            END LOOP;

            -- Журнал аудита: только чтение и добавление (триггер — вторая линия защиты).
            EXECUTE format('REVOKE UPDATE, DELETE, TRUNCATE ON audit.audit_logs FROM %I', r);
            -- История миграций меняется только командой migrate.
            EXECUTE format('REVOKE INSERT, UPDATE, DELETE, TRUNCATE ON platform.__ef_migrations_history FROM %I', r);
        END
        $grant$;
        """;

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$")]
    private static partial Regex RoleName();
}
