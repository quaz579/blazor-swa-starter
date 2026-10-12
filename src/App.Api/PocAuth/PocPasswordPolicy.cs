namespace App.Api.PocAuth;

public static class PocPasswordPolicy
{
    public const int MinLength = 8;
    public const int MaxLength = 128;

    public static string? Validate(string? candidate, string? currentPassword = null)
    {
        if (string.IsNullOrEmpty(candidate))
        {
            return "A new password is required.";
        }

        if (candidate.Length < MinLength)
        {
            return $"The new password must be at least {MinLength} characters.";
        }

        if (candidate.Length > MaxLength)
        {
            return $"The new password must be at most {MaxLength} characters.";
        }

        if (!candidate.Any(char.IsLetter) || !candidate.Any(char.IsDigit))
        {
            return "The new password must contain at least one letter and one digit.";
        }

        if (currentPassword is not null && candidate == currentPassword)
        {
            return "The new password must be different from the current password.";
        }

        return null;
    }
}
