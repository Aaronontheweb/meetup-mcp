using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Meetup.McpServer.Configuration;
using Meetup.McpServer.Security;
using Microsoft.Extensions.Logging;

namespace Meetup.McpServer.MeetupApi;

public sealed class GraphQlMeetupApiClient : IMeetupApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAccessTokenProvider _accessTokenProvider;
    private readonly MeetupServerOptions _options;
    private readonly ILogger<GraphQlMeetupApiClient> _logger;

    public GraphQlMeetupApiClient(
        IHttpClientFactory httpClientFactory,
        IAccessTokenProvider accessTokenProvider,
        MeetupServerOptions options,
        ILogger<GraphQlMeetupApiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _accessTokenProvider = accessTokenProvider;
        _options = options;
        _logger = logger;
    }

    public async Task<MeetupGroup> GetGroupAsync(string groupUrlname, CancellationToken cancellationToken)
    {
        const string query = """
            query($urlname: String!) {
              groupByUrlname(urlname: $urlname) {
                id
                urlname
                name
                memberships {
                  totalCount
                }
              }
            }
            """;

        var data = await ExecuteAsync(query, new { urlname = groupUrlname }, cancellationToken);
        var group = data.GetProperty("groupByUrlname");

        return new MeetupGroup(
            Id: group.GetStringProperty("id"),
            Urlname: group.GetStringProperty("urlname"),
            Name: group.GetStringProperty("name"),
            MemberCount: group.GetProperty("memberships").GetInt32Property("totalCount"));
    }

    public async Task<IReadOnlyList<MeetupEvent>> SearchEventsAsync(string groupUrlname, string status, int limit, CancellationToken cancellationToken)
    {
        var normalizedStatus = (status ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "" or "upcoming" or "active" => "ACTIVE",
            "past" => "PAST",
            "draft" => "DRAFT",
            _ => throw new InvalidOperationException("Invalid status. Use upcoming, past, or draft.")
        };
        var query = $$"""
            query($urlname: String!, $first: Int!) {
              groupByUrlname(urlname: $urlname) {
                events(first: $first, status: {{normalizedStatus}}) {
                  edges {
                    node {
                      id
                      title
                      description
                      dateTime
                      duration
                      eventUrl
                      status
                      featuredEventPhoto {
                        id
                      }
                      venue {
                        id
                        name
                        address
                        city
                        state
                        country
                      }
                    }
                  }
                }
              }
            }
            """;

        var data = await ExecuteAsync(query, new { urlname = groupUrlname, first = Math.Max(1, limit) }, cancellationToken);
        return ParseEvents(data.GetProperty("groupByUrlname").GetProperty("events").GetProperty("edges"));
    }

    public async Task<MeetupEvent> GetEventAsync(string eventId, CancellationToken cancellationToken)
    {
        const string query = """
            query($eventId: ID!) {
              event(id: $eventId) {
                id
                title
                description
                dateTime
                duration
                eventUrl
                status
                featuredEventPhoto {
                  id
                }
                venue {
                  id
                  name
                  address
                  city
                  state
                  country
                }
              }
            }
            """;

        var data = await ExecuteAsync(query, new { eventId }, cancellationToken);
        return ParseEvent(data.GetProperty("event"));
    }

    public async Task<IReadOnlyList<MeetupVenue>> ListVenuesAsync(string groupUrlname, int limit, CancellationToken cancellationToken)
    {
        const string query = """
            query($urlname: String!, $first: Int!) {
              groupByUrlname(urlname: $urlname) {
                venues(first: $first) {
                  edges {
                    node {
                      id
                      name
                      address
                      city
                      state
                      country
                    }
                  }
                }
              }
            }
            """;

        var data = await ExecuteAsync(query, new { urlname = groupUrlname, first = Math.Max(1, limit) }, cancellationToken);
        var edges = data.GetProperty("groupByUrlname").GetProperty("venues").GetProperty("edges");

        var venues = new List<MeetupVenue>();
        foreach (var edge in edges.EnumerateArray())
        {
            var node = edge.GetProperty("node");
            venues.Add(ParseVenue(node));
        }

        return venues;
    }

    public async Task<MeetupEvent> CreateEventAsync(string groupUrlname, CreateEventRequest request, CancellationToken cancellationToken)
    {
        const string query = """
            mutation($input: CreateEventInput!) {
              createEvent(input: $input) {
                event {
                  id
                  title
                  description
                  dateTime
                  duration
                  eventUrl
                  status
                  venue {
                    id
                    name
                    address
                    city
                    state
                    country
                  }
                }
                errors {
                  message
                  code
                  field
                }
              }
            }
            """;

        var publishStatus = request.PublishImmediately ? "PUBLISHED" : "DRAFT";
        var input = new
        {
            groupUrlname,
            title = request.Title,
            description = request.Description,
            startDateTime = request.StartDateTime.ToString("O"),
            duration = request.Duration,
            venueId = request.VenueId,
            publishStatus
        };

        var data = await ExecuteAsync(query, new { input }, cancellationToken);
        var payload = data.GetProperty("createEvent");
        ThrowMutationErrorsIfAny(payload);
        return ParseEvent(payload.GetProperty("event"));
    }

    public async Task<MeetupEvent> EditEventAsync(EditEventRequest request, CancellationToken cancellationToken)
    {
        const string query = """
            mutation($input: EditEventInput!) {
              editEvent(input: $input) {
                event {
                  id
                  title
                  description
                  dateTime
                  duration
                  eventUrl
                  status
                  featuredEventPhoto {
                    id
                  }
                  venue {
                    id
                    name
                    address
                    city
                    state
                    country
                  }
                }
                errors {
                  message
                  code
                  field
                }
              }
            }
            """;

        var input = new Dictionary<string, object?>
        {
            ["eventId"] = request.EventId,
        };

        if (request.Title is not null) input["title"] = request.Title;
        if (request.Description is not null) input["description"] = request.Description;
        if (request.StartDateTime is not null) input["startDateTime"] = request.StartDateTime.Value.ToString("O");
        if (request.Duration is not null) input["duration"] = request.Duration;
        if (request.VenueId is not null) input["venueId"] = request.VenueId;
        if (request.HowToFindUs is not null) input["howToFindUs"] = request.HowToFindUs;
        if (request.FeaturedPhotoId is not null) input["featuredPhotoId"] = request.FeaturedPhotoId;

        var data = await ExecuteAsync(query, new { input }, cancellationToken);
        var payload = data.GetProperty("editEvent");
        ThrowMutationErrorsIfAny(payload);
        return ParseEvent(payload.GetProperty("event"));
    }

    public Task<MeetupEvent> PublishEventAsync(PublishEventRequest request, CancellationToken cancellationToken)
    {
        var editRequest = new EditEventRequest(
            request.EventId,
            Title: null,
            Description: null,
            StartDateTime: null,
            Duration: null,
            VenueId: null,
            HowToFindUs: null,
            FeaturedPhotoId: null);

        return EditEventWithPublishStatusAsync(editRequest, "PUBLISHED", cancellationToken);
    }

    public async Task<EventPhotoUploadTicket> CreateEventPhotoUploadAsync(
        string groupUrlname,
        CreateEventPhotoUploadRequest request,
        CancellationToken cancellationToken)
    {
        var group = await GetGroupAsync(groupUrlname, cancellationToken);

        const string query = """
            mutation($input: GroupEventPhotoCreateInput!) {
              createGroupEventPhoto(input: $input) {
                uploadUrl
                imagePath
                photo {
                  id
                  baseUrl
                }
              }
            }
            """;

        var contentType = request.ContentType.Trim().ToUpperInvariant() switch
        {
            "IMAGE/JPEG" or "JPEG" or "JPG" => "JPEG",
            "IMAGE/PNG" or "PNG" => "PNG",
            "IMAGE/WEBP" or "WEBP" => "WEBP",
            var unknown => throw new InvalidOperationException($"Unsupported image content type '{unknown}'.")
        };

        var input = new
        {
            groupId = group.Id,
            photoType = request.PhotoType,
            contentType,
            setAsMain = request.SetAsMain
        };

        var data = await ExecuteAsync(query, new { input }, cancellationToken);
        var payload = data.GetProperty("createGroupEventPhoto");

        var photo = payload.GetProperty("photo");
        return new EventPhotoUploadTicket(
            PhotoId: photo.GetStringProperty("id"),
            UploadUrl: payload.GetStringProperty("uploadUrl"),
            BaseUrl: photo.TryGetProperty("baseUrl", out var baseUrl) ? baseUrl.GetString() : null,
            ImagePath: payload.TryGetProperty("imagePath", out var imagePath) ? imagePath.GetString() : null);
    }

    public Task<MeetupEvent> AttachEventPhotoAsync(string eventId, string photoId, CancellationToken cancellationToken)
    {
        var request = new EditEventRequest(
            eventId,
            Title: null,
            Description: null,
            StartDateTime: null,
            Duration: null,
            VenueId: null,
            HowToFindUs: null,
            FeaturedPhotoId: photoId);

        return EditEventAsync(request, cancellationToken);
    }

    private async Task<MeetupEvent> EditEventWithPublishStatusAsync(
        EditEventRequest request,
        string publishStatus,
        CancellationToken cancellationToken)
    {
        const string query = """
            mutation($input: EditEventInput!) {
              editEvent(input: $input) {
                event {
                  id
                  title
                  description
                  dateTime
                  duration
                  eventUrl
                  status
                  featuredEventPhoto {
                    id
                  }
                  venue {
                    id
                    name
                    address
                    city
                    state
                    country
                  }
                }
                errors {
                  message
                  code
                  field
                }
              }
            }
            """;

        var input = new Dictionary<string, object?>
        {
            ["eventId"] = request.EventId,
            ["publishStatus"] = publishStatus,
        };

        var data = await ExecuteAsync(query, new { input }, cancellationToken);
        var payload = data.GetProperty("editEvent");
        ThrowMutationErrorsIfAny(payload);
        return ParseEvent(payload.GetProperty("event"));
    }

    private async Task<JsonElement> ExecuteAsync(string query, object variables, CancellationToken cancellationToken)
    {
        var accessToken = await _accessTokenProvider.GetAccessTokenAsync(cancellationToken);
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var body = new GraphQlRequest(query, variables);
        using var response = await client.PostAsJsonAsync(_options.MeetupApiUrl, body, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken)
                            ?? throw new InvalidOperationException("Meetup API response body was empty.");

        if (payload.RootElement.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0)
        {
            var message = errors.EnumerateArray()
                .Select(e => e.TryGetProperty("message", out var m) ? m.GetString() : "Unknown GraphQL error")
                .Where(static m => !string.IsNullOrWhiteSpace(m))
                .Cast<string>()
                .ToArray();

            throw new InvalidOperationException($"Meetup GraphQL error: {string.Join(" | ", message)}");
        }

        if (!payload.RootElement.TryGetProperty("data", out var data))
        {
            throw new InvalidOperationException("Meetup API response did not contain a 'data' object.");
        }

        _logger.LogDebug("Meetup GraphQL call succeeded.");
        return data.Clone();
    }

    private static void ThrowMutationErrorsIfAny(JsonElement payload)
    {
        if (!payload.TryGetProperty("errors", out var errors) || errors.ValueKind != JsonValueKind.Array || errors.GetArrayLength() == 0)
        {
            return;
        }

        var text = errors.EnumerateArray()
            .Select(error =>
            {
                var message = error.TryGetProperty("message", out var m) ? m.GetString() : "Unknown mutation error";
                var code = error.TryGetProperty("code", out var c) ? c.GetString() : null;
                var field = error.TryGetProperty("field", out var f) ? f.GetString() : null;
                return $"{message} (code={code ?? "n/a"}, field={field ?? "n/a"})";
            });

        throw new InvalidOperationException($"Meetup mutation failed: {string.Join(" | ", text)}");
    }

    private static IReadOnlyList<MeetupEvent> ParseEvents(JsonElement edges)
    {
        var events = new List<MeetupEvent>();
        foreach (var edge in edges.EnumerateArray())
        {
            if (!edge.TryGetProperty("node", out var node))
            {
                continue;
            }

            events.Add(ParseEvent(node));
        }

        return events;
    }

    private static MeetupEvent ParseEvent(JsonElement node)
    {
        var venue = node.TryGetProperty("venue", out var venueJson) && venueJson.ValueKind == JsonValueKind.Object
            ? ParseVenue(venueJson)
            : null;

        var featuredPhotoId = node.TryGetProperty("featuredEventPhoto", out var photo)
                              && photo.ValueKind == JsonValueKind.Object
                              && photo.TryGetProperty("id", out var photoId)
            ? photoId.GetString()
            : null;

        return new MeetupEvent(
            Id: node.GetStringProperty("id"),
            Title: node.GetStringProperty("title"),
            Description: node.TryGetProperty("description", out var description) ? description.GetString() : null,
            StartDateTime: DateTimeOffset.Parse(node.GetStringProperty("dateTime")),
            Duration: node.TryGetProperty("duration", out var duration) ? duration.GetString() : null,
            Status: node.TryGetProperty("status", out var status) ? status.GetString() : null,
            EventUrl: node.TryGetProperty("eventUrl", out var eventUrl) ? eventUrl.GetString() : null,
            Venue: venue,
            FeaturedPhotoId: featuredPhotoId);
    }

    private static MeetupVenue ParseVenue(JsonElement venue)
    {
        return new MeetupVenue(
            Id: venue.GetStringProperty("id"),
            Name: venue.GetStringProperty("name"),
            Address: venue.TryGetProperty("address", out var address) ? address.GetString() : null,
            City: venue.TryGetProperty("city", out var city) ? city.GetString() : null,
            State: venue.TryGetProperty("state", out var state) ? state.GetString() : null,
            Country: venue.TryGetProperty("country", out var country) ? country.GetString() : null);
    }

    private sealed record GraphQlRequest(string Query, object Variables);
}

internal static class JsonElementExtensions
{
    public static string GetStringProperty(this JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"Expected string property '{name}' in Meetup API response.");
        }

        return value.GetString() ?? string.Empty;
    }

    public static int GetInt32Property(this JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number)
        {
            throw new InvalidOperationException($"Expected number property '{name}' in Meetup API response.");
        }

        return value.GetInt32();
    }
}
