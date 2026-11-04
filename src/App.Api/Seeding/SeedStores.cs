using App.Api.Storage;
using Azure;
using Azure.Data.Tables;
using Azure.Storage.Blobs;

namespace App.Api.Seeding;

public sealed class BlobSeedStore : ISeedStore
{
    private readonly BlobContainerClient _client;
    public string Kind => "blob";
    public string Name => _client.Name;

    public BlobSeedStore(string name) : this(new BlobServiceClient(StorageConnection.ResolveConnectionString()).GetBlobContainerClient(name)) { }

    internal BlobSeedStore(BlobContainerClient client)
    {
        if (client.Name.StartsWith("azure-webjobs", StringComparison.OrdinalIgnoreCase) || client.Name.StartsWith('$'))
            throw new ArgumentException("System containers cannot be seed stores.");
        _client = client;
    }

    public Task ClearAsync(CancellationToken cancellationToken) =>
        ResetGate.Shared.WriteAsync(() => ClearCoreAsync(cancellationToken), cancellationToken);

    private async Task ClearCoreAsync(CancellationToken cancellationToken)
    {
        if (!await _client.ExistsAsync(cancellationToken)) return;
        await foreach (var blob in _client.GetBlobsAsync(cancellationToken: cancellationToken))
            await _client.DeleteBlobIfExistsAsync(blob.Name, cancellationToken: cancellationToken);
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken)
    {
        if (!await _client.ExistsAsync(cancellationToken)) return 0;
        var count = 0;
        await foreach (var blob in _client.GetBlobsAsync(cancellationToken: cancellationToken)) count++;
        return count;
    }
}

public sealed class TableSeedStore : ISeedStore
{
    private readonly TableClient _client;
    public string Kind => "table";
    public string Name => _client.Name;

    public TableSeedStore(string name) : this(StorageClients.CreateTableClient(name)) { }

    internal TableSeedStore(TableClient client)
    {
        if (client.Name.Equals("pocusers", StringComparison.OrdinalIgnoreCase) || client.Name.Equals("pocsessions", StringComparison.OrdinalIgnoreCase)
            || client.Name.StartsWith("AzureWebJobs", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Auth and system tables cannot be seed stores.");
        _client = client;
    }

    public Task UpsertAsync<T>(T entity, CancellationToken cancellationToken = default) where T : class, ITableEntity =>
        ResetGate.Shared.WriteAsync(async () =>
        {
            await _client.CreateIfNotExistsAsync(cancellationToken);
            await _client.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
        }, cancellationToken);

    public Task DeleteAsync(string partitionKey, string rowKey, CancellationToken cancellationToken = default) =>
        ResetGate.Shared.WriteAsync(async () => { await _client.DeleteEntityAsync(partitionKey, rowKey, ETag.All, cancellationToken); }, cancellationToken);

    public Task ClearAsync(CancellationToken cancellationToken) =>
        ResetGate.Shared.WriteAsync(() => ClearCoreAsync(cancellationToken), cancellationToken);

    private async Task ClearCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var row in _client.QueryAsync<TableEntity>(cancellationToken: cancellationToken))
                await _client.DeleteEntityAsync(row.PartitionKey, row.RowKey, ETag.All, cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status == 404) { }
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken)
    {
        var count = 0;
        try
        {
            await foreach (var row in _client.QueryAsync<TableEntity>(select: ["PartitionKey"], cancellationToken: cancellationToken)) count++;
        }
        catch (RequestFailedException exception) when (exception.Status == 404) { }
        return count;
    }
}
