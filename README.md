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
- Task inbox (polling first)
- Task status updates
- Heartbeats
- Quotas and basic policy flags
- Admin-friendly API docs

## Current status
Repository bootstrapped. .NET project scaffold is next.

## Docs
See `docs/mvp-spec.md`.
