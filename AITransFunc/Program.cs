using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices(services =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();
    })
    //.ConfigureLogging(logging =>
    //{
    //    logging.AddConsole();
    //    logging.AddDebug();
    //    //logging.SetMinimumLevel(LogLevel.Trace);
    //})
    .Build();

host.Run();
