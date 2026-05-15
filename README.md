# AppInWhats — Windows Docker Skeleton

> **This is the Windows branch** of the [AIW Skeleton](https://github.com/romabilibov-mobentis/aiw-skeleton).
> It replaces all Bash scripts with PowerShell and adds Windows-specific Docker configuration.

AppInWhats is a B2B sales management platform built around WhatsApp integration. It manages agents, clients, orders, products, conversations, and a public web catalog with an AI chat module.

**Stack:** Angular 16 · Express/TypeScript · MySQL · Nginx · Docker

---

## What's different from the base branch

| Area | Base (Linux/Mac) | This branch (Windows) |
|---|---|---|
| Build script | `build.sh` | `build.ps1` |
| Start script | `start.sh` | `start.ps1` |
| Production build | `prod-build.sh` | `prod-build.ps1` |
| Hot reload | inotify (native) | `CHOKIDAR_USEPOLLING=true` (required on Windows) |
| `node_modules` | bind mount | Named Docker volume (avoids Windows path issues) |
| Remote mode config | — | `docker-compose.remote.yml` + `nginx/conf.d/default.dev.remote.conf` |

---

## Prerequisites

- [Docker Desktop for Windows](https://www.docker.com/products/docker-desktop/) with WSL 2 backend enabled
- PowerShell 7+ (`winget install Microsoft.PowerShell`)

---

## Getting started

### 1. Clone and configure

```powershell
git clone https://github.com/miguel-gayol-pertierra-mobentis/aiw-skeleton.git -b windows
cd aiw-skeleton
```

Copy `.env.example` to `.env` and fill in the values, then add your DB credentials to `backend/config.cfg`.

### 2. First-time build

```powershell
.\build.ps1
```

Runs `docker compose down -v --rmi all` to stop containers, wipe named volumes, and remove all images, then `docker compose build` to rebuild everything from scratch.

**Run this once before the first start, and again after any `package.json` or Dockerfile change.** Named volumes (like `frontend_node_modules`) are only populated from the image on first creation — a plain restart won't refresh them if the image changed.

### 3. Start

```powershell
.\start.ps1          # Local mode  — local frontend + local backend
.\start.ps1 -Remote  # Remote mode — local frontend + remote backend
```

- **Local:** runs `docker compose up` using `docker-compose.yml` + `docker-compose.override.yml`. Both frontend and backend containers start.
- **Remote:** runs `docker compose -f docker-compose.yml -f docker-compose.override.yml -f docker-compose.remote.yml up --scale backend=0`. The `docker-compose.remote.yml` override swaps the Nginx config volume to point at `default.dev.remote.conf`, and `--scale backend=0` skips starting the local backend container entirely.

| Service | URL | Notes |
|---|---|---|
| App (via Nginx) | `http://localhost:50080` | Always use this — it's the main entry point |
| Backend (direct) | `http://localhost:53000` | Local mode only, useful for Postman/debug |

**Hot reload** works for both frontend (`frontend/src/`) and backend (`backend/scr/`). The `CHOKIDAR_USEPOLLING=true` environment variable set in `docker-compose.override.yml` is what makes this work on Windows — without it, the Docker container can't detect file changes from the Windows host filesystem. Save a file and the container reloads automatically — no host-side compilation needed.

---

## Mode switching (local vs remote)

`environment.ts` is permanently set to `apiUrl: 'http://localhost:50080'` — **never edit it**. Nginx is the only switch between modes.

**Local mode** (`start.ps1`)
Nginx routes `/api/*` to the local backend container. Uses `nginx/conf.d/default.dev.conf`.

**Remote mode** (`start.ps1 -Remote`)
Nginx routes `/api/*` to `https://desarrollo.appinwhats.com`. The local backend container is not started (`--scale backend=0`). Uses `nginx/conf.d/default.dev.remote.conf` (swapped in via `docker-compose.remote.yml`).

In remote mode you run your local frontend code with hot reload, but all API calls go to the remote server.

---

## Architecture

```
nginx:50080
  /api/*          → backend:3001          (local mode)
  /api/*          → desarrollo.appinwhats.com  (remote mode)
  /files/*        → desarrollo.appinwhats.com/files/
  /integration/*  → desarrollo.appinwhats.com/integration/
  /aiw/*          → desarrollo.appinwhats.com/aiw/
  /*              → frontend:4200
```

### Docker Compose file layout

| File | Purpose |
|---|---|
| `docker-compose.yml` | Base service definitions |
| `docker-compose.override.yml` | Dev overrides: bind mounts, ports, `CHOKIDAR_USEPOLLING` |
| `docker-compose.remote.yml` | Swaps Nginx config for remote mode |
| `docker-compose.prod.yml` | Production image overrides |

### Backend (`backend/scr/`)

Express server — pattern: **routes → controllers → services → db**

- `db/connection.ts` — mysql2 connection pool
- `keys.ts` — reads DB credentials from `backend/config.cfg` (not from env vars)
- `models/server.ts` — bootstrap, CORS config, route registration

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
| Catalog *(public, token-based)* | `/catalogo/:token` |

---

## Configuration

`backend/config.cfg` is mounted as a volume. DB credential changes take effect after:

```powershell
docker compose restart backend
```

No rebuild required.

---

## Production build

```powershell
.\prod-build.ps1
```

No local Node or TypeScript installation needed — all compilation happens inside Docker builder stages. The script runs four steps:

1. **Frontend compile** — builds a temporary Docker image from `containers/Dockerfile.frontend` targeting the `builder` stage, creates a short-lived container from it, copies `/app/dist` out to `frontend/dist/` on the host, then removes the temporary container and image.
2. **Backend compile** — same process with `containers/Dockerfile.backend`, copying `/app/dist` to `backend/dist/`.
3. **Wipe** — runs `docker compose down -v --rmi local` to clear old containers, volumes, and locally-built images so the next step starts clean.
4. **Rebuild production images** — runs `docker compose -f docker-compose.yml -f docker-compose.prod.yml build` for frontend and backend, then starts everything with `up`.

After it finishes, commit both `dist/` folders and push to deploy.
