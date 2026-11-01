using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace App.Api.Functions;

public sealed class HealthFunction
{
    private readonly ILogger<HealthFunction> _logger;
    private readonly TimeProvider _timeProvider;

    public HealthFunction(ILogger<HealthFunction> logger, TimeProvider? timeProvider = null)
    {
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>GET /api/health</summary>
    [Function("Health")]
    public async Task<HttpResponseData> GetHealth(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing health check request");

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { status = "ok", utc = _timeProvider.GetUtcNow() }, cancellationToken);
        return response;
    }
}
