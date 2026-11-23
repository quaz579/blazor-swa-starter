# Field encryption

Use App.Core.Security.IFieldProtector for individual sensitive string fields that must be
recoverable later. Keep the protected value in the server-side data model, and use a stable
purpose that includes the field and record identity. Do not encrypt an entire JSON document when
only a few fields are sensitive.

    public sealed class Customer
    {
        public string Id { get; set; } = string.Empty;
        public string EncryptedEmail { get; set; } = string.Empty;
    }

    public sealed class CustomerService(IFieldProtector protector)
    {
        public string ProtectEmail(string customerId, string email) =>
            protector.Protect(email, $"customer:{customerId}:email");

        public string ReadEmail(Customer customer) =>
            protector.Unprotect(customer.EncryptedEmail, $"customer:{customer.Id}:email");
    }

AddFieldEncryption(configuration) is registered by App.Api/Program.cs. It provides
AES-256-GCM protection with a fresh 12-byte nonce, a 16-byte authentication tag, and envelope
format fe1.&lt;key-id&gt;.&lt;base64url(nonce|tag|ciphertext)&gt;. The envelope version, key ID, and
purpose are authenticated. A wrong key, wrong purpose, malformed or modified envelope, or
truncated data throws FieldEncryptionException; callers must not fall back to plaintext.

## Configuration

Set these server-side app settings:

| Setting | Value |
|---|---|
| FIELD_ENCRYPTION_ACTIVE_KEY_ID | Alphanumeric ID used for new protected values |
| FIELD_ENCRYPTION_KEYS__&lt;id&gt; | Base64 encoding of exactly 32 random bytes for that ID |

Generate a production key with openssl rand -base64 32. For example, if the generated key is
assigned ID k2026, set FIELD_ENCRYPTION_ACTIVE_KEY_ID=k2026 and
FIELD_ENCRYPTION_KEYS__k2026=&lt;generated-value&gt; in the Azure Static Web App application
settings. Configure every deployed environment that reads the encrypted data. Keep the value in
Azure app settings or an approved secret manager; never commit it to source, local settings,
Terraform, or public WebAssembly app settings.

Development with no encryption settings uses the documented public development key only when
APP_ENVIRONMENT is not production and storage resolves to loopback Azurite without deployed
storage identity settings. Its key ID is dev and its base64 value is
MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=. This key is public and provides no protection
for real data. The API logs a warning when it selects this default. Production and remote-storage
deployments do not use it. If encryption is not configured, the host still starts and health
reports fieldEncryptionMode=not-configured; Protect and Unprotect throw a
FieldEncryptionException with the required setting names.

## Key rotation

Generate a new random key, add it under a new alphanumeric ID, and keep every old key in the
ring. Change FIELD_ENCRYPTION_ACTIVE_KEY_ID to the new ID after all deployed readers have both
keys. New values use the active key ID; envelopes identify which retained key decrypts older
values. Remove an old key only after every value encrypted with it has been re-encrypted or
deleted. Key IDs are case-sensitive.

## Testing

src/App.Tests/Security/FieldEncryptionTestKit.cs supplies a deterministic test key ring,
CreateProtector, TamperByte, Truncate, and SwapPurpose. Use it to keep tests short and
repeatable:

    var protector = FieldEncryptionTestKit.CreateProtector();
    var envelope = protector.Protect("value", "customer:123:email");
    protector.Unprotect(envelope, "customer:123:email").Should().Be("value");

FieldEncryptionAzuriteTests exercises the real DI registration and blob store, then confirms
the stored JSON does not contain the plaintext. Run the suite with Azurite listening on ports
10000–10002 so [AzuriteFact] tests execute rather than skip; see TESTING.md.
