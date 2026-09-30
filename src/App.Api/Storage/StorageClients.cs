using Azure.Data.Tables;
using Azure.Storage.Queues;

namespace App.Api.Storage;

/// <summary>
/// Builds table and queue clients from the same connection string the blob store uses
/// (<see cref="StorageConnection.ResolveConnectionString"/>). Callers own <c>CreateIfNotExists</c>.
/// </summary>
public static class StorageClients
{
    public static TableClient CreateTableClient(string tableName) =>
        CreateTableClient(StorageConnection.ResolveConnectionString(), tableName);

    public static TableClient CreateTableClient(string connectionString, string tableName) =>
        new TableServiceClient(connectionString).GetTableClient(tableName);

    public static QueueClient CreateQueueClient(string queueName) =>
        CreateQueueClient(StorageConnection.ResolveConnectionString(), queueName);

    public static QueueClient CreateQueueClient(string connectionString, string queueName) =>
        new(connectionString, queueName, new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 });
}
