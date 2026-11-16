using System.Text.Json.Serialization;
using BasisOps.Api.Endpoints;
using BasisOps.Api.Http;
using BasisOps.Application;
using BasisOps.Infrastructure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddInfrastructure();
builder.Services.AddApplication();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi(o => o.AddDocumentTransformer<ApiKeySecuritySchemeTransformer>());

builder.Services.AddOptions<ApiKeyAuthOptions>()
    .Bind(builder.Configuration.GetSection(ApiKeyAuthOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<ApiKeyEndpointFilter>();

var app = builder.Build();

// Local development only; production applies a migration bundle in the release pipeline.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await app.Services.MigrateDatabaseAsync();
}

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapDefaultEndpoints();
app.MapGet("/", () => "BasisOps API").ExcludeFromDescription();
app.MapApiV1();

await app.RunAsync();
