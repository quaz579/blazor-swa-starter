using App.Api.Security;
using App.Api.Storage;
using App.Core.Models;
using App.Core.Security;
using App.Tests.Storage;
using AwesomeAssertions;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace App.Tests.Security;

[Collection(StorageConnectionEnvironmentCollection.Name)]
public sealed class FieldEncryptionAzuriteTests : IDisposable
{
    private const string ActiveKeySetting = "FIELD_ENCRYPTION_ACTIVE_KEY_ID";
    private const string KeySetting = "FIELD_ENCRYPTION_KEYS__integration";

    private readonly Dictionary<string, string?> _originalEncryptionSettings;

    public FieldEncryptionAzuriteTests()
    {
        _originalEncryptionSettings = Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .Where(entry => entry.Key is string name && name.StartsWith("FIELD_ENCRYPTION_", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(entry => (string)entry.Key, entry => entry.Value?.ToString(), StringComparer.OrdinalIgnoreCase);
        foreach (var name in _originalEncryptionSettings.Keys)
            Environment.SetEnvironmentVariable(name, null);
    }

    public void Dispose()
    {
        foreach (var (name, value) in _originalEncryptionSettings)
            Environment.SetEnvironmentVariable(name, value);
    }

    [AzuriteFact]
    public async Task FieldProtector_StoredProtectedField_RoundTripsWithoutPlaintextInBlob()
    {
        Environment.SetEnvironmentVariable(ActiveKeySetting, "integration");
        Environment.SetEnvironmentVariable(KeySetting, FieldEncryptionTestKit.KeyBase64("integration"));
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddFieldEncryption(configuration)
            .BuildServiceProvider();
        var protector = provider.GetRequiredService<IFieldProtector>();
        var prefix = "test-encrypted-" + Guid.NewGuid().ToString("N");
        var key = "record";
        var plaintext = "Sensitive Unicode value: 雪🌨️";
        var purpose = "item.secretField";
        var store = new BlobJsonStore<Item>(
            "UseDevelopmentStorage=true",
            new BlobJsonStoreOptions<Item> { Prefix = prefix },
            NullLogger<BlobJsonStore<Item>>.Instance);

        try
        {
            await store.PutAsync(key, new Item
            {
                Id = key,
                Name = "encrypted fixture",
                Description = protector.Protect(plaintext, purpose),
                CreatedAt = DateTimeOffset.Parse("2026-01-01T12:00:00Z")
            });

            var retrieved = await store.GetAsync(key);
            retrieved.Should().NotBeNull();
            protector.Unprotect(retrieved!.Description!, purpose).Should().Be(plaintext);

            var blob = new BlobServiceClient("UseDevelopmentStorage=true")
                .GetBlobContainerClient("app-data")
                .GetBlobClient($"{prefix}/{key}.json");
            var content = await blob.DownloadContentAsync();
            content.Value.Content.ToString().Should().NotContain(plaintext);
            content.Value.Content.ToString().Should().Contain("fe1.integration.");
        }
        finally
        {
            await store.DeleteAsync(key);
        }
    }
}
