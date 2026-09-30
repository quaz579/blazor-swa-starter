namespace App.Api.PocAuth;

public static class PocAuthSettings
{
    public const string EnabledSetting = "POC_AUTH_ENABLED";
    public const string CookieName = "poc_session";
    public const string UsersTable = "pocusers";
    public const string SessionsTable = "pocsessions";
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);

    public static bool IsEnabled =>
        Environment.GetEnvironmentVariable(EnabledSetting)?.Trim().ToLowerInvariant() switch
        {
            "true" => true,
            "false" => false,
            _ => PocAuthSeed.Users.Count > 0,
        };
}
