namespace App.Core.Models;

public static class CreateItemRequestValidator
{
    public const int MaxNameLength = 100;
    public const int MaxDescriptionLength = 1000;

    public static string? Validate(CreateItemRequest? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Name))
        {
            return "Name is required.";
        }

        if (request.Name.Trim().Length > MaxNameLength)
        {
            return $"Name must be {MaxNameLength} characters or fewer.";
        }

        if (request.Description is not null && request.Description.Length > MaxDescriptionLength)
        {
            return $"Description must be {MaxDescriptionLength} characters or fewer.";
        }

        return null;
    }
}
