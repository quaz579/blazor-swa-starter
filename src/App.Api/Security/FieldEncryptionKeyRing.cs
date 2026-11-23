using System.Security.Cryptography;

namespace App.Api.Security;

public sealed class FieldEncryptionKeyRing
{
    private readonly Dictionary<string, byte[]> _keys;

    public string ActiveKeyId { get; }

    public FieldEncryptionKeyRing(string activeKeyId, IReadOnlyDictionary<string, byte[]> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (!IsValidKeyId(activeKeyId))
            throw new ArgumentException("Key IDs must be alphanumeric.", nameof(activeKeyId));

        _keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (keyId, key) in keys)
        {
            if (!IsValidKeyId(keyId))
                throw new ArgumentException("Key IDs must be alphanumeric.", nameof(keys));
            if (key is null || key.Length != 32)
                throw new ArgumentException("Each key must contain exactly 32 bytes.", nameof(keys));
            _keys.Add(keyId, key.ToArray());
        }

        if (!_keys.ContainsKey(activeKeyId))
            throw new ArgumentException("The active key ID must exist in the key ring.", nameof(activeKeyId));

        ActiveKeyId = activeKeyId;
    }

    internal bool TryGetKey(string keyId, out byte[] key)
    {
        if (_keys.TryGetValue(keyId, out var found))
        {
            key = found.ToArray();
            return true;
        }

        key = [];
        return false;
    }

    internal bool ContainsDevelopmentKey()
    {
        var developmentKey = FieldEncryptionConfiguration.GetDevelopmentKey();
        try
        {
            return _keys.Any(pair => CryptographicOperations.FixedTimeEquals(pair.Value, developmentKey));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(developmentKey);
        }
    }

    internal bool ContainsDevelopmentKeyId => _keys.ContainsKey(FieldEncryptionConfiguration.DevelopmentKeyId);

    private static bool IsValidKeyId(string? keyId) =>
        !string.IsNullOrEmpty(keyId) && keyId.All(char.IsAsciiLetterOrDigit);
}
