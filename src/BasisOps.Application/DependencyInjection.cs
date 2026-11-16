using BasisOps.Application.Abstractions;
using BasisOps.Application.Actions;
using BasisOps.Application.Audit;
using BasisOps.Application.Dumps;
using BasisOps.Application.Findings;
using BasisOps.Application.Health;
using BasisOps.Application.Reports;
using BasisOps.Application.Users;
using BasisOps.Domain.Rules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BasisOps.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        // Health rules with default thresholds (see PLAN.md, step 2).
        services.AddSingleton<IHealthRule>(new CanceledJobRule());
        services.AddSingleton<IHealthRule>(new DumpFrequencyRule());
        services.AddSingleton<IHealthRule>(new CertificateExpiryRule());
        services.AddSingleton<IHealthRule>(new TransportImportRule());
        services.AddSingleton<IHealthRule>(new WorkProcessUtilizationRule());
        services.AddSingleton<IHealthRule>(new LockedUserRule());

        services.AddOptions<ActionExecutorOptions>();
        services.AddScoped<ActionExecutor>();

        services.AddScoped<ICommandHandler<RunHealthScanCommand, HealthScanResult>, RunHealthScanHandler>();
        services.AddScoped<ICommandHandler<RequestActionCommand, RequestActionResult>, RequestActionHandler>();
        services.AddScoped<ICommandHandler<ApproveActionCommand, ActionRequestDto>, ApproveActionHandler>();
        services.AddScoped<ICommandHandler<RejectActionCommand, ActionRequestDto>, RejectActionHandler>();
        services.AddScoped<ICommandHandler<AnalyzeDumpCommand, DumpAnalysisDto>, AnalyzeDumpHandler>();

        services.AddScoped<IQueryHandler<ListSystemsQuery, IReadOnlyList<SystemSummaryDto>>, ListSystemsHandler>();
        services.AddScoped<IQueryHandler<GetSystemHealthQuery, SystemHealthDto>, GetSystemHealthHandler>();
        services.AddScoped<IQueryHandler<ListFindingsQuery, PagedResult<FindingDto>>, ListFindingsHandler>();
        services.AddScoped<IQueryHandler<GetActionQuery, ActionRequestDto>, GetActionHandler>();
        services.AddScoped<IQueryHandler<ListActionsQuery, IReadOnlyList<ActionRequestDto>>, ListActionsHandler>();
        services.AddScoped<IQueryHandler<GetAuditLogQuery, IReadOnlyList<AuditEntryDto>>, GetAuditLogHandler>();
        services.AddScoped<IQueryHandler<GetDailyReportQuery, DailyReportDto>, GetDailyReportHandler>();
        services.AddScoped<IQueryHandler<ListDumpsQuery, IReadOnlyList<DumpDto>>, ListDumpsHandler>();
        services.AddScoped<IQueryHandler<ListLockedUsersQuery, IReadOnlyList<LockedUserDto>>, ListLockedUsersHandler>();

        return services;
    }
}
