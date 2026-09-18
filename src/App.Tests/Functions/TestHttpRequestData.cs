using System.Text.Json;
using Azure.Core.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace App.Tests.Functions;

/// <summary>
/// Builds fake isolated-worker <see cref="HttpRequestData"/>/<see cref="HttpResponseData"/> instances
/// for testing Functions without a running host. The worker's <c>ReadFromJsonAsync</c>/
/// <c>WriteAsJsonAsync</c> extensions resolve their <see cref="ObjectSerializer"/> via
/// <c>FunctionContext.InstanceServices</c>, so that has to be wired with the same camelCase
/// serializer Program.cs registers, or those extensions throw at test time.
/// </summary>
internal static class TestHttpRequestData
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static FunctionContext CreateContext()
    {
        var context = Substitute.For<FunctionContext>();
        var workerOptions = Options.Create(new WorkerOptions
        {
            Serializer = new JsonObjectSerializer(SerializerOptions),
        });
        var services = new ServiceCollection()
            .AddSingleton<IOptions<WorkerOptions>>(workerOptions)
            .BuildServiceProvider();
        context.InstanceServices.Returns(services);
        return context;
    }

    /// <summary>Creates a fake request whose <see cref="HttpRequestData.CreateResponse()"/> returns a working fake response.</summary>
    public static HttpRequestData CreateRequest(string method = "GET", string url = "https://localhost/api/items", object? jsonBody = null)
    {
        var context = CreateContext();

        var response = Substitute.For<HttpResponseData>(context);
        response.Headers.Returns(new HttpHeadersCollection());
        response.Body = new MemoryStream();

        var bodyBytes = jsonBody is null
            ? []
            : JsonSerializer.SerializeToUtf8Bytes(jsonBody, SerializerOptions);

        var request = Substitute.For<HttpRequestData>(context);
        request.Method.Returns(method);
        request.Url.Returns(new Uri(url));
        request.Headers.Returns(new HttpHeadersCollection());
        request.Query.Returns(new System.Collections.Specialized.NameValueCollection());
        request.Body.Returns(new MemoryStream(bodyBytes));
        request.CreateResponse().Returns(response);

        return request;
    }

    public static async Task<T?> ReadBodyAsync<T>(HttpResponseData response)
    {
        response.Body.Position = 0;
        return await JsonSerializer.DeserializeAsync<T>(response.Body, SerializerOptions);
    }

    /// <summary>Reads the response body as raw JSON so tests can assert on exact (camelCase) property names.</summary>
    public static async Task<JsonDocument> ReadBodyAsJsonAsync(HttpResponseData response)
    {
        response.Body.Position = 0;
        return await JsonDocument.ParseAsync(response.Body);
    }
}
