using System.Diagnostics;

namespace App.Tests.Web;

public sealed class VersionMarkerTests
{
    [Fact]
    public async Task WriteVersionMarker_ScriptAndServingContracts_PassRegressionChecks()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "BlazorSwaStarter.sln")))
            root = root.Parent;
        Assert.NotNull(root);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("node")
            {
                WorkingDirectory = root.FullName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                ArgumentList = { "--test", "scripts/write-version-marker.test.mjs" }
            }
        };
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            Assert.True(process.ExitCode == 0, $"{await output}\n{await error}");
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }
}
