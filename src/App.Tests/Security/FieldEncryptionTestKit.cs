using System.Security.Cryptography;
using System.Text;
using App.Api.Security;

namespace App.Tests.Security;

public static class FieldEncryptionTestKit
{
    public static AesGcmFieldProtector CreateProtector(string activeKeyId = "test", params string[] additionalKeyIds)
    {
        var keyIds = new[] { activeKeyId }.Concat(additionalKeyIds).Distinct(StringComparer.Ordinal);
        var keys = keyIds.ToDictionary(
            keyId => keyId,
            keyId => CreateKey(keyId),
            StringComparer.Ordinal);
        return new AesGcmFieldProtector(new FieldEncryptionKeyRing(activeKeyId, keys));
    }

    public static AesGcmFieldProtector CreateProtector(string activeKeyId, IReadOnlyDictionary<string, byte[]> keyRing) =>
        new(new FieldEncryptionKeyRing(activeKeyId, keyRing));

    public static string KeyBase64(string keyId) => Convert.ToBase64String(CreateKey(keyId));

    public static string TamperByte(string envelope, int byteIndex = -1)
    {
        var parts = envelope.Split('.', StringSplitOptions.None);
        var payload = FromBase64Url(parts[2]);
        var index = byteIndex < 0 ? payload.Length + byteIndex : byteIndex;
        if (index < 0 || index >= payload.Length)
            throw new ArgumentOutOfRangeException(nameof(byteIndex));
        payload[index] ^= 1;
        return parts[0] + "." + parts[1] + "." + ToBase64Url(payload);
    }

    public static string Truncate(string envelope)
    {
        var parts = envelope.Split('.', StringSplitOptions.None);
        var payload = FromBase64Url(parts[2]);
        if (payload.Length == 0)
            throw new ArgumentException("Envelope payload is empty.", nameof(envelope));
        return parts[0] + "." + parts[1] + "." + ToBase64Url(payload[..^1]);
    }

    public static string SwapPurpose(string purpose) => purpose + ".swapped";

    private static byte[] CreateKey(string keyId) =>
        SHA256.HashData(Encoding.UTF8.GetBytes("blazor-swa-starter-test-key:" + keyId));

    private static string ToBase64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 += new string('=', (4 - base64.Length % 4) % 4);
        return Convert.FromBase64String(base64);
    }
}
