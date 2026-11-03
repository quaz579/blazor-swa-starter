namespace App.Api;

public static class AppEnvironment
{
    public static string Current => Normalize(Environment.GetEnvironmentVariable("APP_ENVIRONMENT"));

    public static bool IsProduction => IsProductionValue(Environment.GetEnvironmentVariable("APP_ENVIRONMENT"));

    internal static string Normalize(string? rawValue) =>
        rawValue?.Trim().ToLowerInvariant() == "production" ? "production" : "development";

    internal static bool IsProductionValue(string? rawValue) => Normalize(rawValue) == "production";
}
