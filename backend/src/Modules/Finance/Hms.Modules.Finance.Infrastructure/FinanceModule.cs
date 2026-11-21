using FluentValidation;
using Hms.Modules.Finance.Application;
using Hms.Modules.Finance.Contracts;
using Hms.Modules.Finance.Domain;
using Hms.Modules.Hotel.Contracts;
using Hms.Modules.Identity.Contracts;
using Hms.Platform.Http;
using Hms.Platform.Persistence;
using Hms.Platform.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hms.Modules.Finance.Infrastructure;

public static class FinanceModule
{
    public static IServiceCollection AddFinanceModule(this IServiceCollection services)
    {
        services.AddSingleton<IModelConfigurator, FinanceModelConfigurator>();
        services.AddScoped<IDataSeeder, ExpenseCategorySeeder>();
        services.AddScoped<FinanceReads>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IRefundService, RefundService>();
        services.AddScoped<IMyFinanceService, MyFinanceService>();
        services.AddScoped<IExpenseService, ExpenseService>();
        services.AddScoped<IPaymentQueries, PaymentQueries>();
        services.AddValidatorsFromAssemblyContaining<RegisterPaymentRequestValidator>(includeInternalTypes: true);
        return services;
    }

    public static IEndpointRouteBuilder MapFinanceEndpoints(this IEndpointRouteBuilder api)
    {
        var payments = api.MapGroup("/payments").WithTags("Finance");
        payments.MapGet("/", async ([AsParameters] PaymentQuery q, IPaymentService s, CancellationToken ct) => Results.Ok(await s.ListAsync(q, ct)))
            .RequirePermission(Permissions.PaymentView);
        payments.MapGet("/{id:guid}", async (Guid id, IPaymentService s, CancellationToken ct) => (await s.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission(Permissions.PaymentView);
        payments.MapPost("/manual", async (RegisterPaymentRequest r, IPaymentService s, CancellationToken ct) =>
                (await s.RegisterAsync(r, ct)).ToHttpResult(p => Results.Created($"/api/v1/payments/{p.Payment.Id}", p)))
            .WithValidation<RegisterPaymentRequest>().WithIdempotency("payment.register").RequirePermission(Permissions.PaymentRegister);
        payments.MapPost("/{id:guid}/reverse", async (Guid id, ReversePaymentRequest r, IPaymentService s, CancellationToken ct) =>
                (await s.ReverseAsync(id, r, ct)).ToHttpResult())
            .WithValidation<ReversePaymentRequest>().WithIdempotency("payment.reverse").RequirePermission(Permissions.PaymentReverse);

        api.MapGet("/bookings/{id:guid}/balance", async (Guid id, IPaymentService s, CancellationToken ct) => (await s.GetBookingBalanceAsync(id, ct)).ToHttpResult())
            .WithTags("Finance").RequirePermission(Permissions.PaymentView);
        api.MapGet("/finance/debts", async ([AsParameters] DebtQuery q, IPaymentService s, CancellationToken ct) => Results.Ok(await s.ListDebtsAsync(q, ct)))
            .WithTags("Finance").RequirePermission(Permissions.PaymentView);

        // Кабинет гостя: только свои платежи, возвраты и баланс своих броней (владение проверяет сервис; чужое — 404).
        var me = api.MapGroup("/me").WithTags("Account").RequireAuthorization();
        me.MapGet("/payments", async (IMyFinanceService s, CancellationToken ct) => Results.Ok(await s.PaymentsAsync(ct)));
        me.MapGet("/refunds", async (IMyFinanceService s, CancellationToken ct) => Results.Ok(await s.RefundsAsync(ct)));
        me.MapPost("/refunds", async (MyRefundRequest r, IMyFinanceService s, CancellationToken ct) =>
                (await s.RequestRefundAsync(r, ct)).ToHttpResult(x => Results.Created("/api/v1/me/refunds", x)))
            .WithValidation<MyRefundRequest>().WithIdempotency("me.refund.request");
        me.MapGet("/bookings/{id:guid}/balance", async (Guid id, IMyFinanceService s, CancellationToken ct) => (await s.BalanceAsync(id, ct)).ToHttpResult());

        var refunds = api.MapGroup("/refunds").WithTags("Finance");
        refunds.MapGet("/", async ([AsParameters] RefundQuery q, IRefundService s, CancellationToken ct) => Results.Ok(await s.ListAsync(q, ct)))
            .RequireAnyPermission(Permissions.RefundRequest, Permissions.RefundApprove);
        refunds.MapGet("/{id:guid}", async (Guid id, IRefundService s, CancellationToken ct) => (await s.GetAsync(id, ct)).ToHttpResult())
            .RequireAnyPermission(Permissions.RefundRequest, Permissions.RefundApprove);
        refunds.MapPost("/", async (RequestRefundRequest r, IRefundService s, CancellationToken ct) =>
                (await s.RequestAsync(r, ct)).ToHttpResult(x => Results.Created($"/api/v1/refunds/{x.Id}", x)))
            .WithValidation<RequestRefundRequest>().WithIdempotency("refund.request").RequirePermission(Permissions.RefundRequest);
        refunds.MapPost("/{id:guid}/approve", async (Guid id, DecideRefundRequest r, IRefundService s, CancellationToken ct) => (await s.ApproveAsync(id, r, ct)).ToHttpResult())
            .WithValidation<DecideRefundRequest>().RequirePermission(Permissions.RefundApprove);
        refunds.MapPost("/{id:guid}/reject", async (Guid id, DecideRefundRequest r, IRefundService s, CancellationToken ct) => (await s.RejectAsync(id, r, ct)).ToHttpResult())
            .WithValidation<DecideRefundRequest>().RequirePermission(Permissions.RefundApprove);
        refunds.MapPost("/{id:guid}/mark-paid", async (Guid id, IRefundService s, CancellationToken ct) => (await s.MarkPaidAsync(id, ct)).ToHttpResult())
            .WithIdempotency("refund.mark_paid").RequirePermission(Permissions.RefundApprove);

        var expenses = api.MapGroup("/expenses").WithTags("Finance");
        expenses.MapGet("/", async ([AsParameters] ExpenseQuery q, IExpenseService s, CancellationToken ct) => Results.Ok(await s.ListAsync(q, ct)))
            .RequireAnyPermission(Permissions.ExpenseCreate, Permissions.ExpenseManage);
        expenses.MapGet("/{id:guid}", async (Guid id, IExpenseService s, CancellationToken ct) => (await s.GetAsync(id, ct)).ToHttpResult())
            .RequireAnyPermission(Permissions.ExpenseCreate, Permissions.ExpenseManage);
        expenses.MapPost("/", async (SaveExpenseRequest r, IExpenseService s, CancellationToken ct) =>
                (await s.CreateAsync(r, ct)).ToHttpResult(x => Results.Created($"/api/v1/expenses/{x.Id}", x)))
            .WithValidation<SaveExpenseRequest>().WithIdempotency("expense.create").RequirePermission(Permissions.ExpenseCreate);
        expenses.MapPut("/{id:guid}", async (Guid id, SaveExpenseRequest r, IExpenseService s, CancellationToken ct) => (await s.UpdateAsync(id, r, ct)).ToHttpResult())
            .WithValidation<SaveExpenseRequest>().RequirePermission(Permissions.ExpenseManage);
        expenses.MapPost("/{id:guid}/void", async (Guid id, VoidExpenseRequest r, IExpenseService s, CancellationToken ct) => (await s.VoidAsync(id, r, ct)).ToHttpResult())
            .WithValidation<VoidExpenseRequest>().RequirePermission(Permissions.ExpenseManage);

        var categories = api.MapGroup("/expense-categories").WithTags("Finance");
        categories.MapGet("/", async (IExpenseService s, CancellationToken ct) => Results.Ok(await s.ListCategoriesAsync(ct)))
            .RequireAnyPermission(Permissions.ExpenseCreate, Permissions.ExpenseManage);
        categories.MapPost("/", async (SaveExpenseCategoryRequest r, IExpenseService s, CancellationToken ct) =>
                (await s.SaveCategoryAsync(null, r, ct)).ToHttpResult(x => Results.Created($"/api/v1/expense-categories/{x.Id}", x)))
            .WithValidation<SaveExpenseCategoryRequest>().RequirePermission(Permissions.ExpenseManage);
        categories.MapPut("/{id:guid}", async (Guid id, SaveExpenseCategoryRequest r, IExpenseService s, CancellationToken ct) => (await s.SaveCategoryAsync(id, r, ct)).ToHttpResult())
            .WithValidation<SaveExpenseCategoryRequest>().RequirePermission(Permissions.ExpenseManage);
        categories.MapPost("/{id:guid}/deactivate", async (Guid id, IExpenseService s, CancellationToken ct) => (await s.SetCategoryActiveAsync(id, false, ct)).ToHttpResult())
            .RequirePermission(Permissions.ExpenseManage);
        categories.MapPost("/{id:guid}/activate", async (Guid id, IExpenseService s, CancellationToken ct) => (await s.SetCategoryActiveAsync(id, true, ct)).ToHttpResult())
            .RequirePermission(Permissions.ExpenseManage);
        return api;
    }
}

/// <summary>
/// Схема <c>finance</c>. Платежи и журнал операций не удаляются; сторно — не более одного на платёж
/// (уникальный индекс по <c>reversal_of_payment_id</c>); номер операции провайдера уникален (для онлайн-оплаты).
/// </summary>
internal sealed class FinanceModelConfigurator : IModelConfigurator
{
    public const string Schema = "finance";

    public void Configure(ModelBuilder b)
    {
        b.Entity<Payment>(e =>
        {
            e.ToTable("payments", Schema, t =>
            {
                t.HasCheckConstraint("ck_payments_amount", "(type = 'Reversal' AND amount < 0) OR (type <> 'Reversal' AND amount > 0)");
                t.HasCheckConstraint("ck_payments_refunded", "refunded_amount >= 0 AND refunded_amount <= abs(amount)");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Method).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Channel).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Amount).HasPrecision(14, 2);
            e.Property(x => x.RefundedAmount).HasPrecision(14, 2);
            e.Property(x => x.Comment).HasMaxLength(500);
            e.Property(x => x.Version).IsRowVersion();
            e.Ignore(x => x.IsReceived);
            e.Ignore(x => x.NetAmount);
            e.Ignore(x => x.IsRefundable);
            e.HasMany(x => x.Transactions).WithOne().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
            e.Navigation(x => x.Transactions).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.HasIndex(x => x.BookingId);
            e.HasIndex(x => new { x.GuestId, x.PaidAt });
            e.HasIndex(x => new { x.HotelId, x.PaidAt });
            e.HasIndex(x => x.ReversalOfPaymentId).IsUnique().HasFilter("reversal_of_payment_id IS NOT NULL");
        });

        b.Entity<PaymentTransaction>(e =>
        {
            e.ToTable("payment_transactions", Schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Amount).HasPrecision(14, 2);
            e.Property(x => x.Provider).HasMaxLength(20);
            e.Property(x => x.ProviderTxnId).HasMaxLength(100);
            e.Property(x => x.Comment).HasMaxLength(500);
            e.HasIndex(x => new { x.PaymentId, x.OccurredAt });
            e.HasIndex(x => new { x.Provider, x.ProviderTxnId, x.Kind }).IsUnique().HasFilter("provider_txn_id IS NOT NULL");
        });

        b.Entity<Refund>(e =>
        {
            e.ToTable("refunds", Schema, t => t.HasCheckConstraint("ck_refunds_amount", "amount > 0"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Amount).HasPrecision(14, 2);
            e.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            e.Property(x => x.Method).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.DecisionComment).HasMaxLength(500);
            e.Property(x => x.Version).IsRowVersion();
            e.Ignore(x => x.IsActive);
            e.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.PaymentId);
            e.HasIndex(x => new { x.HotelId, x.Status, x.RequestedAt });
            e.HasIndex(x => x.BookingId);
        });

        b.Entity<ExpenseCategory>(e =>
        {
            e.ToTable("expense_categories", Schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Code).HasMaxLength(40).IsRequired();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Version).IsRowVersion();
            e.HasIndex(x => new { x.HotelId, x.Code }).IsUnique();
        });

