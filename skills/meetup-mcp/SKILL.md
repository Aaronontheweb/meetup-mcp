---
name: meetup-mcp
description: Use this skill when working with the Meetup MCP server to manage a Meetup group — drafting events, searching events, editing details, managing venues, and uploading photos. Covers the full tool set, auth flow, safety model, and common workflows.
invocable: false
---

# Meetup MCP Server

The Meetup MCP server is a persistent HTTP service that gives Claude direct access to a single Meetup group's organizer tools. It runs via Docker Compose and connects to Claude Code over Streamable HTTP.

## When to Use This Skill

Use this skill when:
- Drafting, editing, or searching Meetup events
- Looking up venue information for the group
- Uploading and attaching event photos
- Troubleshooting auth or tool errors from the `meetup` MCP server
- Explaining the safety model or publish flow to users

---

## Available Tools

### Read (always available)

| Tool | Description |
|------|-------------|
| `authorize` | Start OAuth2 flow. Call this if any tool returns an auth error. Returns a URL to open in your browser. |
| `get_group` | Returns group name, URL name, and member count. |
| `search_events` | Search events by status: `upcoming`, `past`, or `draft`. Returns titles, dates, venues, and IDs. |
| `get_event` | Full event details for a specific event ID including description, venue, and photo. |
| `list_venues` | Lists known venues for the group with address details. |

### Write (requires `organizer` mode)

| Tool | Description |
|------|-------------|
| `create_venue` | Create a new venue for the group. The API may return existing matches in `didYouMean` instead of creating a duplicate. |
| `create_event` | Creates an event as DRAFT by default. Set `publishNow=true` only if `MEETUP_ALLOW_PUBLISH=true`. |
| `edit_event` | Edit any combination of: title, description, startDateTime, duration, venueId, howToFindUs, featuredPhotoId. |
| `add_event_speaker` | Add structured speaker details to an event. Meetup currently exposes one speaker profile per event through this API. |
| `update_event_speaker` | Update the event's structured speaker details or replace/remove its speaker photo. |
| `remove_event_speaker` | Remove structured speaker details from the event. |
| `attach_event_speaker_photo` | Attach an uploaded photo to the event's structured speaker profile. |
| `publish_event` | Publishes a draft event. Requires `MEETUP_ALLOW_PUBLISH=true` AND `confirm=true`. |
| `create_event_photo_upload` | Gets a photo upload ticket with `photoId` and `uploadUrl`. The caller PUT's bytes to `uploadUrl`. |
| `attach_event_photo` | Attaches an uploaded photo to an event as its featured image. |

---

## Safety Model

The server enforces a layered safety model via environment config:

1. **Group lock** — each server instance is bound to one group via `MEETUP_GROUP_URLNAME`. Cross-group operations are blocked.
2. **Mode** — `organizer` (default) enables writes; `read_only` restricts to read tools only.
3. **Publish gate** — `MEETUP_ALLOW_PUBLISH=false` (default) blocks `publish_event`. Publishing requires explicit opt-in.
4. **Draft default** — `create_event` always creates as DRAFT unless `publishNow=true` AND publish is enabled.

Policy violations return errors prefixed with `POLICY_DENIED`.

**The intended workflow is: draft → review → publish.** Never go straight to publish without reviewing the draft first.

---

## Auth Flow

The server uses OAuth2 authorization code flow. Tokens are stored in a Docker volume and persist across restarts — you only authorize once.

### First-time authorization

1. Call `authorize` — it returns a Meetup authorization URL.
2. Open the URL in a browser. Log in and click **Allow**.
3. The browser redirects to `http://127.0.0.1:8787/meetup/oauth/callback?code=...`

**If the server is on a remote machine:** the redirect happens on your local browser, not the server. Copy the full callback URL and send it to the server manually:

```bash
curl "http://YOUR_SERVER:8787/meetup/oauth/callback?code=THE_CODE_FROM_URL"
```

4. Call the original tool again — it will succeed now.

### Token refresh

