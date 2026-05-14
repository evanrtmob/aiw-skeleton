# AIW Skeleton

## Requisitos

- Docker + Docker Compose

## Primeros pasos

1. Clonar el repositorio
2. Agregar el código de la aplicación:
   - Proyecto Angular → `frontend/`
   - Proyecto Express → `backend/scr/`
3. Actualizar el directorio de salida de Angular en `containers/Dockerfile.frontend` (`dist/AiW`)
4. Copiar `.env.example` a `.env` y completar los valores (requerido en producción)
5. Iniciar con ```docker compose up```, Bash o Powershell:
```bash
./init.sh
```

App disponible en `http://localhost:{{FRONTEND_PORT}}`.

## Estructura del proyecto

```
├── backend/
│   └── scr/              # Express Backend
├── frontend/             # Frontend
├── nginx/conf.d/
│   ├── default.dev.conf  # Config proxy dev
│   └── default.prod.conf # Config proxy prod
├── containers/
│   ├── Dockerfile.backend
│   └── Dockerfile.frontend
├── docker-compose.yml            # Config base (target producción)
├── docker-compose.override.yml   # Overrides dev (se aplica automáticamente - se usa por defecto para desarrollo)
├── docker-compose.prod.yml       # Overrides de puertos y nginx para prod
├── init.sh    # docker compose up
└── down.sh    # Detiene y elimina contenedores, imágenes y .data/
```

## Flujo de desarrollo

```bash
./init.sh      # Inicia todos los contenedores con hot reload
./down.sh      # Detiene, elimina contenedores/imágenes, limpia .data/
./init_prod.sh # Hace el build de los proyectos(back y front) e inicia los contenedores
```

- **Backend** recarga automáticamente vía `ts-node` observando `backend/scr/` (montado como volumen)
- **Frontend** recarga automáticamente vía `ng serve --poll 500`
- Backend accesible directamente en `http://localhost:{{FRONTEND_PORT}}` (sin pasar por nginx)


Desplegar con:
```bash
./init_prod.sh
```

O manualmente:
```bash
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d
```

## Enrutamiento nginx

| Ruta | Destino |
|------|---------|
| `/api/*` | Backend (puerto 3001 dev / 3000 prod) |
| `/*` | Frontend |

CORS es manejado por nginx — no configurar en Express.
