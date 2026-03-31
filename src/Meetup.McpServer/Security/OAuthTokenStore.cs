using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meetup.McpServer.Security;

public sealed class OAuthTokenStore
{
    private readonly string _filePath;

    public OAuthTokenStore(string? overridePath = null)
    {
        _filePath = overridePath
                    ?? Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        ".meetup-mcp",
                        "tokens.json");
    }

    public async Task<StoredTokens?> LoadAsync(string expectedClientId, CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
            return null;

        await using var stream = File.OpenRead(_filePath);
        var stored = await JsonSerializer.DeserializeAsync<StoredTokens>(stream, cancellationToken: cancellationToken);

        if (stored is null || stored.ClientId != expectedClientId)
            return null;

        return stored;
    }

    public async Task SaveAsync(StoredTokens tokens, CancellationToken cancellationToken)
    {
        var dir = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(dir);

        var tmpPath = _filePath + ".tmp";
        await using (var stream = File.Create(tmpPath))
        {
            await JsonSerializer.SerializeAsync(stream, tokens, JsonOptions, cancellationToken);
        }

        // Set restrictive permissions on Linux/macOS before moving into place
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(tmpPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        File.Move(tmpPath, _filePath, overwrite: true);
    }

    public Task DeleteAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(_filePath))
            File.Delete(_filePath);

        return Task.CompletedTask;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };
}

public sealed record StoredTokens
{
    [JsonPropertyName("client_id")]
    public required string ClientId { get; init; }

    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    [JsonPropertyName("refresh_token")]
    public required string RefreshToken { get; init; }

    [JsonPropertyName("expires_at")]
    public required DateTimeOffset ExpiresAt { get; init; }
}
