
#!/bin/bash

docker compose -f docker-compose.yml -f docker-compose.prod.yml build frontend
docker compose -f docker-compose.yml -f docker-compose.prod.yml build backend

docker compose -f docker-compose.yml -f docker-compose.prod.yml up