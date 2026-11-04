using System.Net;
using App.Api.Seeding;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace App.Api.Functions;

public sealed class SeedFunctions(SeedService seed)
{
    [Function("ResetToSeed")]
    public async Task<IActionResult> Reset(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "__test/reset-to-seed")] HttpRequest request,
        CancellationToken cancellationToken, FunctionContext? context = null)
    {
        if (request.Method != "POST" || AppEnvironment.IsProduction
            || Environment.GetEnvironmentVariable("TEST_RESET_ENABLED") != "true")
            return new StatusCodeResult(404);
        var origin = SeedRequestOrigin.Read(request, context);
        if (!IsLoopback(origin.Peer) || origin.ForwardedFor.Any(hop =>
                !IPAddress.TryParse(hop.Trim(), out var address) || !IsLoopback(address)))
            return new StatusCodeResult(404);
        var result = await seed.ResetAsync(cancellationToken);
        return result is null ? new ConflictObjectResult(new { busy = true }) : new OkObjectResult(result);
    }

    [Function("HealthReady")]
    public IActionResult Ready(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health/ready")] HttpRequest request) =>
        seed.Ready ? new OkObjectResult(new { ready = true, seed_version = seed.Version })
            : new ObjectResult(new { ready = false }) { StatusCode = 503 };

    private static bool IsLoopback(IPAddress? address) => address is not null
        && IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);
}
