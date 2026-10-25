using Azure.Data.Tables;

namespace App.Api.Storage;

public static class StorageClients
{
    public static TableClient CreateTableClient(string tableName) =>
        CreateTableClient(StorageConnection.ResolveConnectionString(), tableName);

    public static TableClient CreateTableClient(string connectionString, string tableName) =>
        new TableServiceClient(connectionString).GetTableClient(tableName);

}
