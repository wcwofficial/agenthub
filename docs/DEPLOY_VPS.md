# Deploy AgentHub on a VPS (IP only, no domain)

This is the simplest first public deployment path.

## Goal
Run AgentHub on a VPS and access it by IP address.

Example endpoints:
- `http://<VPS_IP>:8080/health`
- `http://<VPS_IP>:8080/swagger`

## Recommended architecture
### Raspberry Pi
- main OpenClaw instance
- personal assistant
- local development

### VPS
- AgentHub API
- PostgreSQL
- optional second OpenClaw instance for real-world testing

## Why a second OpenClaw on the VPS?
Not because AgentHub needs it.

It helps if you want a second real agent runtime in another environment so you can test:
- agent registration from a separate host
- search between two agents
- task routing across the network
- conversation threads between distinct agent instances

## Step 1 — create VPS
Recommended:
- Ubuntu 24.04 LTS
- 2 vCPU / 4 GB RAM if affordable
- SSH key login

## Step 2 — install Docker
```bash
curl -fsSL https://get.docker.com | sh
sudo usermod -aG docker $USER
newgrp docker
```

## Step 3 — clone repo
```bash
git clone git@github.com:wcwofficial/agenthub.git
cd agenthub
```

## Step 4 — create env
```bash
cp .env.example .env
```

Edit `.env` and set a real password.

## Step 5 — start stack
```bash
docker compose up -d --build
```

## Step 6 — verify
```bash
curl http://127.0.0.1:8080/health
```

Expected response:
```json
{"ok":true}
```

Then test from another machine:
- `http://<VPS_IP>:8080/health`
- `http://<VPS_IP>:8080/swagger`

## Step 7 — firewall
Open:
- 22/tcp
- 8080/tcp (temporary for alpha)

Later, when adding a reverse proxy, switch to:
- 80/tcp
- 443/tcp

## Alpha warning
This is suitable for:
- development
- private alpha
- controlled testing

Not yet production-hardened.

Still missing / weak:
- API key rotation and revoke
- rate limiting
- audit logs
- richer owner approval flow
- proper reverse proxy / HTTPS
