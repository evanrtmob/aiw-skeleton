# AppInWhats

B2B sales management platform built around WhatsApp integration. Manages agents, clients, orders, products, conversations, and a public web catalog with an AI chat module.

**Stack:** Angular 16 · Express/TypeScript · MySQL · Nginx · Docker

---

## Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (Windows)
- PowerShell 7+

---

## Development

### First time (or after `package.json` / Dockerfile changes)

```powershell
.\build.ps1
```

Wipes volumes, removes images, and rebuilds. Run once before starting.

### Start

```powershell
.\start.ps1          # Local mode  — frontend + local backend
.\start.ps1 -Remote  # Remote mode — frontend + desarrollo.appinwhats.com backend
```

| Service | URL |
|---|---|
| App (via Nginx) | http://localhost:50080 |
| Backend (direct) | http://localhost:53000 *(local mode only)* |

**Hot reload** is enabled for both frontend (`frontend/src/`) and backend (`backend/scr/`). Changes are picked up automatically — no host compilation needed.

### Mode switching

`environment.ts` is permanently set to `localhost:50080` — never edit it. Nginx is the switch:

- **Local mode:** `/api/*` → local backend container
- **Remote mode:** `/api/*` → `https://desarrollo.appinwhats.com` (local backend not started)

In remote mode, the app runs your local frontend code but all API calls go to the remote server. `/files/`, `/integration/`, and `/aiw/` also proxy to the remote server in both modes.

---

## Architecture

```
nginx:50080
  /api/*          → backend:3001
  /files/*        → desarrollo.appinwhats.com/files/
  /integration/*  → desarrollo.appinwhats.com/integration/
  /aiw/*          → desarrollo.appinwhats.com/aiw/
  /*              → frontend:4200
```

### Backend (`backend/scr/`)

Express server with the pattern: **routes → controllers → services → db**

- `db/connection.ts` — mysql2 connection pool
- `keys.ts` — reads credentials from `backend/config.cfg` (not env vars)
- `models/server.ts` — app bootstrap, CORS config, route registration

### Frontend (`frontend/src/app/`)

Angular 16 SPA with lazy-loaded modules:

| Module | Route |
|---|---|
| Dashboard | `/appinwhats/dashboard` |
| Clients | `/appinwhats/clientes` |
| Agents | `/appinwhats/agentes` |
| Conversations | `/appinwhats/conversations` |
| Products | `/appinwhats/products` |
| Sales | `/appinwhats/sales` |
| Configuration | `/appinwhats/configuration` |
| Catalog *(public)* | `/catalogo/:token` |

---

## Production build

```powershell
.\prod-build.ps1
```

Compiles frontend (`ng build`) and backend (`tsc`) inside Docker builder stages, copies the output to `frontend/dist/` and `backend/dist/`, then rebuilds production images. After it finishes, commit both `dist/` folders and push.

---

## Configuration

`backend/config.cfg` holds DB credentials and is mounted as a volume. Changes take effect after:

```powershell
docker compose restart backend
```

No rebuild required.
