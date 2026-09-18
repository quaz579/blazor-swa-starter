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
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        handler(request, cancellationToken);
}
