using Azure;
using Azure.Data.Tables;

namespace App.Api.PocAuth;

public sealed record PocPrincipal(string Username, string[] Roles);

public sealed class PocUserEntity : ITableEntity
{
    public const string Partition = "user";

    public string PartitionKey { get; set; } = Partition;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Roles { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Salt { get; set; } = string.Empty;
    public int Iterations { get; set; }
    public string Algorithm { get; set; } = PocPasswordHasher.Algorithm;

    public string[] GetRoles() => Roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string KeyFor(string username) => username.Trim().ToLowerInvariant();
}

public sealed class PocSessionEntity : ITableEntity
{
    public const string Partition = "session";

    public string PartitionKey { get; set; } = Partition;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Username { get; set; } = string.Empty;
    public string Roles { get; set; } = string.Empty;
    public DateTimeOffset ExpiresUtc { get; set; }
}
