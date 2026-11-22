using FluentValidation;
using Hms.Modules.Audit.Contracts;
using Hms.Modules.Hotel.Contracts;
using Hms.Modules.Identity.Contracts;
using Hms.Modules.Reporting.Application;
using Hms.Modules.Reporting.Contracts;
using Hms.Platform.Http;
using Hms.Platform.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Hms.Modules.Reporting.Infrastructure;

public static class ReportingModule
{
    /// <summary>Финансовые отчёты: владельцу и сотруднику с правом «Финансовые отчёты». Остальные — только владельцу (ТЗ §12, api-specification §15).</summary>
    private static readonly HashSet<string> FinancialReports = [ReportTypes.Financial, ReportTypes.Bookings, ReportTypes.Payments, ReportTypes.Refunds];

    public static IServiceCollection AddReportingModule(this IServiceCollection services)
    {
        services.AddScoped<ReportData>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddSingleton<IReportExporter, ReportExporter>();
        services.AddValidatorsFromAssemblyContaining<ReportQueryValidator>(includeInternalTypes: true);
        return services;
    }

    public static IEndpointRouteBuilder MapReportingEndpoints(this IEndpointRouteBuilder api)
    {
        var dash = api.MapGroup("/dashboard").WithTags("Reporting");
        dash.MapGet("/owner", async ([AsParameters] DashboardQuery q, IDashboardService s, CancellationToken ct) => (await s.OwnerAsync(q, ct)).ToHttpResult())
            .RequirePermission(Permissions.DashboardOwnerView);
        dash.MapGet("/operations", async (DateOnly? date, IDashboardService s, CancellationToken ct) => Results.Ok(await s.OperationsAsync(date, ct)))
            .RequireAnyPermission(Permissions.DashboardOperationsView, Permissions.DashboardOwnerView);

        var reports = api.MapGroup("/reports").WithTags("Reporting");
        reports.MapGet("/{type}", async (string type, [AsParameters] ReportQuery q, IReportService s, ICurrentUser user, IUserAccessProvider access, CancellationToken ct) =>
                await CanViewAsync(type, user, access, ct) ? (await s.BuildAsync(type, q, ct)).ToHttpResult() : Forbidden())
            .WithValidation<ReportQuery>()
            .RequireAnyPermission(Permissions.DashboardOwnerView, Permissions.FinanceReportView);
        reports.MapGet("/{type}/export", async (string type, string? format, [AsParameters] ReportQuery q, IReportService s, IReportExporter exporter,
                IHotelContext hotel, ICurrentUser user, IUserAccessProvider access, IAuditTrail audit, CancellationToken ct) =>
            {
                if (!await CanViewAsync(type, user, access, ct))
                {
                    return Forbidden();
                }

                if (format is not ("xlsx" or "pdf"))
                {
                    return ReportErrors.UnknownFormat.ToProblem();
                }

                var built = await s.BuildAsync(type, q, ct);
                if (built.IsFailure)
                {
                    return built.Error.ToProblem();
                }

                var report = built.Value;
                var hotelName = (await hotel.GetSettingsAsync(ct)).HotelName;
                var bytes = format == "xlsx" ? exporter.ToExcel(report, hotelName) : exporter.ToPdf(report, hotelName);
                await audit.RecordNowAsync(new AuditEntry("report.exported", "Report", type, After: new { format, report.From, report.To, report.Filters }), ct);
                var name = $"report-{type}-{report.From:yyyy-MM-dd}_{report.To:yyyy-MM-dd}.{format}";
                return Results.File(bytes, format == "xlsx" ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" : "application/pdf", name);
            })
            .WithValidation<ReportQuery>()
            .RequirePermission(Permissions.ReportExport);
        return api;
    }

    private static IResult Forbidden() =>
        Hms.SharedKernel.Error.Forbidden("auth.forbidden", "Недостаточно прав для этого отчёта.").ToProblem();

    private static async Task<bool> CanViewAsync(string type, ICurrentUser user, IUserAccessProvider access, CancellationToken ct)
    {
        if (user.UserId is not { } id || await access.GetAsync(id, ct) is not { } state)
        {
            return false;
        }

        return state.Permissions.Contains(Permissions.DashboardOwnerView)
            || (FinancialReports.Contains(type) && state.Permissions.Contains(Permissions.FinanceReportView));
    }
}
