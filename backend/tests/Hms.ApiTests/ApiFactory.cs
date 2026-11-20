using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Hms.ApiTests;

/// <summary>
/// Хост API без реальной БД: проверяется HTTP-контракт (ошибки, correlation id, liveness).
/// Строка подключения указывает на заведомо недоступный адрес — к БД эти тесты не обращаются.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string ThrowPath = "/__test/throw";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Hms", "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=1");
        builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, ThrowingEndpointFilter>());
    }

    /// <summary>Добавляет в конвейер маршрут, бросающий исключение, — для проверки глобального обработчика.</summary>
    private sealed class ThrowingEndpointFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Map(ThrowPath, branch => branch.Run(_ =>
                throw new InvalidOperationException("секретная деталь, которую нельзя показывать клиенту")));
        };
    }
}
