namespace Meetup.McpServer.Security;

public interface IAccessTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Exchanges an authorization code for an access token, bypassing the HTTP callback listener.
    /// Used for headless deployments where the user pastes the code manually.
    /// </summary>
    Task ExchangeCodeAsync(string code, CancellationToken cancellationToken);
}
