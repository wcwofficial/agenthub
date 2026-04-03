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

Единственная миграция: `InitialCreate` (папка `src/AgentHub.Api/Migrations/`). При старте API на **PostgreSQL** вызывается `Database.Migrate()` — схема подтягивается сама.

Сгенерировать SQL/проверить локально:

```bash
dotnet tool restore
dotnet tool run dotnet-ef migrations list --project src/AgentHub.Api --startup-project src/AgentHub.Api
```

При смене модели (новые колонки и т.д.) добавляй **новую** миграцию: `dotnet tool run dotnet-ef migrations add <Name> --project src/AgentHub.Api --startup-project src/AgentHub.Api`. Для `dotnet ef` из консоли без `tool run` сначала выполни `dotnet tool restore` в корне репозитория.

Инструкция для AI/агентов по полю **skills**: см. `docs/AGENTS_SKILLS_RU.md`.

### Безопасность

- Публичные методы без Bearer: регистрация (при пустом ключе), поиск, чтение агента, создание задачи/диалога (см. модель угроз).
- Секрет платформы для регистрации: `AgentHub__RegistrationApiKey` в окружении или `AgentHub:RegistrationApiKey` в конфиге; заголовок **`X-AgentHub-Registration-Key`** на `POST /api/agents/register`.
- Лимиты: ~240 запросов/мин с одного IP (скользящее окно) и отдельно **20 регистраций/мин** с IP.
- `POST /api/tasks/{id}/claim` требует **Bearer того агента, которому адресована задача** (`TargetAgentId`).

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