using Meetup.McpServer.Configuration;
using Microsoft.Extensions.Options;

namespace Meetup.McpServer.Tests;

public sealed class CapabilityPolicyTests
{
    [Fact]
    public void ReadOnlyMode_DisablesMutations()
    {
        var options = Options.Create(new MeetupServerOptions
        {
            GroupUrlname = "nhdnug",
            Mode = MeetupServerMode.ReadOnly,
            AllowPublish = false,
        });

        var policy = new CapabilityPolicy(options);

        Assert.True(policy.IsAllowed(MeetupCapability.Read));
        Assert.False(policy.IsAllowed(MeetupCapability.CreateEvent));
        Assert.False(policy.IsAllowed(MeetupCapability.EditEvent));
        Assert.False(policy.IsAllowed(MeetupCapability.PublishEvent));
    }

    [Fact]
    public void OrganizerMode_RequiresExplicitPublishFlag()
    {
        var options = Options.Create(new MeetupServerOptions
        {
            GroupUrlname = "nhdnug",
            Mode = MeetupServerMode.Organizer,
            AllowPublish = false,
        });

        var policy = new CapabilityPolicy(options);

        Assert.True(policy.IsAllowed(MeetupCapability.CreateEvent));
        Assert.True(policy.IsAllowed(MeetupCapability.EditEvent));
        Assert.False(policy.IsAllowed(MeetupCapability.PublishEvent));
    }

    [Fact]
    public void OrganizerMode_CanEnablePublish()
    {
        var options = Options.Create(new MeetupServerOptions
        {
            GroupUrlname = "nhdnug",
            Mode = MeetupServerMode.Organizer,
            AllowPublish = true,
        });

        var policy = new CapabilityPolicy(options);
        Assert.True(policy.IsAllowed(MeetupCapability.PublishEvent));
    }
}
