#### 0.1.0 2026-03-31 ####

Initial public release of the Meetup MCP server.

**Features**

* Organizer-focused MCP server for managing a single Meetup group via the Model Context Protocol
* `authorize` — OAuth2 authorization code flow; call once to obtain and persist tokens
* `get_group` — retrieve group metadata and current member count
* `search_events` — search events by status (`upcoming`, `past`, `draft`)
* `get_event` — fetch full event details by event ID
* `list_venues` — list known venues for the group
* `create_event` — create a new event (always as DRAFT unless publishing is explicitly enabled)
* `edit_event` — update title, description, datetime, duration, venue, directions, or featured photo
* `publish_event` — publish a draft event (gated behind `MEETUP_ALLOW_PUBLISH=true`)
* `create_event_photo_upload` — obtain a direct upload ticket (`photoId` + `uploadUrl`)
* `attach_event_photo` — attach an uploaded photo to an event

**Deployment**

* Runs as a persistent HTTP service via Docker Compose; connect from any Claude Code session
* Streamable HTTP transport on `http://localhost:5180/mcp`
* Tokens persisted in a Docker volume (`meetup-mcp-tokens`); authorize once per token lifetime
* `.mcp.json` included for one-line Claude Code connection
* Docker images published to Docker Hub (`aaronontheweb/meetup-mcp`) on each release tag

**Safety model**

* Single-group lock via `MEETUP_GROUP_URLNAME`
* Mode-based capability control (`organizer` vs `read_only`)
* Publishing blocked by default (`MEETUP_ALLOW_PUBLISH=false`)
* Events created as DRAFT unless publish is explicitly enabled
* Policy violations return errors prefixed with `POLICY_DENIED`

#### 1.0.0 April 10th 2025 ####

Example release notes