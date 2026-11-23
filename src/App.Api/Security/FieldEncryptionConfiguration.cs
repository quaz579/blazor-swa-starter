using System.Security.Cryptography;
using App.Api.Storage;
using Microsoft.Extensions.Configuration;

namespace App.Api.Security;

internal static class FieldEncryptionConfiguration
{
    internal const string ActiveKeyIdSetting = "FIELD_ENCRYPTION_ACTIVE_KEY_ID";
    internal const string KeysSetting = "FIELD_ENCRYPTION_KEYS";
    internal const string DevelopmentKeyId = "dev";
    internal const string DevelopmentKeyBase64 = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    internal sealed record Registration(string Mode, FieldEncryptionKeyRing? KeyRing);

    internal static Registration Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var settings = configuration.AsEnumerable().ToArray();
        var encryptionSettings = settings
            .Where(setting => setting.Key.StartsWith("FIELD_ENCRYPTION_", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var activeKeySettingPresent = encryptionSettings.Any(setting =>
            setting.Key.Equals(ActiveKeyIdSetting, StringComparison.OrdinalIgnoreCase));
        var keySettings = new List<KeyValuePair<string, string?>>();
        var unknownSettingPresent = false;

        foreach (var setting in encryptionSettings)
        {
            if (setting.Key.Equals(ActiveKeyIdSetting, StringComparison.OrdinalIgnoreCase))
                continue;
            if (setting.Key.Equals(KeysSetting, StringComparison.OrdinalIgnoreCase))
            {
                unknownSettingPresent |= setting.Value is not null;
                continue;
            }
            if (!setting.Key.StartsWith($"{KeysSetting}:", StringComparison.OrdinalIgnoreCase))
            {
                unknownSettingPresent = true;
                continue;
            }

            keySettings.Add(new KeyValuePair<string, string?>(setting.Key[(KeysSetting.Length + 1)..], setting.Value));
        }

        var hasConfiguration = encryptionSettings.Length > 0;
        if (!hasConfiguration)
            return ResolveWithoutConfiguration(configuration);

        if (unknownSettingPresent || !activeKeySettingPresent)
            throw InvalidConfiguration();

        var activeKeyId = configuration[ActiveKeyIdSetting];
        if (activeKeyId is null || !IsValidKeyId(activeKeyId) || keySettings.Count == 0)
            throw InvalidConfiguration();

        var keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        try
        {
            foreach (var (keyId, encodedKey) in keySettings)
            {
                if (!IsValidKeyId(keyId) || encodedKey is null || encodedKey.Length == 0 || encodedKey.Any(char.IsWhiteSpace))
                    throw InvalidConfiguration();

                byte[] decodedKey;
                try
                {
                    decodedKey = Convert.FromBase64String(encodedKey);
                }
                catch (FormatException)
                {
                    throw InvalidConfiguration();
                }

                if (decodedKey.Length != 32 || !Convert.ToBase64String(decodedKey).Equals(encodedKey, StringComparison.Ordinal))
                {
                    CryptographicOperations.ZeroMemory(decodedKey);
                    throw InvalidConfiguration();
                }

                if (!keys.TryAdd(keyId, decodedKey))
                {
                    CryptographicOperations.ZeroMemory(decodedKey);
                    throw InvalidConfiguration();
                }
            }

            if (!keys.ContainsKey(activeKeyId))
                throw InvalidConfiguration();

            var isProduction = AppEnvironment.IsProduction;
            var ring = new FieldEncryptionKeyRing(activeKeyId, keys);
            if (isProduction && (ring.ContainsDevelopmentKeyId || ring.ContainsDevelopmentKey()))
                return new Registration(FieldEncryptionStatus.NotConfigured, null);

            return new Registration(FieldEncryptionStatus.Configured, ring);
        }
        finally
        {
            ClearKeys(keys);
        }
    }

    internal static byte[] GetDevelopmentKey() => Convert.FromBase64String(DevelopmentKeyBase64);

    private static Registration ResolveWithoutConfiguration(IConfiguration configuration)
    {
        var isProduction = AppEnvironment.IsProduction;
        if (isProduction || HasStorageIdentitySetting(configuration))
            return new Registration(FieldEncryptionStatus.NotConfigured, null);

        try
        {
            var storage = StorageConnection.Resolve();
            var storageUri = new Azure.Storage.Blobs.BlobServiceClient(storage.ConnectionString).Uri;
            if (storage.Mode == "production" && storage.Identity == "unverified" &&
                storage.ProductionAccount is null && storageUri.IsLoopback)
            {
                var key = GetDevelopmentKey();
                try
                {
                    return new Registration(
                        FieldEncryptionStatus.DevelopmentDefault,
                        new FieldEncryptionKeyRing(DevelopmentKeyId, new Dictionary<string, byte[]> { [DevelopmentKeyId] = key }));
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(key);
                }
            }
        }
        catch (StorageConfigurationException)
        {
        }

        return new Registration(FieldEncryptionStatus.NotConfigured, null);
    }

    private static bool HasStorageIdentitySetting(IConfiguration configuration)
    {
        var settings = configuration.AsEnumerable().Select(setting => setting.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return settings.Contains("PRODUCTION_STORAGE_ACCOUNT_NAME") ||
               settings.Contains("PREVIEW_STORAGE_ACCOUNT_NAME") ||
               Environment.GetEnvironmentVariable("PRODUCTION_STORAGE_ACCOUNT_NAME") is not null ||
               Environment.GetEnvironmentVariable("PREVIEW_STORAGE_ACCOUNT_NAME") is not null;
    }

    private static bool IsValidKeyId(string? keyId) =>
        !string.IsNullOrEmpty(keyId) && keyId.All(char.IsAsciiLetterOrDigit);

    private static InvalidOperationException InvalidConfiguration() => new(
        "Field encryption configuration is invalid; check FIELD_ENCRYPTION_ACTIVE_KEY_ID and FIELD_ENCRYPTION_KEYS__<id>.");

    private static void ClearKeys(Dictionary<string, byte[]> keys)
    {
        foreach (var key in keys.Values)
            CryptographicOperations.ZeroMemory(key);
    }
}
