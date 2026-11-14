using FakeSap.Chaos;
using FakeSap.Endpoints;
using FakeSap.Simulation;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<SapLandscape>();
builder.Services.AddHostedService<SimulationEventGenerator>();

builder.Services.AddOptions<ChaosOptions>()
    .Bind(builder.Configuration.GetSection(ChaosOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<SimulationOptions>()
    .Bind(builder.Configuration.GetSection(SimulationOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseMiddleware<ChaosMiddleware>();

app.MapDefaultEndpoints();

app.MapGet("/", () => "FakeSap: SAP system simulator");
app.MapSapEndpoints();

app.Run();
