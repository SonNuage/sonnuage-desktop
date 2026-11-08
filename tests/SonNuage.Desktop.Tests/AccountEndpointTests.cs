using SonNuage.Desktop.Services;
using SonNuage.UI.Services;

namespace SonNuage.Desktop.Tests;

public sealed class AccountEndpointTests
{
    private sealed class MemoryStore : IKeyValueStore
    {
        private readonly Dictionary<string, string> _values = [];
        public Task<string?> GetAsync(string key) => Task.FromResult(_values.GetValueOrDefault(key));
        public Task SetAsync(string key, string? value)
        {
            if (value is null) _values.Remove(key);
            else _values[key] = value;
            return Task.CompletedTask;
        }
    }

    private static AccountEndpoint Endpoint() => new(new AccountStore(new MemoryStore(), multiAccount: true));

    [Theory]
    [InlineData("https://music.example.org", "https://music.example.org/")]
    [InlineData("https://example.org/sonnuage", "https://example.org/sonnuage/")]
    [InlineData("https://example.org/sonnuage/", "https://example.org/sonnuage/")]
    [InlineData("http://192.168.1.10:8080/a%20b", "http://192.168.1.10:8080/a%20b/")]
    public async Task The_server_address_ends_with_a_slash_so_api_paths_resolve_under_it(string typed, string expected)
    {
        var endpoint = Endpoint();

        await endpoint.SetAsync(new Uri(typed));

        Assert.Equal(expected, endpoint.BaseAddress!.AbsoluteUri);
        Assert.Equal(new Uri(expected + "api/me"), new Uri(endpoint.BaseAddress, "api/me"));
    }

    [Fact]
    public async Task Clearing_the_address_leaves_no_server()
    {
        var endpoint = Endpoint();
        await endpoint.SetAsync(new Uri("https://music.example.org"));

        await endpoint.SetAsync(null);

        Assert.Null(endpoint.BaseAddress);
    }

    [Fact]
    public async Task Changing_the_address_notifies_listeners()
    {
        var endpoint = Endpoint();
        var changes = 0;
        endpoint.Changed += () => changes++;

        await endpoint.SetAsync(new Uri("https://music.example.org"));

        Assert.True(changes > 0);
    }
}
