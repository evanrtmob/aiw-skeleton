param(
    [switch]$Remote,   # Usa API remota (desarrollo.appinwhats.com) y NO levanta backend local
    [switch]$Build     # Fuerza rebuild de imagenes (por defecto NO reconstruye)
)

# --- Optimizaciones del cliente Docker (menos CPU en build/arranque) ---
$env:DOCKER_BUILDKIT = "1"
$env:COMPOSE_DOCKER_CLI_BUILD = "1"
$env:COMPOSE_BAKE = "true"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  AppInWhats - Start (OPTIMIZADO)" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

if ($Remote) {
    Write-Host "  Mode    " -NoNewline; Write-Host " REMOTE" -ForegroundColor Yellow
    Write-Host "  App     " -NoNewline -ForegroundColor Green;      Write-Host " -> http://localhost:50080"
    Write-Host "  API     " -NoNewline -ForegroundColor Blue;       Write-Host " -> https://desarrollo.appinwhats.com"
    Write-Host "  Nginx   " -NoNewline -ForegroundColor DarkYellow; Write-Host " -> http://localhost:50080"
} else {
    Write-Host "  Mode    " -NoNewline; Write-Host " LOCAL" -ForegroundColor Cyan
    Write-Host "  App     " -NoNewline -ForegroundColor Green;      Write-Host " -> http://localhost:50080"
    Write-Host "  API     " -NoNewline -ForegroundColor Blue;       Write-Host " -> http://localhost:53000"
    Write-Host "  Nginx   " -NoNewline -ForegroundColor DarkYellow; Write-Host " -> http://localhost:50080"
}

Write-Host "  Perf    " -NoNewline -ForegroundColor Magenta; Write-Host " poll lento + limites RAM/CPU + logs acotados"
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Ficheros compose comunes (el 'optimized' va SIEMPRE el ultimo para que gane).
$files = @("-f", "docker-compose.yml", "-f", "docker-compose.override.yml")
if ($Remote) {
    $files += @("-f", "docker-compose.remote.yml")
}
$files += @("-f", "docker-compose.optimized.yml")

# Argumentos de 'up'
$upArgs = @("up", "--no-build")
if ($Build) {
    $upArgs = @("up", "--build")
}
if ($Remote) {
    $upArgs += @("--scale", "backend=0")
}

docker compose @files @upArgs
