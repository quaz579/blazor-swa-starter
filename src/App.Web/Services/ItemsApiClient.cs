using System.Net.Http.Json;
using App.Core.Models;

namespace App.Web.Services;

/// <summary>Thin typed client over the <c>/api/items</c> and <c>/api/health</c> endpoints.</summary>
public sealed class ItemsApiClient
{
    private readonly HttpClient _http;

    public ItemsApiClient(HttpClient http)
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
        catch (HttpRequestException)
        {
            return false;
        }
    }

    public async Task<List<Item>> GetItemsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("api/items", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<Item>>(cancellationToken: cancellationToken) ?? [];
    }

    public async Task<Item> CreateItemAsync(CreateItemRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsJsonAsync("api/items", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<Item>(cancellationToken: cancellationToken)
            ?? throw new ApiException((int)response.StatusCode, "The API returned an empty body for a created item.");
    }

    public async Task DeleteItemAsync(string id, CancellationToken cancellationToken = default)
    {
        using var response = await _http.DeleteAsync($"api/items/{id}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var message = $"Request failed with status {(int)response.StatusCode}.";
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ErrorBody>(cancellationToken: cancellationToken);
            if (!string.IsNullOrWhiteSpace(problem?.Error))
            {
                message = problem.Error;
            }
        }
        catch
        {
            // Body wasn't JSON shaped like {"error": "..."}; fall back to the generic message above.
        }

        throw new ApiException((int)response.StatusCode, message);
    }

    private sealed class ErrorBody
    {
        public string? Error { get; set; }
    }
}
