using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace App.Api.Seeding;

public sealed record SeedRequestOrigin(IPAddress? Peer, string[] ForwardedFor)
{
    public static SeedRequestOrigin Read(HttpRequest request, FunctionContext? context)
    {
        var origin = request.HttpContext.Features.Get<SeedRequestOrigin>()
            ?? new SeedRequestOrigin(request.HttpContext.Connection.RemoteIpAddress,
                request.Headers["X-Forwarded-For"].SelectMany(value => (value ?? "").Split(',')).ToArray());
        if (context is null) return origin;
        if (!context.BindingContext.BindingData.TryGetValue("Headers", out var rawHeaders) || rawHeaders is null)
            return origin with { ForwardedFor = ["invalid"] };
        try
        {
            using var headers = JsonDocument.Parse(rawHeaders?.ToString() ?? "{}");
            var original = headers.RootElement.EnumerateObject()
                .Where(header => header.Name.Equals("X-Forwarded-For", StringComparison.OrdinalIgnoreCase))
                .SelectMany(header => header.Value.ValueKind == JsonValueKind.String
                    ? header.Value.GetString()!.Split(',') : new[] { "invalid" });
            return origin with { ForwardedFor = origin.ForwardedFor.Concat(original).ToArray() };
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return origin with { ForwardedFor = ["invalid"] };
        }
    }
}

public sealed class SeedRequestOriginFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, continuation) =>
        {
            context.Features.Set(new SeedRequestOrigin(context.Connection.RemoteIpAddress,
                context.Request.Headers["X-Forwarded-For"].SelectMany(value => (value ?? "").Split(',')).ToArray()));
            await continuation(context);
        });
        next(app);
    };
}
