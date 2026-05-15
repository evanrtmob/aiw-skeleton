param([switch]$Remote)

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  AppInWhats - Start" -ForegroundColor Cyan
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

Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

if ($Remote) {
    docker compose -f docker-compose.yml -f docker-compose.override.yml -f docker-compose.remote.yml up --scale backend=0
} else {
    docker compose up
}
