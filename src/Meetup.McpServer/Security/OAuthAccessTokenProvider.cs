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
    // Cancelled when the provider is disposed (i.e., server shutting down)
    private readonly CancellationTokenSource _providerCts = new();

    private string? _accessToken;
    private string? _refreshToken;
    private DateTimeOffset _expiresAt;

    // Background auth listener — outlives any single tool call
    private Task? _pendingAuthTask;
    private string? _pendingAuthUrl;

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
            if (IsTokenValid())
                return _accessToken!;

            // Try loading from disk
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

            // Need full authorization. Start the background listener if not already running,
            // then immediately surface the URL as an error so the MCP client can show it.
            EnsureAuthListenerRunning();

            throw new InvalidOperationException(
                $"Meetup authorization required. Open this URL in your browser to authorize:\n{_pendingAuthUrl}");
        }
        finally
        {
            _lock.Release();
        }
    }

    private void EnsureAuthListenerRunning()
    {
        // Already running and not faulted — URL is already set
        if (_pendingAuthTask is { IsCompleted: false })
            return;

        _pendingAuthUrl =
            $"{_options.OAuthAuthorizeUrl}?client_id={Uri.EscapeDataString(_options.OAuthClientId)}" +
            $"&response_type=code&redirect_uri={Uri.EscapeDataString(_options.OAuthRedirectUri)}";

        _logger.LogWarning("Starting OAuth listener. Auth URL: {Url}", _pendingAuthUrl);

        // Fire and forget — uses provider lifetime token so it survives individual tool call cancellations
        _pendingAuthTask = Task.Run(() => RunAuthFlowAsync(_providerCts.Token), _providerCts.Token);
    }

    private async Task RunAuthFlowAsync(CancellationToken cancellationToken)
    {
        try
        {
            var code = await ListenForCallbackCodeAsync(cancellationToken);
            await ExchangeCodeForTokenAsync(code, cancellationToken);
            _logger.LogInformation("Meetup authorization successful.");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("OAuth listener cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OAuth authorization flow failed.");
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

    private async Task<string> ListenForCallbackCodeAsync(CancellationToken cancellationToken)
    {
        var uri = new Uri(_options.OAuthRedirectUri);
        // Bind to all interfaces ('+') so port mapping works inside Docker containers
        var prefix = $"http://+:{uri.Port}/";

        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        _logger.LogDebug("Listening for OAuth callback on {Prefix}", prefix);

        try
        {
            using var registration = cancellationToken.Register(() => listener.Stop());

            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (ObjectDisposedException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw;
            }

            var code = context.Request.QueryString["code"];

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
            _logger.LogWarning(ex, "Failed to persist OAuth tokens to disk — re-authorization will be needed on restart");
        }
    }

    public void Dispose()
    {
        _providerCts.Cancel();
        _providerCts.Dispose();
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
