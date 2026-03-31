using Meetup.McpServer.Configuration;
using Meetup.McpServer.MeetupApi;
using Meetup.McpServer.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services
    .AddSingleton(sp => MeetupServerOptions.FromConfiguration(sp.GetRequiredService<IConfiguration>()))
    .AddSingleton<IOptions<MeetupServerOptions>>(sp => Options.Create(sp.GetRequiredService<MeetupServerOptions>()))
    .AddSingleton<CapabilityPolicy>()
    .AddHttpClient()
    .AddSingleton<OAuthTokenStore>(sp =>
    {
        var opts = sp.GetRequiredService<IOptions<MeetupServerOptions>>().Value;
        return new OAuthTokenStore(opts.TokenStorePath);
    })
    .AddSingleton<IAccessTokenProvider, OAuthAccessTokenProvider>()
    .AddSingleton<IMeetupApiClient>(sp =>
    {
        var options = sp.GetRequiredService<IOptions<MeetupServerOptions>>().Value;
        if (options.UseFakeApi)
        {
            return new FakeMeetupApiClient(options.GroupUrlname);
        }

        var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
        var tokenProvider = sp.GetRequiredService<IAccessTokenProvider>();
        var logger = sp.GetRequiredService<ILogger<GraphQlMeetupApiClient>>();

        return new GraphQlMeetupApiClient(httpClientFactory, tokenProvider, options, logger);
    });

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();
app.MapMcp();
await app.RunAsync();
