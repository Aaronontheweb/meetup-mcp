using System.Collections.Concurrent;

namespace Meetup.McpServer.MeetupApi;

public sealed class FakeMeetupApiClient : IMeetupApiClient
{
    private readonly string _groupUrlname;
    private readonly ConcurrentDictionary<string, MeetupEvent> _events = new();
    private readonly IReadOnlyList<MeetupVenue> _venues;

    public FakeMeetupApiClient(string groupUrlname)
    {
        _groupUrlname = groupUrlname;
        _venues =
        [
            new MeetupVenue("venue-1", "Main Venue", "123 Organizer Ave", "Houston", "TX", "US"),
            new MeetupVenue("venue-2", "Overflow Room", "450 Backup St", "Houston", "TX", "US")
        ];

        var seedEvent = new MeetupEvent(
            Id: "evt-1000",
            Title: "Sample Draft Event",
            Description: "Draft description",
            StartDateTime: DateTimeOffset.UtcNow.AddDays(7),
            Duration: "PT2H",
            Status: "DRAFT",
            EventUrl: $"https://www.meetup.com/{groupUrlname}/events/evt-1000/",
            Venue: _venues[0],
            FeaturedPhotoId: null);

        _events[seedEvent.Id] = seedEvent;
    }

    public Task<MeetupGroup> GetGroupAsync(string groupUrlname, CancellationToken cancellationToken)
    {
        EnsureGroup(groupUrlname);
        return Task.FromResult(new MeetupGroup("group-1", _groupUrlname, "Fake Meetup Group", 123));
    }

    public Task<IReadOnlyList<MeetupEvent>> SearchEventsAsync(string groupUrlname, string status, int limit, CancellationToken cancellationToken)
    {
        EnsureGroup(groupUrlname);

        var normalizedStatus = string.IsNullOrWhiteSpace(status) ? "upcoming" : status.Trim().ToLowerInvariant();
        var matching = _events.Values
            .Where(e =>
                normalizedStatus switch
                {
                    "draft" => string.Equals(e.Status, "DRAFT", StringComparison.OrdinalIgnoreCase),
                    "past" => e.StartDateTime < DateTimeOffset.UtcNow,
                    _ => e.StartDateTime >= DateTimeOffset.UtcNow
                })
            .OrderBy(e => e.StartDateTime)
            .Take(Math.Max(1, limit))
            .ToArray();

        return Task.FromResult<IReadOnlyList<MeetupEvent>>(matching);
    }

    public Task<MeetupEvent> GetEventAsync(string eventId, CancellationToken cancellationToken)
    {
        if (!_events.TryGetValue(eventId, out var existing))
        {
            throw new InvalidOperationException($"Event '{eventId}' was not found.");
        }

        return Task.FromResult(existing);
    }

    public Task<IReadOnlyList<MeetupVenue>> ListVenuesAsync(string groupUrlname, int limit, CancellationToken cancellationToken)
    {
        EnsureGroup(groupUrlname);
        var results = _venues.Take(Math.Max(1, limit)).ToArray();
        return Task.FromResult<IReadOnlyList<MeetupVenue>>(results);
    }

    public Task<MeetupEvent> CreateEventAsync(string groupUrlname, CreateEventRequest request, CancellationToken cancellationToken)
    {
        EnsureGroup(groupUrlname);

        var venue = _venues.FirstOrDefault(v => v.Id == request.VenueId)
                    ?? throw new InvalidOperationException($"Unknown venue '{request.VenueId}'.");

        var id = $"evt-{Random.Shared.Next(1001, 9999)}";
        var status = request.PublishImmediately ? "PUBLISHED" : "DRAFT";

        var created = new MeetupEvent(
            Id: id,
            Title: request.Title,
            Description: request.Description,
            StartDateTime: request.StartDateTime,
            Duration: request.Duration,
            Status: status,
            EventUrl: $"https://www.meetup.com/{_groupUrlname}/events/{id}/",
            Venue: venue,
            FeaturedPhotoId: null);

        _events[id] = created;
        return Task.FromResult(created);
    }

    public Task<MeetupEvent> EditEventAsync(EditEventRequest request, CancellationToken cancellationToken)
    {
        var current = GetEventForUpdate(request.EventId);
        var venue = request.VenueId is null
            ? current.Venue
            : _venues.FirstOrDefault(v => v.Id == request.VenueId)
              ?? throw new InvalidOperationException($"Unknown venue '{request.VenueId}'.");

        var updated = current with
        {
            Title = request.Title ?? current.Title,
            Description = request.Description ?? current.Description,
            StartDateTime = request.StartDateTime ?? current.StartDateTime,
            Duration = request.Duration ?? current.Duration,
            Venue = venue,
            FeaturedPhotoId = request.FeaturedPhotoId ?? current.FeaturedPhotoId,
        };

        _events[updated.Id] = updated;
        return Task.FromResult(updated);
    }

    public Task<MeetupEvent> PublishEventAsync(PublishEventRequest request, CancellationToken cancellationToken)
    {
        var current = GetEventForUpdate(request.EventId);
        var published = current with { Status = "PUBLISHED" };
        _events[published.Id] = published;
        return Task.FromResult(published);
    }

    public Task<EventPhotoUploadTicket> CreateEventPhotoUploadAsync(string groupUrlname, CreateEventPhotoUploadRequest request, CancellationToken cancellationToken)
    {
        EnsureGroup(groupUrlname);

        var photoId = $"photo-{Random.Shared.Next(10000, 99999)}";
        var ticket = new EventPhotoUploadTicket(
            photoId,
            $"https://uploads.example.test/{photoId}",
            "https://secure-content.meetupstatic.com/images/classic-events/",
            string.Empty);

        return Task.FromResult(ticket);
    }

    public Task<MeetupEvent> AttachEventPhotoAsync(string eventId, string photoId, CancellationToken cancellationToken)
    {
        var current = GetEventForUpdate(eventId);
        var updated = current with { FeaturedPhotoId = photoId };
        _events[eventId] = updated;
        return Task.FromResult(updated);
    }

    private MeetupEvent GetEventForUpdate(string eventId)
    {
        if (!_events.TryGetValue(eventId, out var current))
        {
            throw new InvalidOperationException($"Event '{eventId}' was not found.");
        }

        return current;
    }

    private void EnsureGroup(string groupUrlname)
    {
        if (!string.Equals(groupUrlname, _groupUrlname, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Group mismatch. This server is locked to '{_groupUrlname}', requested '{groupUrlname}'.");
        }
    }
}
