namespace App.Api.PocAuth;

public sealed record PocSeedUser(
    string Username,
    string DisplayName,
    string[] Roles,
    string Salt,
    string PasswordHash,
    int Iterations);
