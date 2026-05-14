#!/bin/bash

docker compose down

docker volume rm 4aiw-configurador_backend_node_modules 4aiw-configurador_frontend_node_modules

docker rmi 4aiw-configurador-backend:latest 4aiw-configurador-frontend:latest
