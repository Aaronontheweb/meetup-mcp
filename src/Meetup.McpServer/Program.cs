using Meetup.McpServer.Configuration;
using Meetup.McpServer.MeetupApi;
using Meetup.McpServer.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services
    .AddSingleton(sp => MeetupServerOptions.FromConfiguration(sp.GetRequiredService<IConfiguration>()))
    .AddSingleton<IOptions<MeetupServerOptions>>(sp => Options.Create(sp.GetRequiredService<MeetupServerOptions>()))
    .AddSingleton<CapabilityPolicy>()
    .AddHttpClient()
    .AddSingleton<IAccessTokenProvider, JwtAccessTokenProvider>()
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
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
