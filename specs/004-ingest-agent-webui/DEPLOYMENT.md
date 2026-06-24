# Deployment & Setup Guide: Ingest Agent

Complete guide to set up, configure, and deploy the Ingest Agent feature across all components (agent, backend Hub, frontend).

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Local Development](#local-development)
3. [Configuration](#configuration)
4. [Running Components](#running-components)
5. [Docker Deployment](#docker-deployment)
6. [Production Checklist](#production-checklist)
7. [Troubleshooting](#troubleshooting)

---

## Prerequisites

### System Requirements

- **.NET 9 SDK** (for agent and backend)
- **Node.js 18+** (for frontend)
- **Git 2.30+** (for LibGit2Sharp)
- **SQLite 3.30+** (included in .NET/Node)
- **Docker & Docker Compose** (optional, for containerized deployment)

### API Keys & Credentials

- **ANTHROPIC_API_KEY** - Claude API key from [Anthropic console](https://console.anthropic.com)
- **Git credentials** - Configure for auto-commits (SSH key or personal access token)

### Ports

- **Agent**: 5100 (default)
- **Backend Hub**: 5001 (default, adjust in config)
- **Frontend Dev**: 5173 (Vite default)

---

## Local Development

### 1. Clone & Setup

```bash
# Clone repository
git clone <repo-url> grimoire
cd grimoire

# Verify .NET 9
dotnet --version

# Verify Node.js
node --version
npm --version
```

### 2. Environment Setup

Create `.env.local` in project root:

```bash
# Anthropic
export ANTHROPIC_API_KEY=sk-ant-...
export ANTHROPIC_MODEL=claude-3-5-sonnet-20241022

# Agent
export INGEST_SOURCE_DIR=./raw/sources
export INGEST_HTTP_PORT=5100
export INGEST_HUB_URL=http://localhost:5001
export INGEST_DB_PATH=./ingest-cache.db

# Backend
export IngestAgent__BaseUrl=http://localhost:5100
export GrimoireDb__Path=./grimoire.db

# Git
export INGEST_GIT_AUTHOR_NAME="Your Name"
export INGEST_GIT_AUTHOR_EMAIL="you@example.com"
```

Load environment:

```bash
source .env.local
```

### 3. Build All Components

```bash
# Backend
dotnet build src/backend/Grimoire.Api/Grimoire.Api.csproj

# Agent
dotnet build src/agents/ingest/Grimoire.Ingest.csproj

# Frontend
cd src/frontend
npm install
npm run build
```

### 4. Run Tests

```bash
# Architecture & integration tests
dotnet test src/backend/Grimoire.ArchTests/

# Backend tests
dotnet test src/backend/Grimoire.Api.Tests/

# All tests
dotnet test
```

---

## Configuration

### Agent Configuration

**File**: `src/agents/ingest/appsettings.json` (optional, env vars take precedence)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  },
  "IngestSourceDir": "raw/sources",
  "IngestHttpPort": "5100",
  "IngestHubUrl": "http://localhost:5001",
  "IngestDbPath": "./ingest-cache.db",
  "IngestFileSizeLimitMb": "10"
}
```

### Backend Configuration

**File**: `src/backend/Grimoire.Api/appsettings.json`

```json
{
  "GrimoireDb": {
    "Path": "./grimoire.db"
  },
  "IngestAgent": {
    "BaseUrl": "http://localhost:5100"
  }
}
```

### Frontend Configuration

**File**: `src/frontend/.env.local` (for development)

```
VITE_API_BASE=http://localhost:5001
VITE_INGEST_HUB_URL=http://localhost:5001/hubs/ingest
```

---

## Running Components

### Option A: Individual Terminal Windows (Recommended for Development)

**Terminal 1: Backend Hub**

```bash
cd /workspaces/grimoire
source .env.local
dotnet run --project src/backend/Grimoire.Api/Grimoire.Api.csproj
# Hub running at http://localhost:5001
```

**Terminal 2: Agent**

```bash
cd /workspaces/grimoire
source .env.local
dotnet run --project src/agents/ingest/Grimoire.Ingest.csproj
# Agent running at http://localhost:5100
```

**Terminal 3: Frontend**

```bash
cd /workspaces/grimoire/src/frontend
npm run dev
# Frontend running at http://localhost:5173
# Auto-proxies to backend at http://localhost:5001
```

### Option B: All at Once with dotnet-tools

```bash
# Install tmux or screen (if not present)
sudo apt-get install tmux

# Create tmux session with 3 panes
tmux new-session -d -s grimoire -x 200 -y 50

tmux send-keys -t grimoire:0 "cd /workspaces/grimoire && source .env.local && dotnet run --project src/backend/Grimoire.Api/Grimoire.Api.csproj" Enter
tmux split-window -t grimoire -h
tmux send-keys -t grimoire:1 "cd /workspaces/grimoire && source .env.local && dotnet run --project src/agents/ingest/Grimoire.Ingest.csproj" Enter
tmux split-window -t grimoire -h
tmux send-keys -t grimoire:2 "cd /workspaces/grimoire/src/frontend && npm run dev" Enter

# Attach to session
tmux attach -t grimoire
```

### Verify Components Are Running

```bash
# Agent health check
curl http://localhost:5100/health

# Backend health check
curl http://localhost:5001/health

# Frontend
open http://localhost:5173
```

---

## Docker Deployment

### Build Docker Images

**Agent**

```bash
cd src/agents/ingest
docker build -t grimoire-ingest:latest .
```

**Backend**

```bash
cd src/backend
docker build -t grimoire-api:latest .
```

**Frontend**

```bash
cd src/frontend
docker build -t grimoire-frontend:latest .
```

### Docker Compose

Create `docker-compose.yml` in project root:

```yaml
version: '3.8'

services:
  backend:
    image: grimoire-api:latest
    ports:
      - "5001:5001"
    environment:
      GrimoireDb__Path: /data/grimoire.db
      IngestAgent__BaseUrl: http://ingest-agent:5100
    volumes:
      - grimoire-data:/data
    networks:
      - grimoire

  ingest-agent:
    image: grimoire-ingest:latest
    ports:
      - "5100:5100"
    environment:
      ANTHROPIC_API_KEY: ${ANTHROPIC_API_KEY}
      INGEST_HUB_URL: http://backend:5001
      INGEST_DB_PATH: /data/ingest-cache.db
    volumes:
      - ingest-data:/data
      - ./raw/sources:/app/raw/sources
      - ./wiki:/app/wiki
    depends_on:
      - backend
    networks:
      - grimoire

  frontend:
    image: grimoire-frontend:latest
    ports:
      - "80:3000"
    environment:
      VITE_API_BASE: http://backend:5001
    depends_on:
      - backend
    networks:
      - grimoire

volumes:
  grimoire-data:
  ingest-data:

networks:
  grimoire:
```

### Run with Docker Compose

```bash
# Set API key
export ANTHROPIC_API_KEY=sk-ant-...

# Build and start
docker-compose up -d

# View logs
docker-compose logs -f

# Stop
docker-compose down
```

---

## Production Checklist

### Security

- [ ] API keys stored in secure vault (AWS Secrets Manager, Azure Key Vault, HashiCorp Vault)
- [ ] SSL/TLS certificates configured (Let's Encrypt or internal CA)
- [ ] CORS policy restricted to trusted domains only
- [ ] Database encrypted at rest
- [ ] Input validation enabled on all endpoints
- [ ] Rate limiting configured (e.g., 100 req/min per IP)
- [ ] OWASP security headers enabled (CSP, X-Frame-Options, etc.)

### Performance

- [ ] Database connection pooling configured
- [ ] SQLite WAL mode enabled for concurrent access
- [ ] Redis caching for frequently accessed data (optional)
- [ ] CDN configured for frontend assets
- [ ] Compression (gzip) enabled
- [ ] Load balancer configured (nginx/haproxy)

### Observability

- [ ] OpenTelemetry collector running (Jaeger, Datadog, New Relic)
- [ ] Structured logging aggregation (ELK, Splunk, CloudWatch)
- [ ] Metrics dashboard set up (Prometheus/Grafana)
- [ ] Alert rules configured (email, Slack, PagerDuty)
- [ ] Health check endpoints monitored
- [ ] SLA tracking enabled

### Infrastructure

- [ ] Database backups scheduled (daily, weekly retention)
- [ ] Log retention policy set (30-90 days)
- [ ] Auto-scaling configured
- [ ] Disaster recovery plan documented
- [ ] Failover tested (agent, backend, frontend)
- [ ] Monitoring uptime > 99%

### Deployment

- [ ] Staging environment mirrors production
- [ ] Blue-green deployment strategy implemented
- [ ] Rollback procedure documented and tested
- [ ] Database migrations versioned and tested
- [ ] Zero-downtime deployment capability verified
- [ ] Change management process followed

---

## Troubleshooting

### Agent Won't Start

```bash
# Check prerequisites
dotnet --version
git --version

# Verify API key
echo $ANTHROPIC_API_KEY

# Check logs
tail -f /tmp/ingest-agent.log

# Test Agent health
curl -v http://localhost:5100/health
```

### Hub Connection Failed

```bash
# Verify Hub is running
curl http://localhost:5001/health

# Check firewall
sudo ufw allow 5001/tcp

# Verify agent can reach Hub
dotnet run -- --verify-hub-connection
```

### Frontend Can't Connect to Backend

```bash
# Check backend running
curl http://localhost:5001/health

# Verify CORS headers
curl -v -H "Origin: http://localhost:5173" http://localhost:5001/

# Check Vite proxy config
cat src/frontend/vite.config.ts
```

### Database Lock Issues

```bash
# Check SQLite WAL files
ls -lah *.db*

# Clear WAL journal (if safe)
rm -f *.db-wal *.db-shm

# Restart services
```

### Out of Memory

```bash
# Check memory usage
docker stats

# Increase container memory limits
docker update --memory 2g container-name

# Profile agent memory
dotnet run -- --profile-memory
```

### Files Not Processing

```bash
# Check source directory
ls -R raw/sources/

# Check file permissions
chmod 755 raw/sources/

# Check logs for errors
curl http://localhost:5100/health | jq .

# Manually trigger run
curl -X POST http://localhost:5001/api/ingest/trigger
```

### Git Commit Failures

```bash
# Verify git config
git config --list

# Check git credentials
git ls-remote --heads origin

# Reset git state
git reset --hard HEAD
```

---

## Support & Resources

- **Documentation**: `/specs/004-ingest-agent-webui/`
- **Architecture Decision Records**: `/docs/adr/`
- **API Contracts**: `/specs/004-ingest-agent-webui/contracts/`
- **Issues & Feedback**: GitHub issues or internal tracker
