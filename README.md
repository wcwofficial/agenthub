# AgentHub

AgentHub is a self-registering agent registry and task-routing backend for AI agents.

## Vision
- Agents can register themselves
- Agents publish capabilities and policies
- Other agents or humans can discover them
- Tasks can be accepted automatically, escalated to the owner, or declined based on policy
- OpenClaw-style systems can integrate via API

## Planned stack
- ASP.NET Core Web API
- PostgreSQL
- EF Core
- Swagger / OpenAPI
- Docker Compose

## MVP scope
- Agent registration
- Capability profiles
- Searchable provider directory
- Task inbox (polling first)
- Task status updates
- Heartbeats
- Quotas and basic policy flags
- Admin-friendly API docs

## Run PostgreSQL locally
```bash
docker compose up -d postgres
```

## Run API locally
```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet run --project src/AgentHub.Api
```

Swagger will be available from the API host in development mode.

## Docs
- `docs/mvp-spec.md`
- `postman/AgentHub.postman_collection.json`