Access tokens expire after 1 hour. The server automatically refreshes using the stored refresh token. Meetup refresh tokens are single-use — the server always persists the new refresh token immediately after use.

---

## Common Workflows

### Search upcoming events

```
search_events(status="upcoming", limit=10)
```

### Create a venue

```
create_venue(
  name="Community Center",
  address="500 Innovation Blvd",
  city="Houston",
  country="us",
  state="TX"
)
```

Returns `venue` (the created venue) and `didYouMean` (existing similar venues if the API detected a potential duplicate). Note: the Meetup API does not support updating or deleting venues.

### Draft a new event

```
list_venues()  // find the right venueId first

create_event(
  title="Your Event Title",
  description="Event description in plain text or HTML",
  startDateTime="2026-05-15T17:30:00-05:00",
  duration="PT2H30M",
  venueId="VENUE_ID_FROM_LIST_VENUES"
)
```

Returns the created event with its ID. Status will be `DRAFT`.

### Edit a draft

```
edit_event(
  eventId="EVENT_ID",
  title="Updated Title",           // optional
  description="Updated copy",      // optional
  howToFindUs="Parking details"    // optional
)
```

Only include fields you want to change — all fields are optional except `eventId`.

### Publish a draft

Publishing requires `MEETUP_ALLOW_PUBLISH=true` in the server's environment. If it's not set, `publish_event` returns a `POLICY_DENIED` error.

```
publish_event(eventId="EVENT_ID", confirm=true)
```

The `confirm=true` parameter is required as an explicit acknowledgement.

### Attach a photo to an event

```
// Step 1: get upload ticket
create_event_photo_upload(contentType="image/jpeg")
// Returns: { photoId, uploadUrl }

// Step 2: upload bytes (done externally via HTTP PUT to uploadUrl)

// Step 3: attach to event
attach_event_photo(eventId="EVENT_ID", photoId="PHOTO_ID")
```

### Add or update the structured speaker profile

```
add_event_speaker(
  eventId="EVENT_ID",
  name="Jane Doe",
  bio="Principal engineer and community organizer.",
  photoId="PHOTO_ID" // optional
)

update_event_speaker(
  eventId="EVENT_ID",
  bio="Updated speaker bio",
  clearPhoto=false
)

attach_event_speaker_photo(eventId="EVENT_ID", photoId="PHOTO_ID")
```

Meetup's GraphQL surface currently exposes a single `speakerDetails` object per event rather than an arbitrary array of speakers.

---

## Datetime and Duration Formats

- **startDateTime**: ISO-8601 with timezone offset — `2026-05-15T17:30:00-05:00`
- **duration**: ISO-8601 duration — `PT2H` (2 hours), `PT2H30M` (2.5 hours), `PT90M` (90 minutes)

---

## Deployment

The server runs as a persistent Docker Compose service:

```bash
# Build image
dotnet publish src/Meetup.McpServer/Meetup.McpServer.csproj /t:PublishContainer

# Start in background
docker compose up -d

# Check logs
docker compose logs -f

# Stop
docker compose down
```

Claude Code connects via `.mcp.json`:

```json
{
  "mcpServers": {
    "meetup": {
      "type": "http",
      "url": "http://localhost:5180/mcp"
    }
  }
}
```

---

## Troubleshooting

**`authorize` keeps returning an auth URL even after completing the flow**
- The background listener may not have received the callback. Verify you `curl`'d the callback URL to the server, not just opened it in a browser on the same machine.

**Tools return `POLICY_DENIED`**
- Check `MEETUP_MODE=organizer` is set in `.env`
- For publish errors, set `MEETUP_ALLOW_PUBLISH=true` and restart the container

**`search_events` returns empty results**
- Check the `status` value: use `upcoming`, `past`, or `draft`
- `upcoming` maps to Meetup's `ACTIVE` status — published events with future dates

**Container won't start**
- Check `.env` has `MEETUP_OAUTH_KEY` and `MEETUP_OAUTH_SECRET` set
- Run `docker compose logs` to see the startup error
