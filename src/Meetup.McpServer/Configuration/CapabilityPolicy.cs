using Microsoft.Extensions.Options;

namespace Meetup.McpServer.Configuration;

public enum MeetupCapability
{
    Read,
    CreateEvent,
    EditEvent,
    PublishEvent,
    Destructive,
    PhotoUpload,
    CreateVenue
}

public sealed class CapabilityPolicy
{
    private readonly MeetupServerOptions _options;

    public CapabilityPolicy(IOptions<MeetupServerOptions> options)
    {
        _options = options.Value;
    }

    public bool IsAllowed(MeetupCapability capability)
    {
        return capability switch
        {
            MeetupCapability.Read => true,
            MeetupCapability.CreateEvent => _options.Mode == MeetupServerMode.Organizer,
            MeetupCapability.EditEvent => _options.Mode == MeetupServerMode.Organizer,
            MeetupCapability.PhotoUpload => _options.Mode == MeetupServerMode.Organizer,
            MeetupCapability.PublishEvent => _options.Mode == MeetupServerMode.Organizer && _options.AllowPublish,
            MeetupCapability.CreateVenue => _options.Mode == MeetupServerMode.Organizer,
            MeetupCapability.Destructive => _options.Mode == MeetupServerMode.Organizer && _options.AllowDestructive,
            _ => false
        };
    }

    public void Demand(MeetupCapability capability, string action)
    {
        if (!IsAllowed(capability))
        {
            throw new InvalidOperationException($"POLICY_DENIED: '{action}' is disabled by server configuration.");
        }
    }
}
