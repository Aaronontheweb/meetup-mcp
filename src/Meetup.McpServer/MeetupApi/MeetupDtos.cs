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
    string? FeaturedPhotoId);

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

public sealed record PublishEventRequest(string EventId);

public sealed record CreateEventPhotoUploadRequest(string ContentType, bool SetAsMain = false, string PhotoType = "GROUP_PHOTO");

public sealed record EventPhotoUploadTicket(string PhotoId, string UploadUrl, string? BaseUrl, string? ImagePath);
