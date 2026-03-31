namespace Meetup.McpServer.MeetupApi;

public interface IMeetupApiClient
{
    Task<MeetupGroup> GetGroupAsync(string groupUrlname, CancellationToken cancellationToken);

    Task<IReadOnlyList<MeetupEvent>> SearchEventsAsync(
        string groupUrlname,
        string status,
        int limit,
        CancellationToken cancellationToken);

    Task<MeetupEvent> GetEventAsync(string eventId, CancellationToken cancellationToken);

    Task<IReadOnlyList<MeetupVenue>> ListVenuesAsync(string groupUrlname, int limit, CancellationToken cancellationToken);

    Task<MeetupEvent> CreateEventAsync(string groupUrlname, CreateEventRequest request, CancellationToken cancellationToken);

    Task<MeetupEvent> EditEventAsync(EditEventRequest request, CancellationToken cancellationToken);

    Task<MeetupEvent> PublishEventAsync(PublishEventRequest request, CancellationToken cancellationToken);

    Task<MeetupEvent> AddEventSpeakerAsync(AddEventSpeakerRequest request, CancellationToken cancellationToken);

    Task<MeetupEvent> UpdateEventSpeakerAsync(UpdateEventSpeakerRequest request, CancellationToken cancellationToken);

    Task<MeetupEvent> RemoveEventSpeakerAsync(string eventId, CancellationToken cancellationToken);

    Task<MeetupEvent> AttachEventSpeakerPhotoAsync(string eventId, string photoId, CancellationToken cancellationToken);

    Task<EventPhotoUploadTicket> CreateEventPhotoUploadAsync(
        string groupUrlname,
        CreateEventPhotoUploadRequest request,
        CancellationToken cancellationToken);

    Task<MeetupEvent> AttachEventPhotoAsync(string eventId, string photoId, CancellationToken cancellationToken);
}
