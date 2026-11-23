using App.Api.Security;
using App.Core.Security;
using App.Tests.Storage;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace App.Tests.Security;

[Collection(StorageConnectionEnvironmentCollection.Name)]
public sealed class FieldEncryptionTests : IDisposable
{
    private static readonly string[] EnvironmentNames =
    [
        "APP_ENVIRONMENT",
        "AZURE_STORAGE_CONNECTION_STRING",
        "AzureWebJobsStorage",
        "PREVIEW_STORAGE_CONNECTION_STRING",
        "PRODUCTION_STORAGE_ACCOUNT_NAME",
        "PREVIEW_STORAGE_ACCOUNT_NAME"
    ];

    private readonly Dictionary<string, string?> _originalEnvironment;
    private readonly Dictionary<string, string?> _originalEncryptionEnvironment;

    public FieldEncryptionTests()
    {
        _originalEnvironment = EnvironmentNames.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        _originalEncryptionEnvironment = Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .Where(entry => entry.Key is string name && name.StartsWith("FIELD_ENCRYPTION_", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(entry => (string)entry.Key, entry => entry.Value?.ToString(), StringComparer.OrdinalIgnoreCase);

        foreach (var name in EnvironmentNames.Concat(_originalEncryptionEnvironment.Keys))
            Environment.SetEnvironmentVariable(name, null);
    }

    public void Dispose()
    {
        foreach (var name in EnvironmentNames)
            Environment.SetEnvironmentVariable(name, _originalEnvironment[name]);
        foreach (var (name, value) in _originalEncryptionEnvironment)
            Environment.SetEnvironmentVariable(name, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("雪🌨️ café — naïve")]
    public void Protect_ValidPlaintext_RoundTrips(string plaintext)
    {
        var protector = FieldEncryptionTestKit.CreateProtector();

        var envelope = protector.Protect(plaintext, "customer.birthDate");

        protector.Unprotect(envelope, "customer.birthDate").Should().Be(plaintext);
    }

    [Fact]
    public void Protect_RepeatedPlaintext_ProducesDifferentEnvelopes()
    {
        var protector = FieldEncryptionTestKit.CreateProtector();

        var first = protector.Protect("same value", "profile.email");
        var second = protector.Protect("same value", "profile.email");

        first.Should().NotBe(second);
        protector.Unprotect(first, "profile.email").Should().Be("same value");
        protector.Unprotect(second, "profile.email").Should().Be("same value");
    }

    [Fact]
    public void Unprotect_WrongPurpose_ThrowsSafeFieldEncryptionException()
    {
        var protector = FieldEncryptionTestKit.CreateProtector();
        var envelope = protector.Protect("sensitive plaintext", "profile.email");

        Action act = () => protector.Unprotect(envelope, FieldEncryptionTestKit.SwapPurpose("profile.email"));

        var exception = act.Should().Throw<FieldEncryptionException>().Which;
        exception.Message.Should().NotContain("sensitive plaintext").And.NotContain(FieldEncryptionTestKit.KeyBase64("test"));
    }

    [Fact]
    public void Unprotect_TamperedByte_ThrowsFieldEncryptionException()
    {
        var protector = FieldEncryptionTestKit.CreateProtector();
        var envelope = protector.Protect("value", "profile.name");

        Action act = () => protector.Unprotect(FieldEncryptionTestKit.TamperByte(envelope), "profile.name");

        act.Should().Throw<FieldEncryptionException>();
    }

    [Fact]
    public void Unprotect_TruncatedEnvelope_ThrowsFieldEncryptionException()
    {
        var protector = FieldEncryptionTestKit.CreateProtector();
        var envelope = protector.Protect("value", "profile.name");

        Action act = () => protector.Unprotect(FieldEncryptionTestKit.Truncate(envelope), "profile.name");

        act.Should().Throw<FieldEncryptionException>();
    }

    [Theory]
    [InlineData("fe2.test.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("fe1.unknown.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("fe1.test.***")]
    [InlineData("fe1.test.AA")]
    public void Unprotect_UnknownOrMalformedEnvelope_ThrowsFieldEncryptionException(string envelope)
    {
        var protector = FieldEncryptionTestKit.CreateProtector();

        Action act = () => protector.Unprotect(envelope, "profile.name");

        act.Should().Throw<FieldEncryptionException>();
    }

    [Fact]
    public void Unprotect_WrongKey_ThrowsFieldEncryptionException()
    {
        var original = FieldEncryptionTestKit.CreateProtector("shared");
        var other = FieldEncryptionTestKit.CreateProtector("shared", new Dictionary<string, byte[]>
        {
            ["shared"] = System.Security.Cryptography.SHA256.HashData("different test key"u8)
        });
        var envelope = original.Protect("value", "profile.name");

        Action act = () => other.Unprotect(envelope, "profile.name");

        act.Should().Throw<FieldEncryptionException>();
    }

    [Fact]
    public void Unprotect_RotatedKeyRing_DecryptsOldEnvelopeAndWritesWithActiveKey()
    {
        var oldProtector = FieldEncryptionTestKit.CreateProtector("old");
        var rotatedProtector = FieldEncryptionTestKit.CreateProtector("new", "old");
        var oldEnvelope = oldProtector.Protect("value", "profile.name");

        rotatedProtector.Unprotect(oldEnvelope, "profile.name").Should().Be("value");
        rotatedProtector.Protect("new value", "profile.name").Split('.')[1].Should().Be("new");
    }

    [Fact]
    public void AddFieldEncryption_NoConfigurationOnLocalAzurite_UsesDevelopmentDefault()
    {
        using var provider = BuildProvider(new ConfigurationBuilder().Build());

        provider.GetRequiredService<FieldEncryptionStatus>().Mode.Should().Be(FieldEncryptionStatus.DevelopmentDefault);
        var protector = provider.GetRequiredService<IFieldProtector>();
        protector.Unprotect(protector.Protect("local", "profile.name"), "profile.name").Should().Be("local");
    }

    [Fact]
    public void AddFieldEncryption_NoConfigurationOnCustomLoopbackStorage_UsesDevelopmentDefault()
    {
        Environment.SetEnvironmentVariable(
            "AZURE_STORAGE_CONNECTION_STRING",
            "DefaultEndpointsProtocol=http;AccountName=localtest;AccountKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=;BlobEndpoint=http://127.0.0.1:10000/localtest;");

        using var provider = BuildProvider(new ConfigurationBuilder().Build());

        provider.GetRequiredService<FieldEncryptionStatus>().Mode.Should().Be(FieldEncryptionStatus.DevelopmentDefault);
        provider.GetRequiredService<IFieldProtector>().Protect("local", "profile.name").Split('.')[1].Should().Be("dev");
    }

    [Fact]
    public void AddFieldEncryption_ProductionMissingConfiguration_RegistersFailClosedProtector()
    {
        Environment.SetEnvironmentVariable("APP_ENVIRONMENT", "production");
        using var provider = BuildProvider(new ConfigurationBuilder().Build());
        var protector = provider.GetRequiredService<IFieldProtector>();

        provider.GetRequiredService<FieldEncryptionStatus>().Mode.Should().Be(FieldEncryptionStatus.NotConfigured);
        Action act = () => protector.Protect("value", "profile.name");

        act.Should().Throw<FieldEncryptionException>()
            .WithMessage("field encryption is not configured: set FIELD_ENCRYPTION_ACTIVE_KEY_ID and FIELD_ENCRYPTION_KEYS__<id>");
    }

    [Fact]
    public void AddFieldEncryption_ProductionDevelopmentKey_RegistersFailClosedProtector()
    {
        Environment.SetEnvironmentVariable("APP_ENVIRONMENT", "production");
        using var provider = BuildProvider(Configuration(
            ("FIELD_ENCRYPTION_ACTIVE_KEY_ID", "dev"),
            ("FIELD_ENCRYPTION_KEYS:dev", "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=")));

        provider.GetRequiredService<FieldEncryptionStatus>().Mode.Should().Be(FieldEncryptionStatus.NotConfigured);
        Action act = () => provider.GetRequiredService<IFieldProtector>().Unprotect("fe1.dev.AA", "profile.name");

        act.Should().Throw<FieldEncryptionException>()
            .WithMessage("field encryption is not configured: set FIELD_ENCRYPTION_ACTIVE_KEY_ID and FIELD_ENCRYPTION_KEYS__<id>");
    }

    [Fact]
    public void AddFieldEncryption_DevelopmentKeyWithDeployedIdentityAndMissingMode_RegistersFailClosedProtector()
    {
        Environment.SetEnvironmentVariable("PRODUCTION_STORAGE_ACCOUNT_NAME", "productionaccount");
        Environment.SetEnvironmentVariable("PREVIEW_STORAGE_ACCOUNT_NAME", "previewaccount");
        using var provider = BuildProvider(Configuration(
            ("FIELD_ENCRYPTION_ACTIVE_KEY_ID", "dev"),
            ("FIELD_ENCRYPTION_KEYS:dev", FieldEncryptionConfiguration.DevelopmentKeyBase64)));
        var protector = provider.GetRequiredService<IFieldProtector>();

        provider.GetRequiredService<FieldEncryptionStatus>().Mode.Should().Be(FieldEncryptionStatus.NotConfigured);
        Action act = () => protector.Protect("value", "profile.name");

        act.Should().Throw<FieldEncryptionException>()
            .WithMessage("field encryption is not configured: set FIELD_ENCRYPTION_ACTIVE_KEY_ID and FIELD_ENCRYPTION_KEYS__<id>");
    }

    [Fact]
    public void AddFieldEncryption_DevelopmentKeyWithRemoteStorage_RegistersFailClosedProtector()
    {
        Environment.SetEnvironmentVariable(
            "AZURE_STORAGE_CONNECTION_STRING",
            "DefaultEndpointsProtocol=https;AccountName=remoteaccount;AccountKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=;BlobEndpoint=https://remoteaccount.blob.core.windows.net;");
        using var provider = BuildProvider(Configuration(
            ("FIELD_ENCRYPTION_ACTIVE_KEY_ID", "dev"),
            ("FIELD_ENCRYPTION_KEYS:dev", FieldEncryptionConfiguration.DevelopmentKeyBase64)));
        var protector = provider.GetRequiredService<IFieldProtector>();

        provider.GetRequiredService<FieldEncryptionStatus>().Mode.Should().Be(FieldEncryptionStatus.NotConfigured);
        Action act = () => protector.Protect("value", "profile.name");

        act.Should().Throw<FieldEncryptionException>()
            .WithMessage("field encryption is not configured: set FIELD_ENCRYPTION_ACTIVE_KEY_ID and FIELD_ENCRYPTION_KEYS__<id>");
    }

    [Theory]
    [InlineData("not-base64", "test")]
    [InlineData("AA==", "test")]
    [InlineData("AA==", "bad-key-id!")]
    public void AddFieldEncryption_MalformedConfiguration_ThrowsSafeConfigurationError(string key, string keyId)
    {
        var configuration = Configuration(
            ("FIELD_ENCRYPTION_ACTIVE_KEY_ID", "test"),
            ($"FIELD_ENCRYPTION_KEYS:{keyId}", key));

        Action act = () => BuildProvider(configuration);

        var exception = act.Should().Throw<InvalidOperationException>().Which;
        exception.Message.Should().Contain("FIELD_ENCRYPTION_ACTIVE_KEY_ID")
            .And.Contain("FIELD_ENCRYPTION_KEYS__<id>")
            .And.NotContain(key);
    }

    [Fact]
    public void AddFieldEncryption_PartialConfiguration_ThrowsSafeConfigurationError()
    {
        var configuration = Configuration(("FIELD_ENCRYPTION_ACTIVE_KEY_ID", "test"));

        Action act = () => BuildProvider(configuration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*FIELD_ENCRYPTION_ACTIVE_KEY_ID*FIELD_ENCRYPTION_KEYS__<id>*");
    }

    private static ServiceProvider BuildProvider(IConfiguration configuration) =>
        new ServiceCollection().AddLogging().AddFieldEncryption(configuration).BuildServiceProvider();

    private static IConfiguration Configuration(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(value => value.Key, value => value.Value))
            .Build();
}
