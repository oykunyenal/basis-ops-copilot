using BasisOps.Application;
using BasisOps.Application.Actions;
using BasisOps.Infrastructure;
using BasisOps.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddInfrastructure();
builder.Services.AddApplication();

builder.Services.AddOptions<HealthScanOptions>()
    .Bind(builder.Configuration.GetSection(HealthScanOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<ActionExecutorOptions>()
    .Bind(builder.Configuration.GetSection(ActionExecutorOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<ActionExecutorWorkerOptions>()
    .Bind(builder.Configuration.GetSection(ActionExecutorWorkerOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddHostedService<HealthScanWorker>();
builder.Services.AddHostedService<ActionExecutorWorker>();

var host = builder.Build();
await host.RunAsync();
