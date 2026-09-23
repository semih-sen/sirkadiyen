using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sirkadiyen.Api.Vault;
using Sirkadiyen.Application.Vault;
using Sirkadiyen.Infrastructure.Vault;
using Xunit;

namespace Sirkadiyen.Api.UnitTests;

public sealed class VaultApiTests
{
    private const string Key = "0123456789abcdef0123456789abcdef-test";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("yanlis-anahtar")]
    [InlineData(Key + "x")]
    public async Task Filter_refuses_a_missing_or_wrong_key(string? presented)
    {
        object? result = await InvokeFilterAsync(presented);

        Assert.IsType<UnauthorizedHttpResult>(result);
    }

    [Fact]
    public async Task Filter_admits_the_configured_key()
    {
        object? result = await InvokeFilterAsync(Key);

        Assert.Equal("admitted", result);
    }

    [Fact]
    public void Feature_is_off_without_a_key()
    {
        ServiceCollection services = new();

        services.AddSirkadiyenVault(Configuration([]), AppContext.BaseDirectory);

        Assert.Empty(services);
    }

    [Fact]
    public void Feature_refuses_a_short_key()
    {
        ServiceCollection services = new();

        Assert.Throws<InvalidOperationException>(() => services.AddSirkadiyenVault(
            Configuration(new() { ["SIRKADIYEN_VAULT:API_KEY"] = "kisa" }),
            AppContext.BaseDirectory));
    }

    [Fact]
    public void Feature_requires_storage_once_enabled()
    {
        ServiceCollection services = new();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => services.AddSirkadiyenVault(
            Configuration(new() { ["SIRKADIYEN_VAULT:API_KEY"] = Key }),
            AppContext.BaseDirectory));
        Assert.Contains("SIRKADIYEN_VAULT:S3_ENDPOINT", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Feature_registers_with_the_shipped_instructions()
    {
        ServiceCollection services = new();

        services.AddSirkadiyenVault(
            Configuration(new()
            {
                ["SIRKADIYEN_VAULT:API_KEY"] = Key,
                ["SIRKADIYEN_VAULT:S3_ENDPOINT"] = "http://127.0.0.1:9000",
                ["SIRKADIYEN_VAULT:S3_ACCESS_KEY"] = "a",
                ["SIRKADIYEN_VAULT:S3_SECRET_KEY"] = "s",
                ["SIRKADIYEN_VAULT:S3_BUCKET"] = "vault",
                ["SIRKADIYEN_VAULT:MAX_BACKLINKS"] = "3",
            }),
            AppContext.BaseDirectory);

        VaultNoteOptions options = Single<VaultNoteOptions>(services);
        ClaudeCodeOptions agent = Single<ClaudeCodeOptions>(services);
        Assert.Equal(3, options.MaxBacklinks);
        Assert.Equal("claude-sonnet-5", agent.Model);
        Assert.Equal("medium", agent.Effort);
        Assert.Contains("[[Not Adı]]", agent.StandingInstructions, StringComparison.Ordinal);
    }

    private static async Task<object?> InvokeFilterAsync(string? presented)
    {
        DefaultHttpContext http = new();
        if (presented is not null)
        {
            http.Request.Headers[VaultApiKeyFilter.HeaderName] = presented;
        }

        VaultApiKeyFilter filter = new(new VaultApiOptions(Key));
        return await filter.InvokeAsync(
            EndpointFilterInvocationContext.Create(http),
            static _ => ValueTask.FromResult<object?>("admitted"));
    }

    private static T Single<T>(ServiceCollection services) =>
        Assert.Single(services.Where(static service => service.ServiceType == typeof(T))
            .Select(static service => (T)service.ImplementationInstance!));

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
