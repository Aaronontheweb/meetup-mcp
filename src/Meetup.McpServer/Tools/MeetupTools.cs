using System.ComponentModel;
using Meetup.McpServer.Configuration;
using Meetup.McpServer.MeetupApi;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace Meetup.McpServer.Tools;

[McpServerToolType]
public sealed class MeetupTools
{
    private readonly IMeetupApiClient _apiClient;
    private readonly CapabilityPolicy _policy;
    private readonly MeetupServerOptions _options;

    public MeetupTools(
        IMeetupApiClient apiClient,
        CapabilityPolicy policy,
        IOptions<MeetupServerOptions> options)
    {
        _apiClient = apiClient;
        _policy = policy;
        _options = options.Value;
    }

    [McpServerTool, Description("Get metadata for the configured Meetup group.")]
    public Task<MeetupGroup> GetGroup(CancellationToken cancellationToken)
    {
        _policy.Demand(MeetupCapability.Read, "get_group");
        return _apiClient.GetGroupAsync(_options.GroupUrlname, cancellationToken);
    }

    [McpServerTool, Description("Search events in the configured Meetup group by status.")]
    public Task<IReadOnlyList<MeetupEvent>> SearchEvents(
        [Description("Event status to search: upcoming, past, or draft.")] string status = "upcoming",
        [Description("Maximum number of events to return.")] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        _policy.Demand(MeetupCapability.Read, "search_events");
        return _apiClient.SearchEventsAsync(_options.GroupUrlname, status, Math.Clamp(limit, 1, 100), cancellationToken);
    }

    [McpServerTool, Description("Get full details of a specific event by event ID.")]
    public Task<MeetupEvent> GetEvent(
        [Description("Meetup event ID.")] string eventId,
        CancellationToken cancellationToken)
    {
        _policy.Demand(MeetupCapability.Read, "get_event");
        return _apiClient.GetEventAsync(eventId, cancellationToken);
    }

    [McpServerTool, Description("List known venues for the configured Meetup group.")]
    public Task<IReadOnlyList<MeetupVenue>> ListVenues(
        [Description("Maximum number of venues to return.")] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        _policy.Demand(MeetupCapability.Read, "list_venues");
        return _apiClient.ListVenuesAsync(_options.GroupUrlname, Math.Clamp(limit, 1, 100), cancellationToken);
    }

    [McpServerTool, Description("Create a new event in the configured Meetup group. Defaults to DRAFT unless publish is explicitly requested and enabled.")]
    public Task<MeetupEvent> CreateEvent(
        [Description("Event title.")] string title,
        [Description("Event description; plain text or HTML.")] string description,
        [Description("Event start time in ISO-8601 format with timezone.")] string startDateTime,
        [Description("Event duration in ISO-8601 duration format, e.g. PT2H.")] string duration,
        [Description("Meetup venue ID.")] string venueId,
        [Description("Set to true to publish immediately. Requires MEETUP_ALLOW_PUBLISH=true.")] bool publishNow = false,
        CancellationToken cancellationToken = default)
    {
        _policy.Demand(MeetupCapability.CreateEvent, "create_event");
        if (publishNow)
        {
            _policy.Demand(MeetupCapability.PublishEvent, "create_event(publishNow=true)");
        }

        if (!DateTimeOffset.TryParse(startDateTime, out var start))
        {
            throw new InvalidOperationException("Invalid startDateTime. Use ISO-8601 format.");
        }

        var request = new CreateEventRequest(
            Title: title,
            Description: description,
            StartDateTime: start,
            Duration: duration,
            VenueId: venueId,
            PublishImmediately: publishNow);

        return _apiClient.CreateEventAsync(_options.GroupUrlname, request, cancellationToken);
    }

    [McpServerTool, Description("Edit an existing event using safe editable fields only.")]
    public Task<MeetupEvent> EditEvent(
        [Description("Meetup event ID.")] string eventId,
        [Description("Optional updated title.")] string? title = null,
        [Description("Optional updated description.")] string? description = null,
        [Description("Optional updated startDateTime in ISO-8601 format.")] string? startDateTime = null,
        [Description("Optional updated duration in ISO-8601 format.")] string? duration = null,
        [Description("Optional updated venue ID.")] string? venueId = null,
        [Description("Optional directions/howToFindUs text.")] string? howToFindUs = null,
        [Description("Optional featured photo ID.")] string? featuredPhotoId = null,
        CancellationToken cancellationToken = default)
    {
        _policy.Demand(MeetupCapability.EditEvent, "edit_event");

        DateTimeOffset? parsedDateTime = null;
        if (!string.IsNullOrWhiteSpace(startDateTime))
        {
            if (!DateTimeOffset.TryParse(startDateTime, out var parsed))
            {
                throw new InvalidOperationException("Invalid startDateTime. Use ISO-8601 format.");
            }

            parsedDateTime = parsed;
        }

        var request = new EditEventRequest(
            EventId: eventId,
            Title: title,
            Description: description,
            StartDateTime: parsedDateTime,
            Duration: duration,
            VenueId: venueId,
            HowToFindUs: howToFindUs,
            FeaturedPhotoId: featuredPhotoId);

        return _apiClient.EditEventAsync(request, cancellationToken);
    }

    [McpServerTool, Description("Publish an existing draft event. Disabled unless MEETUP_ALLOW_PUBLISH=true.")]
    public Task<MeetupEvent> PublishEvent(
        [Description("Meetup event ID.")] string eventId,
        [Description("Set to true to confirm you intend to publish this event.")] bool confirm = false,
        CancellationToken cancellationToken = default)
    {
        _policy.Demand(MeetupCapability.PublishEvent, "publish_event");

        if (!confirm)
        {
            throw new InvalidOperationException("POLICY_DENIED: publish_event requires confirm=true.");
        }

        return _apiClient.PublishEventAsync(new PublishEventRequest(eventId), cancellationToken);
    }

    [McpServerTool, Description("Create an event-photo upload ticket. Upload bytes directly to uploadUrl from the caller.")]
    public Task<EventPhotoUploadTicket> CreateEventPhotoUpload(
        [Description("Image content type. Supported: image/jpeg, image/png, image/webp.")] string contentType,
        [Description("Whether to set as main group photo if supported.")] bool setAsMain = false,
        CancellationToken cancellationToken = default)
    {
        _policy.Demand(MeetupCapability.PhotoUpload, "create_event_photo_upload");
        var request = new CreateEventPhotoUploadRequest(contentType, setAsMain);
        return _apiClient.CreateEventPhotoUploadAsync(_options.GroupUrlname, request, cancellationToken);
    }

    [McpServerTool, Description("Attach an uploaded photo ID to an event as its featured image.")]
    public Task<MeetupEvent> AttachEventPhoto(
        [Description("Meetup event ID.")] string eventId,
        [Description("Photo ID from create_event_photo_upload.")] string photoId,
        CancellationToken cancellationToken = default)
    {
        _policy.Demand(MeetupCapability.EditEvent, "attach_event_photo");
        return _apiClient.AttachEventPhotoAsync(eventId, photoId, cancellationToken);
    }
}
