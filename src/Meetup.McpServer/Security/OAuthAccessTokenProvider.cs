using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Meetup.McpServer.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meetup.McpServer.Security;

public sealed class OAuthAccessTokenProvider : IAccessTokenProvider, IDisposable
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MeetupServerOptions _options;
    private readonly OAuthTokenStore _tokenStore;
    private readonly ILogger<OAuthAccessTokenProvider> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private string? _accessToken;
    private string? _refreshToken;
    private DateTimeOffset _expiresAt;

    public OAuthAccessTokenProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<MeetupServerOptions> options,
        OAuthTokenStore tokenStore,
        ILogger<OAuthAccessTokenProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _tokenStore = tokenStore;
        _logger = logger;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_options.UseFakeApi)
            return "fake-access-token";

        // Fast path: valid cached token
        if (IsTokenValid())
            return _accessToken!;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (IsTokenValid())
                return _accessToken!;

            // Try loading from disk if we don't have tokens in memory
            if (_accessToken is null)
            {
                var stored = await _tokenStore.LoadAsync(_options.OAuthClientId, cancellationToken);
                if (stored is not null)
                {
                    _accessToken = stored.AccessToken;
                    _refreshToken = stored.RefreshToken;
                    _expiresAt = stored.ExpiresAt;

                    if (IsTokenValid())
                        return _accessToken;
                }
            }

            // Try refreshing if we have a refresh token
            if (_refreshToken is not null)
            {
                try
                {
                    await RefreshAccessTokenAsync(cancellationToken);
                    return _accessToken!;
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning(ex, "Token refresh failed — starting new authorization flow");
                    _accessToken = null;
                    _refreshToken = null;
                    await _tokenStore.DeleteAsync(cancellationToken);
                }
            }

            // Full interactive authorization
            await AuthorizeInteractiveAsync(cancellationToken);
            return _accessToken!;
        }
        finally
        {
            _lock.Release();
        }
    }

    private bool IsTokenValid() =>
        _accessToken is not null && _expiresAt > DateTimeOffset.UtcNow.AddMinutes(5);

    private async Task RefreshAccessTokenAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Refreshing Meetup access token");

        var client = _httpClientFactory.CreateClient();
        var form = new Dictionary<string, string>
        {
            ["client_id"] = _options.OAuthClientId,
            ["client_secret"] = _options.OAuthClientSecret,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = _refreshToken!
        };

        using var response = await client.PostAsync(
            _options.OAuthAccessUrl, new FormUrlEncodedContent(form), cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken)
                      ?? throw new InvalidOperationException("Meetup OAuth refresh response was empty.");

        // Persist IMMEDIATELY — refresh tokens are single-use
        await PersistTokens(payload, cancellationToken);
    }

    private async Task AuthorizeInteractiveAsync(CancellationToken cancellationToken)
    {
        var authorizeUrl =
            $"{_options.OAuthAuthorizeUrl}?client_id={Uri.EscapeDataString(_options.OAuthClientId)}" +
            $"&response_type=code&redirect_uri={Uri.EscapeDataString(_options.OAuthRedirectUri)}";

        _logger.LogWarning("Meetup authorization required. Open this URL in your browser:\n{Url}", authorizeUrl);

        TryOpenBrowser(authorizeUrl);

        var code = await ListenForCallbackCodeAsync(cancellationToken);
        await ExchangeCodeForTokenAsync(code, cancellationToken);
    }

    private async Task<string> ListenForCallbackCodeAsync(CancellationToken cancellationToken)
    {
        var uri = new Uri(_options.OAuthRedirectUri);
        var prefix = $"http://{uri.Host}:{uri.Port}/";

        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        _logger.LogDebug("Listening for OAuth callback on {Prefix}", prefix);

        try
        {
            // Register cancellation to stop the listener
            await using var registration = cancellationToken.Register(() => listener.Stop());

            var context = await listener.GetContextAsync();
            var code = context.Request.QueryString["code"];

            // Send a friendly response to the browser
            var html = System.Text.Encoding.UTF8.GetBytes(
                """
                <html><body style="font-family:sans-serif;text-align:center;padding:60px">
                <h1>Authorization successful</h1>
                <p>You can close this tab and return to your terminal.</p>
                </body></html>
                """);
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = html.Length;
            await context.Response.OutputStream.WriteAsync(html, cancellationToken);
            context.Response.Close();

            return code ?? throw new InvalidOperationException(
                "OAuth callback did not include a 'code' parameter. Authorization may have been denied.");
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task ExchangeCodeForTokenAsync(string code, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Exchanging authorization code for access token");

        var client = _httpClientFactory.CreateClient();
        var form = new Dictionary<string, string>
        {
            ["client_id"] = _options.OAuthClientId,
            ["client_secret"] = _options.OAuthClientSecret,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = _options.OAuthRedirectUri,
            ["code"] = code
        };

        using var response = await client.PostAsync(
            _options.OAuthAccessUrl, new FormUrlEncodedContent(form), cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken)
                      ?? throw new InvalidOperationException("Meetup OAuth token response was empty.");

        await PersistTokens(payload, cancellationToken);
    }

    private async Task PersistTokens(TokenResponse payload, CancellationToken cancellationToken)
    {
        _accessToken = payload.AccessToken;
        _refreshToken = payload.RefreshToken;
        _expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, payload.ExpiresIn));

        try
        {
            await _tokenStore.SaveAsync(new StoredTokens
            {
                ClientId = _options.OAuthClientId,
                AccessToken = _accessToken,
                RefreshToken = _refreshToken,
                ExpiresAt = _expiresAt
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            // In-memory tokens are still valid for this session, but next restart will require re-auth
            _logger.LogWarning(ex, "Failed to persist OAuth tokens to disk — session will work but re-authorization needed on restart");
        }
    }

    private void TryOpenBrowser(string url)
    {
        try
        {
            var psi = new ProcessStartInfo(url) { UseShellExecute = true };
            Process.Start(psi);
        }
        catch
        {
            // Best-effort — user can copy the URL from the log
        }
    }

    public void Dispose()
    {
        _lock.Dispose();
    }

    private sealed record TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;

        [JsonPropertyName("token_type")]
        public string TokenType { get; init; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; init; } = string.Empty;
    }
}