        b.Entity<Expense>(e =>
        {
            e.ToTable("expenses", Schema, t => t.HasCheckConstraint("ck_expenses_amount", "amount > 0"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Amount).HasPrecision(14, 2);
            e.Property(x => x.Method).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Supplier).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(500).IsRequired();
            e.Property(x => x.VoidReason).HasMaxLength(500);
            e.Property(x => x.Version).IsRowVersion();
            e.HasOne<ExpenseCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.HotelId, x.Date });
            e.HasIndex(x => x.CreatedBy);
        });
    }
}

/// <summary>Категории расходов по умолчанию (владелец может переименовать, отключить, добавить свои).</summary>
internal sealed class ExpenseCategorySeeder(DbContext db, IHotelContext hotel) : IDataSeeder
{
    private static readonly (string Code, string Name)[] Defaults =
    [
        ("utilities", "Коммунальные услуги"),
        ("laundry", "Бельё и прачечная"),
        ("cleaning", "Моющие и расходные материалы"),
        ("repair", "Ремонт и обслуживание"),
        ("salary", "Зарплата"),
        ("food", "Продукты (завтраки)"),
        ("marketing", "Реклама"),
        ("other", "Прочее"),
    ];

    public int Order => 30;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var hotelId = await hotel.GetHotelIdAsync(cancellationToken);
        var existing = await db.Set<ExpenseCategory>().Where(c => c.HotelId == hotelId).Select(c => c.Code).ToListAsync(cancellationToken);
        var order = 0;
        foreach (var (code, name) in Defaults)
        {
            order += 10;
            if (!existing.Contains(code))
            {
                db.Set<ExpenseCategory>().Add(new ExpenseCategory(hotelId, code, name, order));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
