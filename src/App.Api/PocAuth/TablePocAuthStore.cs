using App.Api.Storage;
using Azure;
using Azure.Data.Tables;

namespace App.Api.PocAuth;

public sealed class TablePocAuthStore : IPocAuthStore
{
    private readonly TableClient _users;
    private readonly TableClient _sessions;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public TablePocAuthStore(string connectionString)
    {
        _users = StorageClients.CreateTableClient(connectionString, PocAuthSettings.UsersTable);
        _sessions = StorageClients.CreateTableClient(connectionString, PocAuthSettings.SessionsTable);
    }

    public async Task<PocUserEntity?> GetUserAsync(string username, CancellationToken cancellationToken = default)
    {
        await EnsureTablesAsync(cancellationToken);
        var result = await _users.GetEntityIfExistsAsync<PocUserEntity>(
            PocUserEntity.Partition, PocUserEntity.KeyFor(username), cancellationToken: cancellationToken);
        return result.HasValue ? result.Value : null;
    }

    public async Task<bool> AddUserIfMissingAsync(PocUserEntity user, CancellationToken cancellationToken = default)
    {
        await EnsureTablesAsync(cancellationToken);
        try
        {
            await _users.AddEntityAsync(user, cancellationToken);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            return false;
        }
    }

    public async Task PutSessionAsync(PocSessionEntity session, CancellationToken cancellationToken = default)
    {
        await EnsureTablesAsync(cancellationToken);
        await _sessions.UpsertEntityAsync(session, TableUpdateMode.Replace, cancellationToken);
    }

    public async Task<PocSessionEntity?> GetSessionAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        await EnsureTablesAsync(cancellationToken);
        var result = await _sessions.GetEntityIfExistsAsync<PocSessionEntity>(
            PocSessionEntity.Partition, tokenHash, cancellationToken: cancellationToken);
        return result.HasValue ? result.Value : null;
    }

    public async Task DeleteSessionAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        await EnsureTablesAsync(cancellationToken);
        await _sessions.DeleteEntityAsync(PocSessionEntity.Partition, tokenHash, cancellationToken: cancellationToken);
    }

    private async Task EnsureTablesAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            await _users.CreateIfNotExistsAsync(cancellationToken);
            await _sessions.CreateIfNotExistsAsync(cancellationToken);
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }
}
