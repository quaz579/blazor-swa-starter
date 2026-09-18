using System.Net.Sockets;
using Xunit;

namespace App.Tests.Storage;

/// <summary>
/// A <see cref="FactAttribute"/> that skips (rather than fails) when Azurite is not reachable
/// on <c>127.0.0.1:10000</c> — CI starts Azurite before running the suite, a bare local
/// <c>dotnet test</c> may not.
/// </summary>
public sealed class AzuriteFactAttribute : FactAttribute
{
    public AzuriteFactAttribute()
    {
        if (!IsAzuriteReachable())
        {
            Skip = "Azurite is not reachable on 127.0.0.1:10000 (start it with `npx azurite`) — skipping.";
        }
    }

    private static bool IsAzuriteReachable()
    {
        try
        {
            using var client = new TcpClient();
            return client.ConnectAsync(System.Net.IPAddress.Loopback, 10000).Wait(TimeSpan.FromMilliseconds(500));
        }
        catch
        {
            return false;
        }
    }
}
