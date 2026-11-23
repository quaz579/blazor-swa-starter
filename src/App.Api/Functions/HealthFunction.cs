using System.Net;
using App.Api.Security;
using App.Api.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace App.Api.Functions;

public sealed class HealthFunction
{
    private readonly ILogger<HealthFunction> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly FieldEncryptionStatus? _fieldEncryptionStatus;

    public HealthFunction(
        ILogger<HealthFunction> logger,
        TimeProvider? timeProvider = null,
        FieldEncryptionStatus? fieldEncryptionStatus = null)
    {
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _fieldEncryptionStatus = fieldEncryptionStatus;
    }

    /// <summary>GET /api/health</summary>
    [Function("Health")]
    public async Task<HttpResponseData> GetHealth(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        _fieldEncryptionStatus?.LogDevelopmentWarning(_logger);
        _logger.LogInformation("Processing health check request");

        var storage = StorageConnection.Identity();
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            status = "ok",
            utc = _timeProvider.GetUtcNow(),
            storageMode = storage.Mode,
            storageAccount = storage.Account,
            storageIdentity = storage.Identity,
            productionStorageAccount = storage.ProductionAccount,
            fieldEncryptionMode = _fieldEncryptionStatus?.Mode ?? FieldEncryptionStatus.NotConfigured
        }, cancellationToken);
        return response;
    }
}
