# meetup-mcp

Organizer-focused MCP server for managing a single Meetup group.

This server is intentionally simple:

- no built-in content templating
- no raw GraphQL passthrough
- single-group scope per server instance
- publish actions gated by explicit configuration

Use your LLM skills/prompts to generate event content. Use this MCP server to execute Meetup operations safely.

## Features

- `get_group`
- `search_events`
- `get_event`
- `list_venues`
- `create_event`
- `edit_event`
- `publish_event` (gated)
- `create_event_photo_upload` (direct upload ticket)
- `attach_event_photo`

## Safety model

The server enforces policy via environment configuration:

- single group lock via `MEETUP_GROUP_URLNAME`
- mode-based capability control (`organizer` vs `read_only`)
- publish blocked by default (`MEETUP_ALLOW_PUBLISH=false`)

Policy violations return errors with `POLICY_DENIED` in the message.

## Configuration

Required:

- `MEETUP_GROUP_URLNAME` - Meetup group URL name for this server instance

Optional behavior flags:

- `MEETUP_MODE` - `organizer` (default) or `read_only`
- `MEETUP_ALLOW_PUBLISH` - `false` (default)
- `MEETUP_ALLOW_DESTRUCTIVE` - `false` (reserved for future destructive tools)
- `MEETUP_USE_FAKE_API` - `false` (set `true` to run locally against in-memory fake responses)

Meetup API endpoint overrides:

- `MEETUP_API_URL` - default `https://api.meetup.com/gql-ext`
- `MEETUP_OAUTH_ACCESS_URL` - default `https://secure.meetup.com/oauth2/access`

OAuth2 settings (required unless `MEETUP_USE_FAKE_API=true`):

- `MEETUP_OAUTH_KEY` - OAuth consumer key from Meetup OAuth client settings
- `MEETUP_OAUTH_SECRET` - OAuth consumer secret
- `MEETUP_OAUTH_REDIRECT_URI` - default `http://127.0.0.1:8787/meetup/oauth/callback`
- `MEETUP_TOKEN_STORE_PATH` - optional, default `~/.meetup-mcp/tokens.json`

## Meetup setup (once per app)

1. Create OAuth client: `https://www.meetup.com/api/oauth/create/`
2. Set **Application Website** to a real URL (e.g. repo home page)
3. Set **Redirect URI** to `http://127.0.0.1:8787/meetup/oauth/callback`
4. Copy consumer key + consumer secret into env vars
5. Run the server; on first API call it will open your browser to authorize and store tokens for reuse

## Image upload flow

Image upload is direct and two-step:

1. Call `create_event_photo_upload` to get `{ photoId, uploadUrl }`
2. Upload image bytes directly to `uploadUrl` using HTTP `PUT`
3. Call `attach_event_photo` with `eventId` + `photoId`

## Local development

```bash
dotnet build MeetupMcp.slnx
dotnet test MeetupMcp.slnx
```

Run server in fake mode:

```bash
MEETUP_GROUP_URLNAME=nhdnug \
MEETUP_USE_FAKE_API=true \
dotnet run --project src/Meetup.McpServer/Meetup.McpServer.csproj
```

## Repository

- GitHub: `https://github.com/Aaronontheweb/meetup-mcp`
