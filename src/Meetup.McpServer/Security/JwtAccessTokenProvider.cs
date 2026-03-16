using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Meetup.McpServer.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Meetup.McpServer.Security;

public sealed class JwtAccessTokenProvider : IAccessTokenProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MeetupServerOptions _options;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _expiresAt;

    public JwtAccessTokenProvider(IHttpClientFactory httpClientFactory, IOptions<MeetupServerOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_options.UseFakeApi)
        {
            return "fake-access-token";
        }

        if (!string.IsNullOrWhiteSpace(_accessToken) && _expiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return _accessToken;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(_accessToken) && _expiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            {
                return _accessToken;
            }

            var signedJwt = BuildSignedJwt();
            var client = _httpClientFactory.CreateClient();
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"] = signedJwt,
            };

            using var response = await client.PostAsync(_options.OAuthAccessUrl, new FormUrlEncodedContent(form), cancellationToken);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken)
                          ?? throw new InvalidOperationException("Meetup OAuth response body was empty.");

            _accessToken = payload.AccessToken;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, payload.ExpiresIn));
            return _accessToken;
        }
        finally
        {
            _lock.Release();
        }
    }

    private string BuildSignedJwt()
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(_options.JwtPrivateKeyPem);

        var key = new RsaSecurityKey(rsa) { KeyId = _options.JwtSigningKeyId };
        var creds = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);

        var now = DateTime.UtcNow;
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.OAuthClientId,
            Audience = "api.meetup.com",
            Subject = new ClaimsIdentity([new Claim("sub", _options.AuthorizedMemberId)]),
            Expires = now.AddMinutes(2),
            NotBefore = now.AddSeconds(-5),
            SigningCredentials = creds,
            AdditionalHeaderClaims = new Dictionary<string, object>
            {
                ["kid"] = _options.JwtSigningKeyId,
                ["typ"] = "JWT"
            }
        };

        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(tokenDescriptor);
        return handler.WriteToken(token);
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
