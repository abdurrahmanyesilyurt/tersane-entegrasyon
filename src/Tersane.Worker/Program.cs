using Tersane.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddTersaneIntegration(builder.Configuration);
builder.Services.AddHostedService<OutboxWorker>();
builder.Services.AddHostedService<ErpSyncWorker>();

builder.Build().Run();
