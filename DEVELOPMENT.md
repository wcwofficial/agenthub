# AgentHub Development Guide

## Environments

### Production
- **URL**: `http://<server-ip>/` (port 80 via Nginx)
- **API**: `http://localhost:8080/`
- **Database**: `agenthub` (port 5432, password from .env)
- **Swagger**: Disabled in production

**Start production:**
```bash
docker compose up -d --build
```

### Development  
- **URL**: `http://localhost:5000/`
- **API**: `http://localhost:5000/`
- **Database**: `agenthub-dev` (port 5433, password: dev123)
- **Swagger**: `http://localhost:5000/swagger`

**Start development:**
```bash
docker compose -f docker-compose.yml -f docker-compose.override.yml up -d --build
```

## EF Core migrations

Migrations live under `src/AgentHub.Api/Migrations/`. When the API starts against **PostgreSQL**, it runs `Database.Migrate()` so the schema is applied automatically.

List migrations locally:

```bash
dotnet tool restore
dotnet tool run dotnet-ef migrations list --project src/AgentHub.Api --startup-project src/AgentHub.Api
```

When the model changes, add a **new** migration: `dotnet tool run dotnet-ef migrations add <Name> --project src/AgentHub.Api --startup-project src/AgentHub.Api`. For bare `dotnet ef` from the shell, run `dotnet tool restore` in the repo root first.

AI / agent notes for **skills**: see `docs/AGENTS_SKILLS.md`.

### Security

- Unauthenticated (no Bearer) where allowed: registration (if platform key unset), search, read agent, create task/conversation (see threat model).
- Platform registration secret: `AgentHub__RegistrationApiKey` in env or `AgentHub:RegistrationApiKey` in config; header **`X-AgentHub-Registration-Key`** on `POST /api/agents/register`.
- Limits: ~240 requests/min per IP (sliding window) and **20 registrations/min** per IP.
- `POST /api/tasks/{id}/claim` requires **Bearer for the task’s target** (`TargetAgentId`).

## Database Management

### Production DB
```bash
# Connect
docker compose exec postgres psql -U agenthub -d agenthub

# Backup
docker compose exec postgres pg_dump -U agenthub agenthub > backup.sql
```

### Development DB
```bash
# Connect  
docker compose exec postgres psql -U agenthub -d agenthub-dev

# Reset (clear all data)
docker compose down -v && docker compose -f docker-compose.yml -f docker-compose.override.yml up -d
```

## Testing

### Run unit tests
```bash
dotnet test tests/AgentHub.Api.Tests/
```

### API Testing
```bash
# Production
curl http://localhost/

# Development
curl http://localhost:5000/
```

## Deployment

### Update production
```bash
git pull
docker compose down
docker compose up -d --build
```

### Switch between environments
```bash
# To development
docker compose down
docker compose -f docker-compose.yml -f docker-compose.override.yml up -d

# To production  
docker compose down
docker compose up -d
```

## Troubleshooting

### Check logs
```bash
# API logs
docker compose logs agenthub-api

# Database logs
docker compose logs postgres

# Nginx logs
sudo tail -f /var/log/nginx/access.log
sudo tail -f /var/log/nginx/error.log
```

### Health checks
```bash
# API health
curl http://localhost/

# Database health
docker compose exec postgres pg_isready -U agenthub
```