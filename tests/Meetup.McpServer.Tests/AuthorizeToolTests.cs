using Meetup.McpServer.Configuration;
using Meetup.McpServer.MeetupApi;
using Meetup.McpServer.Security;
using Meetup.McpServer.Tools;
using Microsoft.Extensions.Options;

namespace Meetup.McpServer.Tests;

public sealed class AuthorizeToolTests
{
    /// <summary>
    /// Stub that records ExchangeCodeAsync calls and simulates "authorization required" on GetAccessTokenAsync.
    /// </summary>
    private sealed class StubTokenProvider : IAccessTokenProvider
    {
        public List<string> ExchangedCodes { get; } = new();
        public bool IsAuthorized { get; set; }

        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            if (IsAuthorized)
                return Task.FromResult("fake-token");

            throw new InvalidOperationException(
                "Meetup authorization required. Open this URL in your browser to authorize:\nhttps://secure.meetup.com/oauth2/authorize?client_id=test");
        }

        public Task ExchangeCodeAsync(string code, CancellationToken cancellationToken)
        {
            ExchangedCodes.Add(code);
            IsAuthorized = true;
            return Task.CompletedTask;
        }
    }

    private static MeetupTools CreateTools(StubTokenProvider tokenProvider)
    {
        var options = Options.Create(new MeetupServerOptions
        {
            GroupUrlname = "test-group",
            Mode = MeetupServerMode.Organizer,
            UseFakeApi = true,
        });

        var apiClient = new FakeMeetupApiClient("test-group");
        var policy = new CapabilityPolicy(options);

        return new MeetupTools(apiClient, tokenProvider, policy, options);
    }

    [Fact]
    public async Task Authorize_NoArgs_ReturnsAuthUrl_WhenNotAuthorized()
    {
        var tokenProvider = new StubTokenProvider { IsAuthorized = false };
        var tools = CreateTools(tokenProvider);

        var result = await tools.Authorize(cancellationToken: CancellationToken.None);

        Assert.Contains("authorization required", result);
        Assert.Contains("https://secure.meetup.com/oauth2/authorize", result);
    }

    [Fact]
    public async Task Authorize_NoArgs_ReturnsAlreadyAuthorized_WhenAuthorized()
    {
        var tokenProvider = new StubTokenProvider { IsAuthorized = true };
        var tools = CreateTools(tokenProvider);

        var result = await tools.Authorize(cancellationToken: CancellationToken.None);

        Assert.Equal("Already authorized with Meetup.", result);
    }

    [Fact]
    public async Task Authorize_WithCode_ExchangesCodeDirectly()
    {
        var tokenProvider = new StubTokenProvider { IsAuthorized = false };
        var tools = CreateTools(tokenProvider);

        var result = await tools.Authorize(code: "my-auth-code", cancellationToken: CancellationToken.None);

        Assert.Equal("Authorization successful! You can now use Meetup tools.", result);
        Assert.Single(tokenProvider.ExchangedCodes);
        Assert.Equal("my-auth-code", tokenProvider.ExchangedCodes[0]);
    }

    [Fact]
    public async Task Authorize_WithCallbackUrl_ExtractsCodeAndExchanges()
    {
        var tokenProvider = new StubTokenProvider { IsAuthorized = false };
        var tools = CreateTools(tokenProvider);

        var callbackUrl = "http://127.0.0.1:8787/meetup/oauth/callback?code=extracted-code";
        var result = await tools.Authorize(callbackUrl: callbackUrl, cancellationToken: CancellationToken.None);

        Assert.Equal("Authorization successful! You can now use Meetup tools.", result);
        Assert.Single(tokenProvider.ExchangedCodes);
        Assert.Equal("extracted-code", tokenProvider.ExchangedCodes[0]);
    }

    [Fact]
    public async Task Authorize_WithCallbackUrl_MissingCode_ReturnsError()
    {
        var tokenProvider = new StubTokenProvider { IsAuthorized = false };
        var tools = CreateTools(tokenProvider);

        var callbackUrl = "http://127.0.0.1:8787/meetup/oauth/callback?error=access_denied";
        var result = await tools.Authorize(callbackUrl: callbackUrl, cancellationToken: CancellationToken.None);

        Assert.Equal("The provided callback URL does not contain a 'code' parameter.", result);
        Assert.Empty(tokenProvider.ExchangedCodes);
    }

    [Fact]
    public async Task Authorize_WithInvalidCallbackUrl_ReturnsError()
    {
        var tokenProvider = new StubTokenProvider { IsAuthorized = false };
        var tools = CreateTools(tokenProvider);

        var result = await tools.Authorize(callbackUrl: "not-a-valid-url", cancellationToken: CancellationToken.None);

        Assert.Equal("The provided callback URL is not a valid URL.", result);
        Assert.Empty(tokenProvider.ExchangedCodes);
    }
}
