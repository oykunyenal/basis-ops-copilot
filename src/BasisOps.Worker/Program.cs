var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

// Health scan and action executor services are added in step 4.

var host = builder.Build();
host.Run();
