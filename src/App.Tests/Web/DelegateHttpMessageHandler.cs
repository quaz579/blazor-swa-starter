namespace App.Tests.Web;

/// <summary>
/// An <see cref="HttpMessageHandler"/> whose response is supplied by a per-test delegate, so
/// <see cref="App.Web.Services.ItemsApiClient"/> and any bUnit component that injects it can be
/// exercised against canned or deferred (via a <see cref="TaskCompletionSource{T}"/>) responses
/// without a real API.
/// </summary>
internal sealed class DelegateHttpMessageHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
{
    public static HttpClient CreateClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) =>
        new(new DelegateHttpMessageHandler(handle)) { BaseAddress = new Uri("https://api.test/") };

    public static HttpClient CreateRecordingClient(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle, out List<string> requests)
    {
        var seen = new List<string>();
        requests = seen;
        return CreateClient((req, _) =>
        {
            seen.Add($"{req.Method} {req.RequestUri!.AbsolutePath}");
            return handle(req);
        });
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        handler(request, cancellationToken);
}
