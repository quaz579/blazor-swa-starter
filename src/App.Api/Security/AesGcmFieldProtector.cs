using System.Security.Cryptography;
using System.Text;
using App.Core.Security;

namespace App.Api.Security;

public sealed class AesGcmFieldProtector : IFieldProtector
{
    private const string EnvelopeVersion = "fe1";
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly FieldEncryptionKeyRing? _keyRing;

    public AesGcmFieldProtector(FieldEncryptionKeyRing? keyRing)
    {
        _keyRing = keyRing;
    }

    public string Protect(string plaintext, string purpose)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ValidatePurpose(purpose);
        var keyRing = _keyRing;
        if (keyRing is null)
            throw new FieldEncryptionException(FieldEncryptionFailure.NotConfigured);

        if (!keyRing.TryGetKey(keyRing.ActiveKeyId, out var key))
            throw new FieldEncryptionException(FieldEncryptionFailure.NotConfigured);

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var associatedData = CreateAssociatedData(keyRing.ActiveKeyId, purpose);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plaintextBytes, ciphertext, tag, associatedData);
            var payload = new byte[NonceSize + TagSize + ciphertext.Length];
            nonce.CopyTo(payload, 0);
            tag.CopyTo(payload, NonceSize);
            ciphertext.CopyTo(payload, NonceSize + TagSize);
            return $"{EnvelopeVersion}.{keyRing.ActiveKeyId}.{ToBase64Url(payload)}";
        }
        catch (CryptographicException)
        {
            throw new FieldEncryptionException(FieldEncryptionFailure.InvalidProtectedValue);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintextBytes);
            CryptographicOperations.ZeroMemory(associatedData);
        }
    }

    public string Unprotect(string envelope, string purpose)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ValidatePurpose(purpose);
        var keyRing = _keyRing;
        if (keyRing is null)
            throw new FieldEncryptionException(FieldEncryptionFailure.NotConfigured);

        var parts = envelope.Split('.', StringSplitOptions.None);
        if (parts.Length != 3 || parts[0] != EnvelopeVersion || !IsValidKeyId(parts[1]))
            throw new FieldEncryptionException(FieldEncryptionFailure.InvalidProtectedValue);

        if (!keyRing.TryGetKey(parts[1], out var key))
            throw new FieldEncryptionException(FieldEncryptionFailure.InvalidProtectedValue);

        byte[] payload;
        try
        {
            payload = FromBase64Url(parts[2]);
        }
        catch (FormatException)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new FieldEncryptionException(FieldEncryptionFailure.InvalidProtectedValue);
        }

        if (payload.Length < NonceSize + TagSize)
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(payload);
            throw new FieldEncryptionException(FieldEncryptionFailure.InvalidProtectedValue);
        }

        var ciphertextLength = payload.Length - NonceSize - TagSize;
        var plaintextBytes = new byte[ciphertextLength];
        var associatedData = CreateAssociatedData(parts[1], purpose);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(
                payload.AsSpan(0, NonceSize),
                payload.AsSpan(NonceSize + TagSize, ciphertextLength),
                payload.AsSpan(NonceSize, TagSize),
                plaintextBytes,
                associatedData);
            return StrictUtf8.GetString(plaintextBytes);
        }
        catch (CryptographicException)
        {
            throw new FieldEncryptionException(FieldEncryptionFailure.InvalidProtectedValue);
        }
        catch (DecoderFallbackException)
        {
            throw new FieldEncryptionException(FieldEncryptionFailure.InvalidProtectedValue);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(payload);
            CryptographicOperations.ZeroMemory(plaintextBytes);
            CryptographicOperations.ZeroMemory(associatedData);
        }
    }

    private static byte[] CreateAssociatedData(string keyId, string purpose) =>
        Encoding.UTF8.GetBytes($"{EnvelopeVersion}\0{keyId}\0{purpose}");

    private static string ToBase64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        if (value.Length == 0 || value.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')) || value.Length % 4 == 1)
            throw new FormatException();

        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 += new string('=', (4 - base64.Length % 4) % 4);
        return Convert.FromBase64String(base64);
    }

    private static bool IsValidKeyId(string keyId) =>
        keyId.Length > 0 && keyId.All(char.IsAsciiLetterOrDigit);

    private static void ValidatePurpose(string purpose)
    {
        if (string.IsNullOrWhiteSpace(purpose))
            throw new ArgumentException("Purpose must not be null, empty, or whitespace.", nameof(purpose));
    }
}
