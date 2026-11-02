namespace App.Web.Services;

/// <summary>Thin typed client over the <c>/api/health</c> endpoint.</summary>
public sealed class HealthApiClient
{
    private readonly HttpClient _http;

    public HealthApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync("api/health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            return false;
        }
    }
}
