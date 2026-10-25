using System.Net.Http.Json;
using System.Text.Json;
using App.Core.Models;

namespace App.Web.Services;

public sealed class ItemsApiClient
{
    private readonly HttpClient _http;

    public ItemsApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<Item>> GetItemsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("api/items", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var items = await response.Content.ReadFromJsonAsync<List<Item?>>(cancellationToken: cancellationToken)
            ?? throw new ApiException((int)response.StatusCode, "The API returned a null item list.");
        return items.ConvertAll(item => ValidateItem(item, (int)response.StatusCode,
            "The API returned an item list containing a null item."));
    }

    public async Task<Item> CreateItemAsync(CreateItemRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsJsonAsync("api/items", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var item = await response.Content.ReadFromJsonAsync<Item>(cancellationToken: cancellationToken);
        return ValidateItem(item, (int)response.StatusCode, "The API returned an empty body for a created item.");
    }

    public async Task DeleteItemAsync(string id, CancellationToken cancellationToken = default)
    {
        using var response = await _http.DeleteAsync($"api/items/{id}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private static Item ValidateItem(Item? item, int statusCode, string nullMessage)
    {
        if (item is null)
        {
            throw new ApiException(statusCode, nullMessage);
        }

        if (item.Id is null || item.Name is null)
        {
            throw new ApiException(statusCode, "The API returned an item with a null id or name.");
        }

        return item;
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
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
        }

        throw new ApiException((int)response.StatusCode, message);
    }

    private sealed class ErrorBody
    {
        public string? Error { get; set; }
    }
}
