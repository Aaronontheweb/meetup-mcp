using Microsoft.Extensions.Configuration;

namespace Meetup.McpServer.Configuration;

public sealed class MeetupServerOptions
{
    public const string GroupUrlnameEnvVar = "MEETUP_GROUP_URLNAME";
    public const string ModeEnvVar = "MEETUP_MODE";
    public const string AllowPublishEnvVar = "MEETUP_ALLOW_PUBLISH";
    public const string AllowDestructiveEnvVar = "MEETUP_ALLOW_DESTRUCTIVE";
    public const string UseFakeApiEnvVar = "MEETUP_USE_FAKE_API";
    public const string ApiUrlEnvVar = "MEETUP_API_URL";
    public const string OAuthAccessUrlEnvVar = "MEETUP_OAUTH_ACCESS_URL";
    public const string ClientIdEnvVar = "MEETUP_OAUTH_KEY";
    public const string ClientSecretEnvVar = "MEETUP_OAUTH_SECRET";
    public const string RedirectUriEnvVar = "MEETUP_OAUTH_REDIRECT_URI";
    public const string OAuthAuthorizeUrlEnvVar = "MEETUP_OAUTH_AUTHORIZE_URL";
    public const string TokenStorePathEnvVar = "MEETUP_TOKEN_STORE_PATH";

    public string GroupUrlname { get; init; } = string.Empty;
    public MeetupServerMode Mode { get; init; } = MeetupServerMode.Organizer;
    public bool AllowPublish { get; init; }
    public bool AllowDestructive { get; init; }
    public bool UseFakeApi { get; init; }

    public string MeetupApiUrl { get; init; } = "https://api.meetup.com/gql-ext";
    public string OAuthAccessUrl { get; init; } = "https://secure.meetup.com/oauth2/access";

    public string OAuthClientId { get; init; } = string.Empty;
    public string OAuthClientSecret { get; init; } = string.Empty;
    public string OAuthRedirectUri { get; init; } = "http://127.0.0.1:8787/meetup/oauth/callback";
    public string OAuthAuthorizeUrl { get; init; } = "https://secure.meetup.com/oauth2/authorize";
    public string? TokenStorePath { get; init; }

    public static MeetupServerOptions FromConfiguration(IConfiguration configuration)
    {
        var modeText = configuration[ModeEnvVar] ?? "organizer";
        if (!Enum.TryParse<MeetupServerMode>(modeText.Replace("-", string.Empty).Replace("_", string.Empty), ignoreCase: true, out var mode))
        {
            throw new InvalidOperationException($"Invalid {ModeEnvVar} value '{modeText}'. Use 'organizer' or 'read_only'.");
        }

        var options = new MeetupServerOptions
        {
            GroupUrlname = configuration[GroupUrlnameEnvVar]?.Trim() ?? string.Empty,
            Mode = mode,
            AllowPublish = ParseBoolean(configuration, AllowPublishEnvVar),
            AllowDestructive = ParseBoolean(configuration, AllowDestructiveEnvVar),
            UseFakeApi = ParseBoolean(configuration, UseFakeApiEnvVar),
            MeetupApiUrl = configuration[ApiUrlEnvVar]?.Trim() ?? "https://api.meetup.com/gql-ext",
            OAuthAccessUrl = configuration[OAuthAccessUrlEnvVar]?.Trim() ?? "https://secure.meetup.com/oauth2/access",
            OAuthClientId = configuration[ClientIdEnvVar]?.Trim() ?? string.Empty,
            OAuthClientSecret = configuration[ClientSecretEnvVar]?.Trim() ?? string.Empty,
            OAuthRedirectUri = configuration[RedirectUriEnvVar]?.Trim() ?? "http://127.0.0.1:8787/meetup/oauth/callback",
            OAuthAuthorizeUrl = configuration[OAuthAuthorizeUrlEnvVar]?.Trim() ?? "https://secure.meetup.com/oauth2/authorize",
            TokenStorePath = configuration[TokenStorePathEnvVar]?.Trim(),
        };

        Validate(options);
        return options;
    }

    private static bool ParseBoolean(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        return bool.TryParse(value, out var parsed) && parsed;
    }

    private static void Validate(MeetupServerOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.GroupUrlname))
        {
            throw new InvalidOperationException($"{GroupUrlnameEnvVar} is required.");
        }

        if (options.UseFakeApi)
        {
            return;
        }

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(options.OAuthClientId)) missing.Add(ClientIdEnvVar);
        if (string.IsNullOrWhiteSpace(options.OAuthClientSecret)) missing.Add(ClientSecretEnvVar);

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Missing required Meetup OAuth configuration: {string.Join(", ", missing)}. " +
                $"Set {UseFakeApiEnvVar}=true to run with stubbed Meetup responses.");
        }
    }
}
