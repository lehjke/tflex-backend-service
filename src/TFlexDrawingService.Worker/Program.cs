using TFlexDrawingService.Infrastructure.Configuration;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "TFlexDrawingService.Worker");
builder.Services.AddDrawingInfrastructure(builder.Configuration, builder.Environment.ContentRootPath);
builder.Services.AddDrawingGenerationWorker();

var host = builder.Build();
host.Run();
