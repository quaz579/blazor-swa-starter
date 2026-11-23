using Microsoft.Extensions.Logging;

namespace App.Api.Security;

public sealed class FieldEncryptionStatus
{
    public const string DevelopmentDefault = "development-default";
    public const string Configured = "configured";
    public const string NotConfigured = "not-configured";

    public string Mode { get; }
    private int _developmentWarningLogged;

    internal FieldEncryptionStatus(string mode)
    {
        Mode = mode;
    }

    internal void LogDevelopmentWarning(ILogger logger)
    {
        if (Mode == DevelopmentDefault && Interlocked.Exchange(ref _developmentWarningLogged, 1) == 0)
        {
            logger.LogWarning(
                "Field encryption is using the public local development key. Configure FIELD_ENCRYPTION_ACTIVE_KEY_ID and FIELD_ENCRYPTION_KEYS__<id> for deployed use.");
        }
    }
}
