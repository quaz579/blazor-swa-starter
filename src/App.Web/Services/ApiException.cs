namespace App.Web.Services;

/// <summary>
/// A non-2xx response from the API. Worker 2.x preserves the real status code on error
/// paths, so callers must surface it rather than swallowing the failure.
/// </summary>
public sealed class ApiException : Exception
{
    public int StatusCode { get; }

    public ApiException(int statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }
}
