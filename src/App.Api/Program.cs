using System.Text.Json;
using App.Api.Storage;
using App.Core.Models;
using Azure.Core.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices(services =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();

        // HttpRequestData.ReadFromJsonAsync / HttpResponseData.WriteAsJsonAsync go through this
        // serializer; without it the worker defaults to PascalCase, breaking the camelCase contract.
        services.Configure<WorkerOptions>(workerOptions =>
        {
            workerOptions.Serializer = new JsonObjectSerializer(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        });

        services.AddBlobJsonStore<Item>("items");
    })
    .Build();

host.Run();
