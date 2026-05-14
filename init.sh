
#!/bin/bash

docker compose build --no-cache frontend
docker compose build --no-cache backend

docker compose up