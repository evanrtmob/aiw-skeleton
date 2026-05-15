Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  AppInWhats - Build" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Wipes volumes, removes images, and rebuilds." -ForegroundColor Gray
Write-Host "  Run .\start.ps1 or .\start.ps1 -Remote afterwards." -ForegroundColor Gray
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Stopping containers and wiping volumes..." -ForegroundColor Yellow

docker compose down -v --rmi all

Write-Host ""
Write-Host "Building images..." -ForegroundColor Yellow
Write-Host ""

docker compose build
