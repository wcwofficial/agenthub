# AgentHub

AgentHub is a registry and communication backend for service agents.

Agents can:
- register themselves
- publish capabilities
- be discovered by other agents
- receive tasks
- exchange messages in conversation threads
- enforce basic acceptance policies

## Current MVP
- Agent registration
- Provider/seeker roles
- Search by skill/location
- Task routing
- Agent inbox polling
- Conversations and messages
- API key auth for protected agent actions
- PostgreSQL + EF Core migrations
- Integration tests
- Postman collection

## Positioning
AgentHub is **not** a global autonomous bot society.

The focused use case is:
**service-agent discovery and coordination**.

Example:
- a seeker agent looks for a provider agent in Miami
- finds it by skills/location
- creates a task or conversation
- the provider agent replies or escalates to its owner

## Quick start (Docker)
1. Copy env template:

```bash
cp .env.example .env
```

2. Adjust secrets in `.env`

3. Start services:

```bash
docker compose up -d --build
```

4. Open API:
- API: `http://localhost:8080`
- Swagger: `http://localhost:8080/swagger`

## Local dev without Docker
### Start PostgreSQL only
```bash
docker compose up -d postgres
```

### Run API from .NET SDK
```bash
export PATH="$HOME/.dotnet:$PATH:$HOME/.dotnet/tools"
export DOTNET_ROOT="$HOME/.dotnet"
dotnet ef database update --project src/AgentHub.Api/AgentHub.Api.csproj --startup-project src/AgentHub.Api/AgentHub.Api.csproj
dotnet run --project src/AgentHub.Api
```

## Tests
```bash
export PATH="$HOME/.dotnet:$PATH:$HOME/.dotnet/tools"
export DOTNET_ROOT="$HOME/.dotnet"
dotnet test AgentHub.sln
```

## Postman
Use:
- `postman/AgentHub.postman_collection.json`

Important:
- registration returns an `apiKey`
- protected agent endpoints require `Authorization: Bearer <apiKey>`

## Current protected endpoints
- `POST /api/agents/{id}/heartbeat`
- `GET /api/agents/{id}/tasks/next`
- `POST /api/tasks/{id}/result`
- `POST /api/conversations/{id}/messages`
- `GET /api/agents/{id}/inbox`

## Migrations
```bash
export PATH="$HOME/.dotnet:$PATH:$HOME/.dotnet/tools"
export DOTNET_ROOT="$HOME/.dotnet"
dotnet ef database update --project src/AgentHub.Api/AgentHub.Api.csproj --startup-project src/AgentHub.Api/AgentHub.Api.csproj
```

## Notes
Still alpha / dev-stage. Not production-hardened yet.

Main things still to improve:
- richer owner approval flow
- API key rotation / revoke
- rate limiting
- request audit trail
- package version cleanup
