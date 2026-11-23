namespace App.Core.Security;

public enum FieldEncryptionFailure
{
    NotConfigured,
    InvalidProtectedValue
}

public sealed class FieldEncryptionException : Exception
{
    public FieldEncryptionFailure Failure { get; }

    public FieldEncryptionException(FieldEncryptionFailure failure) : base(MessageFor(failure))
    {
        Failure = failure;
    }

    private static string MessageFor(FieldEncryptionFailure failure) => failure switch
    {
        FieldEncryptionFailure.NotConfigured => "field encryption is not configured: set FIELD_ENCRYPTION_ACTIVE_KEY_ID and FIELD_ENCRYPTION_KEYS__<id>",
        FieldEncryptionFailure.InvalidProtectedValue => "The protected value is invalid or cannot be authenticated.",
        _ => "Field encryption failed."
    };
}
