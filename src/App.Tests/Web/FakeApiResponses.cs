using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace App.Tests.Web;

internal static class FakeApiResponses
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static HttpResponseMessage Ok<T>(T body) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(body, options: SerializerOptions),
    };

    public static HttpResponseMessage Created<T>(T body) => new(HttpStatusCode.Created)
    {
        Content = JsonContent.Create(body, options: SerializerOptions),
    };

    public static HttpResponseMessage Error(HttpStatusCode statusCode, string error) => new(statusCode)
    {
        Content = JsonContent.Create(new { error }, options: SerializerOptions),
    };
}
