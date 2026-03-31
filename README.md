# meetup-mcp

[![Docker Hub](https://img.shields.io/docker/v/aaronontheweb/meetup-mcp?label=Docker%20Hub&logo=docker&sort=semver)](https://hub.docker.com/r/aaronontheweb/meetup-mcp)

Organizer-focused MCP server for managing a single Meetup group. Runs as a persistent HTTP service via Docker Compose — start it once, connect from any Claude Code session.

This server is intentionally simple:

- no built-in content templating
- no raw GraphQL passthrough
- single-group scope per server instance
- publish actions gated by explicit configuration

Use your LLM to generate event content. Use this MCP server to execute Meetup operations safely.

## Features

- `authorize` — OAuth2 authorization flow (first run only)
- `get_group` — group metadata and member count
- `search_events` — search by status: `upcoming`, `past`, `draft`
- `get_event` — full event details by ID
- `list_venues` — known venues for the group
- `create_event` — creates as DRAFT by default
- `edit_event` — update title, description, datetime, duration, venue, directions, featured photo
- `publish_event` — gated behind `MEETUP_ALLOW_PUBLISH=true`
- `create_event_photo_upload` — get a direct upload ticket (photoId + uploadUrl)
- `attach_event_photo` — attach an uploaded photo to an event

## Safety model

- Single group lock via `MEETUP_GROUP_URLNAME`
- Mode-based capability control (`organizer` vs `read_only`)
- All writes require `organizer` mode
- Publishing blocked by default (`MEETUP_ALLOW_PUBLISH=false`)
- Events created as DRAFT unless publish is explicitly enabled and requested

Policy violations return errors prefixed with `POLICY_DENIED`.

## Deployment

### Prerequisites

- Docker + Docker Compose
- A Meetup Pro account with API access
- A Meetup OAuth consumer (key + secret)

### 1. Create a Meetup OAuth consumer

1. Go to `https://www.meetup.com/api/oauth/create/`
2. Set **Redirect URI** to `http://127.0.0.1:8787/meetup/oauth/callback`
3. Copy your **Consumer Key** and **Consumer Secret**

### 2. Configure environment

Copy `.env.example` to `.env` and fill in your values:

```bash
cp .env.example .env
```

```env
MEETUP_GROUP_URLNAME=your-group-urlname
MEETUP_MODE=organizer
MEETUP_ALLOW_PUBLISH=false
MEETUP_OAUTH_KEY=your-consumer-key
MEETUP_OAUTH_SECRET=your-consumer-secret
```

### 3. Build the container image

```bash
dotnet publish src/Meetup.McpServer/Meetup.McpServer.csproj /t:PublishContainer
```

### 4. Start the server

```bash
docker compose up -d
```

The server runs on `http://localhost:5180/mcp` and restarts automatically unless explicitly stopped.

### 5. Configure Claude Code

The included `.mcp.json` points Claude Code at the running server:

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

### 6. Authorize (first run only)

The first time you make any API call, the server will prompt for authorization. Call the `authorize` tool — it returns a URL to open in your browser. After you authorize on Meetup's site, the browser will redirect to `http://127.0.0.1:8787/meetup/oauth/callback?code=...`.

**Important:** If the server is running on a remote machine (VM, cloud), the browser redirect lands on your local machine, not the server. Copy the full callback URL and run:

```bash
curl "http://YOUR_SERVER:8787/meetup/oauth/callback?code=THE_CODE"
```

Tokens are persisted in a Docker volume (`meetup-mcp-tokens`) and survive container restarts. You only need to authorize once per token lifetime.

## Configuration reference

| Variable | Required | Default | Description |
|---|---|---|---|
| `MEETUP_GROUP_URLNAME` | Yes | — | Meetup group URL name (e.g. `nhdnug`) |
| `MEETUP_MODE` | No | `organizer` | `organizer` or `read_only` |
| `MEETUP_ALLOW_PUBLISH` | No | `false` | Enable `publish_event` tool |
| `MEETUP_ALLOW_DESTRUCTIVE` | No | `false` | Reserved for future destructive operations |
| `MEETUP_USE_FAKE_API` | No | `false` | Use in-memory fake responses (local dev) |
| `MEETUP_OAUTH_KEY` | Yes* | — | OAuth consumer key |
| `MEETUP_OAUTH_SECRET` | Yes* | — | OAuth consumer secret |
| `MEETUP_OAUTH_REDIRECT_URI` | No | `http://127.0.0.1:8787/meetup/oauth/callback` | OAuth redirect URI |
| `MEETUP_TOKEN_STORE_PATH` | No | `~/.meetup-mcp/tokens.json` | Token persistence path |
| `MEETUP_API_URL` | No | `https://api.meetup.com/gql-ext` | Meetup GraphQL endpoint |
| `MEETUP_OAUTH_ACCESS_URL` | No | `https://secure.meetup.com/oauth2/access` | Token exchange endpoint |

*Not required when `MEETUP_USE_FAKE_API=true`

## Image upload flow

Photo upload is a two-step process:

1. Call `create_event_photo_upload` → returns `{ photoId, uploadUrl }`
2. `PUT` image bytes directly to `uploadUrl`
3. Call `attach_event_photo` with `eventId` + `photoId`

## Local development

```bash
dotnet build MeetupMcp.slnx
dotnet test MeetupMcp.slnx
```

Run against in-memory fake data (no Meetup credentials needed):

```bash
MEETUP_GROUP_URLNAME=nhdnug \
MEETUP_USE_FAKE_API=true \
dotnet run --project src/Meetup.McpServer/Meetup.McpServer.csproj
```

## Repository

- GitHub: `https://github.com/Aaronontheweb/meetup-mcp`
