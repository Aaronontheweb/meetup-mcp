namespace Meetup.McpServer.MeetupApi;

public sealed record MeetupGroup(string Id, string Urlname, string Name, int MemberCount);

public sealed record MeetupVenue(string Id, string Name, string? Address, string? City, string? State, string? Country);

public sealed record MeetupEvent(
    string Id,
    string Title,
    string? Description,
    DateTimeOffset StartDateTime,
    string? Duration,
    string? Status,
    string? EventUrl,
    MeetupVenue? Venue,
    string? FeaturedPhotoId,
    IReadOnlyList<MeetupSpeaker> Speakers);

public sealed record MeetupSpeaker(string Name, string Bio, string? PhotoId);

public sealed record CreateEventRequest(
    string Title,
    string Description,
    DateTimeOffset StartDateTime,
    string Duration,
    string VenueId,
    bool PublishImmediately = false);

public sealed record EditEventRequest(
    string EventId,
    string? Title,
    string? Description,
    DateTimeOffset? StartDateTime,
    string? Duration,
    string? VenueId,
    string? HowToFindUs,
    string? FeaturedPhotoId);

public sealed record AddEventSpeakerRequest(
    string EventId,
    string Name,
    string Bio,
    string? PhotoId);

public sealed record UpdateEventSpeakerRequest(
    string EventId,
    string? Name,
    string? Bio,
    string? PhotoId,
    bool ClearPhoto = false);

public sealed record PublishEventRequest(string EventId);

public sealed record CreateEventPhotoUploadRequest(string ContentType, bool SetAsMain = false, string PhotoType = "GROUP_PHOTO");

public sealed record CreateVenueRequest(
    string Name,
    string Address,
    string City,
    string Country,
    string? State = null,
    string? Visibility = null);

public sealed record CreateVenueResult(
    MeetupVenue? Venue,
    IReadOnlyList<MeetupVenue> DidYouMean);

public sealed record EventPhotoUploadTicket(string PhotoId, string UploadUrl, string? BaseUrl, string? ImagePath);
