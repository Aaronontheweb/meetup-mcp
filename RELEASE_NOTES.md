#### 0.2.2 2026-04-09 ####

Fixes `create_venue` throwing when the Meetup API returns duplicate venue suggestions.

**Bug Fixes**

* Fixed `create_venue` throwing `InvalidOperationException` on `venue_exists` instead of returning `didYouMean` suggestions ([#33](https://github.com/Aaronontheweb/meetup-mcp/issues/33), [#34](https://github.com/Aaronontheweb/meetup-mcp/pull/34))

#### 0.2.1 2026-04-08 ####

Adds venue creation support to the MCP server.

**Features**

* Added `create_venue` tool for creating new venues via the Meetup GraphQL API. The API supports deduplication — similar existing venues are returned in the `didYouMean` field rather than creating duplicates. ([#30](https://github.com/Aaronontheweb/meetup-mcp/issues/30), [#31](https://github.com/Aaronontheweb/meetup-mcp/pull/31))

#### 0.2.0 2026-03-31 ####

Fixes several bugs in event editing and Docker deployment, and adds headless OAuth support for server and container environments.

**Bug Fixes**

* Fixed `edit_event` clearing structured speakers when updating other event fields ([#24](https://github.com/Aaronontheweb/meetup-mcp/issues/24), [#25](https://github.com/Aaronontheweb/meetup-mcp/pull/25))
* Fixed Docker volume mount for token store now using correct ownership (`app:app` instead of `root:root`) ([#19](https://github.com/Aaronontheweb/meetup-mcp/issues/19), [#26](https://github.com/Aaronontheweb/meetup-mcp/pull/26))
* Fixed OAuth callback HTTP listener to loop until a valid authorization code arrives instead of failing on the first bad request

**Features**

* `authorize` tool now accepts optional `code` and `callbackUrl` parameters for headless and Docker deployments ([#18](https://github.com/Aaronontheweb/meetup-mcp/issues/18), [#27](https://github.com/Aaronontheweb/meetup-mcp/pull/27))

#### 0.1.1 2026-03-31 ####

Improves day-to-day event management by adding structured speaker tooling and simplifying Docker-based local deployment.

**Features**

* Added dedicated speaker management tools for Meetup events, including structured speaker bios and speaker photo support

**Deployment**

* Updated Docker Compose to use host networking so the OAuth callback listener is directly reachable on port `8787`
* Configured the service to listen on `http://localhost:5180/mcp` without requiring the previous container port mapping setup
* Switched Docker Compose to the published Docker Hub image for simpler startup from release artifacts

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
