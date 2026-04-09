using Meetup.McpServer.MeetupApi;

namespace Meetup.McpServer.Tests;

public sealed class FakeMeetupApiClientTests
{
    [Fact]
    public async Task CanCreateEditAndPublishEvent()
    {
        var api = new FakeMeetupApiClient("nhdnug");

        var created = await api.CreateEventAsync(
            "nhdnug",
            new CreateEventRequest(
                Title: "Intro to MCP",
                Description: "Draft event",
                StartDateTime: DateTimeOffset.UtcNow.AddDays(2),
                Duration: "PT2H",
                VenueId: "venue-1"),
            CancellationToken.None);

        Assert.Equal("DRAFT", created.Status);

        var edited = await api.EditEventAsync(
            new EditEventRequest(
                EventId: created.Id,
                Title: "Intro to MCP (Updated)",
                Description: null,
                StartDateTime: null,
                Duration: null,
                VenueId: null,
                HowToFindUs: null,
                FeaturedPhotoId: null),
            CancellationToken.None);

        Assert.Equal("Intro to MCP (Updated)", edited.Title);

        var published = await api.PublishEventAsync(new PublishEventRequest(created.Id), CancellationToken.None);
        Assert.Equal("PUBLISHED", published.Status);
    }

    [Fact]
    public async Task CanCreateUploadTicketAndAttachPhoto()
    {
        var api = new FakeMeetupApiClient("nhdnug");
        var created = await api.CreateEventAsync(
            "nhdnug",
            new CreateEventRequest(
                Title: "Photo Test",
                Description: "Attach photo",
                StartDateTime: DateTimeOffset.UtcNow.AddDays(3),
                Duration: "PT2H",
                VenueId: "venue-1"),
            CancellationToken.None);

        var ticket = await api.CreateEventPhotoUploadAsync(
            "nhdnug",
            new CreateEventPhotoUploadRequest("image/jpeg"),
            CancellationToken.None);

        Assert.StartsWith("photo-", ticket.PhotoId, StringComparison.Ordinal);
        Assert.StartsWith("https://uploads.example.test/", ticket.UploadUrl, StringComparison.Ordinal);

        var updated = await api.AttachEventPhotoAsync(created.Id, ticket.PhotoId, CancellationToken.None);
        Assert.Equal(ticket.PhotoId, updated.FeaturedPhotoId);
    }

    [Fact]
    public async Task CanManageStructuredSpeakerDetails()
    {
        var api = new FakeMeetupApiClient("nhdnug");
        var created = await api.CreateEventAsync(
            "nhdnug",
            new CreateEventRequest(
                Title: "Speaker Test",
                Description: "Speaker support",
                StartDateTime: DateTimeOffset.UtcNow.AddDays(4),
                Duration: "PT1H30M",
                VenueId: "venue-1"),
            CancellationToken.None);

        var withSpeaker = await api.AddEventSpeakerAsync(
            new AddEventSpeakerRequest(created.Id, "Jane Doe", "Principal engineer and community organizer.", null),
            CancellationToken.None);

        Assert.Single(withSpeaker.Speakers);
        Assert.Equal("Jane Doe", withSpeaker.Speakers[0].Name);

        var withPhoto = await api.AttachEventSpeakerPhotoAsync(created.Id, "photo-12345", CancellationToken.None);
        Assert.Equal("photo-12345", withPhoto.Speakers[0].PhotoId);

        var updated = await api.UpdateEventSpeakerAsync(
            new UpdateEventSpeakerRequest(created.Id, Name: null, Bio: "Updated bio", PhotoId: null),
            CancellationToken.None);

        Assert.Equal("Updated bio", updated.Speakers[0].Bio);
        Assert.Equal("photo-12345", updated.Speakers[0].PhotoId);

        var cleared = await api.UpdateEventSpeakerAsync(
            new UpdateEventSpeakerRequest(created.Id, Name: null, Bio: null, PhotoId: null, ClearPhoto: true),
            CancellationToken.None);

        Assert.Null(cleared.Speakers[0].PhotoId);

        var removed = await api.RemoveEventSpeakerAsync(created.Id, CancellationToken.None);
        Assert.Empty(removed.Speakers);
    }

    [Fact]
    public async Task EditEvent_PreservesExistingSpeaker()
    {
        var api = new FakeMeetupApiClient("nhdnug");

        var created = await api.CreateEventAsync(
            "nhdnug",
            new CreateEventRequest(
                Title: "Speaker Preservation Test",
                Description: "Original description",
                StartDateTime: DateTimeOffset.UtcNow.AddDays(5),
                Duration: "PT2H",
                VenueId: "venue-1"),
            CancellationToken.None);

        await api.AddEventSpeakerAsync(
            new AddEventSpeakerRequest(created.Id, "Alice Smith", "Expert in distributed systems.", null),
            CancellationToken.None);

        var edited = await api.EditEventAsync(
            new EditEventRequest(
                EventId: created.Id,
                Title: null,
                Description: "Updated description",
                StartDateTime: null,
                Duration: null,
                VenueId: null,
                HowToFindUs: null,
                FeaturedPhotoId: null),
            CancellationToken.None);

        Assert.Equal("Updated description", edited.Description);
        Assert.Single(edited.Speakers);
        Assert.Equal("Alice Smith", edited.Speakers[0].Name);
        Assert.Equal("Expert in distributed systems.", edited.Speakers[0].Bio);
    }

    [Fact]
    public async Task CanCreateVenueAndUseInEvent()
    {
        var api = new FakeMeetupApiClient("nhdnug");

        var result = await api.CreateVenueAsync(
            "nhdnug",
            new CreateVenueRequest(
                Name: "New Conference Center",
                Address: "500 Innovation Blvd",
                City: "Houston",
                Country: "us",
                State: "TX"),
            CancellationToken.None);

        Assert.NotNull(result.Venue);
        Assert.Equal("New Conference Center", result.Venue.Name);
        Assert.Equal("500 Innovation Blvd", result.Venue.Address);
        Assert.Empty(result.DidYouMean);

        // Verify the new venue appears in list
        var venues = await api.ListVenuesAsync("nhdnug", 100, CancellationToken.None);
        Assert.Contains(venues, v => v.Id == result.Venue.Id);

        // Verify the new venue can be used when creating an event
        var created = await api.CreateEventAsync(
            "nhdnug",
            new CreateEventRequest(
                Title: "Event at New Venue",
                Description: "Testing new venue",
                StartDateTime: DateTimeOffset.UtcNow.AddDays(7),
                Duration: "PT2H",
                VenueId: result.Venue.Id),
            CancellationToken.None);

        Assert.Equal(result.Venue.Id, created.Venue?.Id);
    }
}
