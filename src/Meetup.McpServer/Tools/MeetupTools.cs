using System.ComponentModel;
using Meetup.McpServer.Configuration;
using Meetup.McpServer.MeetupApi;
using Meetup.McpServer.Security;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace Meetup.McpServer.Tools;

[McpServerToolType]
public sealed class MeetupTools
{
    private readonly IMeetupApiClient _apiClient;
    private readonly IAccessTokenProvider _tokenProvider;
    private readonly CapabilityPolicy _policy;
    private readonly MeetupServerOptions _options;

    public MeetupTools(
        IMeetupApiClient apiClient,
        IAccessTokenProvider tokenProvider,
        CapabilityPolicy policy,
        IOptions<MeetupServerOptions> options)
    {
        _apiClient = apiClient;
        _tokenProvider = tokenProvider;
        _policy = policy;
        _options = options.Value;
    }

    [McpServerTool, Description("Authorize this server with Meetup. With no arguments, returns the URL to open in your browser. " +
        "For headless/Docker deployments, open the URL in any browser, then call again with the 'callbackUrl' (the URL your browser was redirected to) " +
        "or just the 'code' parameter from that URL.")]
    public async Task<string> Authorize(
        [Description("The authorization code from the OAuth callback URL.")] string? code = null,
        [Description("The full OAuth callback URL from your browser's address bar. The code will be extracted automatically.")] string? callbackUrl = null,
        CancellationToken cancellationToken = default)
    {
        // Extract code from callbackUrl if provided
        if (code is null && callbackUrl is not null)
        {
            try
            {
                var uri = new Uri(callbackUrl);
                code = System.Web.HttpUtility.ParseQueryString(uri.Query)["code"];
                if (code is null)
                    return "The provided callback URL does not contain a 'code' parameter.";
            }
            catch (UriFormatException)
            {
                return "The provided callback URL is not a valid URL.";
            }
        }

        // If we have a code (from either parameter), exchange it directly
        if (code is not null)
        {
            try
            {
                await _tokenProvider.ExchangeCodeAsync(code, cancellationToken);
                return "Authorization successful! You can now use Meetup tools.";
            }
            catch (HttpRequestException ex)
            {
                return $"Failed to exchange authorization code: {ex.Message}";
            }
        }

        // No code provided — fall through to existing behavior
        try
        {
            await _tokenProvider.GetAccessTokenAsync(cancellationToken);
            return "Already authorized with Meetup.";
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("authorization required"))
        {
            return ex.Message;
        }
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

    [McpServerTool, Description("Create a new venue for the configured Meetup group. The API may return existing matches in 'didYouMean' instead of creating a duplicate.")]
    public Task<CreateVenueResult> CreateVenue(
        [Description("Venue name.")] string name,
        [Description("Street address.")] string address,
        [Description("City.")] string city,
        [Description("Country code (e.g. 'us').")] string country,
        [Description("State or region (optional).")] string? state = null,
        [Description("Venue visibility: 'PUBLIC' or 'GROUP'. Defaults to group setting if omitted.")] string? visibility = null,
        CancellationToken cancellationToken = default)
    {
        _policy.Demand(MeetupCapability.CreateVenue, "create_venue");

        var request = new CreateVenueRequest(
            Name: name,
            Address: address,
            City: city,
            Country: country,
            State: state,
            Visibility: visibility);

        return _apiClient.CreateVenueAsync(_options.GroupUrlname, request, cancellationToken);
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

    [McpServerTool, Description("Add structured speaker details to an event. Meetup currently exposes a single structured speaker profile per event via this API.")]
    public Task<MeetupEvent> AddEventSpeaker(
        [Description("Meetup event ID.")] string eventId,
        [Description("Speaker display name.")] string name,
        [Description("Speaker bio/description text.")] string bio,
        [Description("Optional uploaded photo ID from create_event_photo_upload.")] string? photoId = null,
        CancellationToken cancellationToken = default)
    {
        _policy.Demand(MeetupCapability.EditEvent, "add_event_speaker");

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Speaker name is required.");
        }

        if (string.IsNullOrWhiteSpace(bio))
        {
            throw new InvalidOperationException("Speaker bio is required.");
        }

        return _apiClient.AddEventSpeakerAsync(
            new AddEventSpeakerRequest(eventId, name.Trim(), bio.Trim(), string.IsNullOrWhiteSpace(photoId) ? null : photoId.Trim()),
            cancellationToken);
    }

    [McpServerTool, Description("Update an event's structured speaker details. Meetup currently exposes a single structured speaker profile per event via this API.")]
    public Task<MeetupEvent> UpdateEventSpeaker(
        [Description("Meetup event ID.")] string eventId,
        [Description("Optional updated speaker name.")] string? name = null,
        [Description("Optional updated speaker bio/description.")] string? bio = null,
        [Description("Optional replacement speaker photo ID from create_event_photo_upload.")] string? photoId = null,
        [Description("Set true to remove the current speaker photo.")] bool clearPhoto = false,
        CancellationToken cancellationToken = default)
    {
        _policy.Demand(MeetupCapability.EditEvent, "update_event_speaker");

        if (name is null && bio is null && photoId is null && !clearPhoto)
        {
            throw new InvalidOperationException("Provide at least one speaker field to update.");
        }

        return _apiClient.UpdateEventSpeakerAsync(
            new UpdateEventSpeakerRequest(
                eventId,
                string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
                string.IsNullOrWhiteSpace(bio) ? null : bio.Trim(),
                string.IsNullOrWhiteSpace(photoId) ? null : photoId.Trim(),
                clearPhoto),
            cancellationToken);
    }

    [McpServerTool, Description("Remove structured speaker details from an event.")]
    public Task<MeetupEvent> RemoveEventSpeaker(
        [Description("Meetup event ID.")] string eventId,
        CancellationToken cancellationToken = default)
    {
        _policy.Demand(MeetupCapability.EditEvent, "remove_event_speaker");
        return _apiClient.RemoveEventSpeakerAsync(eventId, cancellationToken);
    }

    [McpServerTool, Description("Attach an uploaded photo ID to the event's structured speaker profile. Create or upload the photo first using create_event_photo_upload.")]
    public Task<MeetupEvent> AttachEventSpeakerPhoto(
        [Description("Meetup event ID.")] string eventId,
        [Description("Photo ID from create_event_photo_upload.")] string photoId,
        CancellationToken cancellationToken = default)
    {
        _policy.Demand(MeetupCapability.EditEvent, "attach_event_speaker_photo");
        return _apiClient.AttachEventSpeakerPhotoAsync(eventId, photoId, cancellationToken);
    }
}
