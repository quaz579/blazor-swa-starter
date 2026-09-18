using System.Net;
using App.Core.Models;
using App.Core.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace App.Api.Functions;

public sealed class ItemsFunction
{
    private const int MaxNameLength = 100;
    private const int MaxDescriptionLength = 1000;

    private readonly ILogger<ItemsFunction> _logger;
    private readonly IBlobJsonStore<Item> _store;

    public ItemsFunction(ILogger<ItemsFunction> logger, IBlobJsonStore<Item> store)
    {
        _logger = logger;
        _store = store;
    }

    /// <summary>GET /api/items — newest first.</summary>
    [Function("GetItems")]
    public async Task<HttpResponseData> GetItems(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "items")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        try
        {
            var keys = await _store.ListKeysAsync(cancellationToken: cancellationToken);
            var items = new List<Item>(keys.Count);

            foreach (var key in keys)
            {
                var item = await _store.GetAsync(key, cancellationToken);
                if (item is not null)
                {
                    items.Add(item);
                }
            }

            items.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(items, cancellationToken);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list items");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = "Failed to retrieve items" }, cancellationToken);
            return response;
        }
    }

    /// <summary>GET /api/items/{id}</summary>
    [Function("GetItem")]
    public async Task<HttpResponseData> GetItem(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "items/{id}")] HttpRequestData req,
        string id,
        CancellationToken cancellationToken)
    {
        Item? item;
        try
        {
            item = await _store.GetAsync(id, cancellationToken);
        }
        catch (ArgumentException)
        {
            return await InvalidIdResponse(req, id, cancellationToken);
        }

        if (item is null)
        {
            _logger.LogInformation("Item {Id} not found", id);
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = $"Item '{id}' was not found." }, cancellationToken);
            return notFound;
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(item, cancellationToken);
        return response;
    }

    /// <summary>POST /api/items</summary>
    [Function("CreateItem")]
    public async Task<HttpResponseData> CreateItem(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "items")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        CreateItemRequest? request;
        try
        {
            request = await req.ReadFromJsonAsync<CreateItemRequest>(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rejected malformed create-item body");
            request = null;
        }

        var validationError = Validate(request);
        if (validationError is not null)
        {
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteAsJsonAsync(new { error = validationError }, cancellationToken);
            return badRequest;
        }

        var item = new Item
        {
            Id = Guid.NewGuid().ToString("n"),
            Name = request!.Name!.Trim(),
            Description = request.Description,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await _store.PutAsync(item.Id, item, cancellationToken);
        _logger.LogInformation("Created item {Id}", item.Id);

        var response = req.CreateResponse(HttpStatusCode.Created);
        response.Headers.Add("Location", $"/api/items/{item.Id}");
        await response.WriteAsJsonAsync(item, cancellationToken);
        return response;
    }

    /// <summary>DELETE /api/items/{id}</summary>
    [Function("DeleteItem")]
    public async Task<HttpResponseData> DeleteItem(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "items/{id}")] HttpRequestData req,
        string id,
        CancellationToken cancellationToken)
    {
        bool deleted;
        try
        {
            deleted = await _store.DeleteAsync(id, cancellationToken);
        }
        catch (ArgumentException)
        {
            return await InvalidIdResponse(req, id, cancellationToken);
        }

        if (!deleted)
        {
            _logger.LogInformation("Delete requested for missing item {Id}", id);
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = $"Item '{id}' was not found." }, cancellationToken);
            return notFound;
        }

        _logger.LogInformation("Deleted item {Id}", id);
        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// The store rejects keys containing '/', '\', or '..' as a path-traversal guard.
    /// Surface that as a clean 400 instead of the ArgumentException reaching the host as a 500,
    /// and never echo <see cref="ArgumentException.Message"/> — its "(Parameter 'key')" suffix
    /// names an internal argument the public route calls "id".
    /// </summary>
    private async Task<HttpResponseData> InvalidIdResponse(HttpRequestData req, string id, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Rejected malformed item id {Id}", id);
        var response = req.CreateResponse(HttpStatusCode.BadRequest);
        await response.WriteAsJsonAsync(new { error = "Item id must not contain path separators or '..'." }, cancellationToken);
        return response;
    }

    private static string? Validate(CreateItemRequest? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Name))
        {
            return "Name is required.";
        }

        if (request.Name.Trim().Length > MaxNameLength)
        {
            return $"Name must be {MaxNameLength} characters or fewer.";
        }

        if (request.Description is not null && request.Description.Length > MaxDescriptionLength)
        {
            return $"Description must be {MaxDescriptionLength} characters or fewer.";
        }

        return null;
    }
}
