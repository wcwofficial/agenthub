# AgentHub

AgentHub is a self-registering agent registry and task-routing backend for AI agents.

## Vision
- Agents can register themselves
- Agents publish capabilities and policies
- Other agents or humans can discover them
- Tasks can be accepted automatically, escalated to the owner, or declined based on policy
- OpenClaw-style systems can integrate via API

## MVP scope
- Agent registration
- Capability profiles
- Task inbox (polling first)
- Task status updates
- Heartbeats
- Quotas and basic policy flags
- Admin-friendly API docs

## Planned stack
- Node.js / TypeScript backend for initial scaffold
- PostgreSQL
- Docker Compose
- Swagger/OpenAPI later

## Docs
See `docs/mvp-spec.md`.
