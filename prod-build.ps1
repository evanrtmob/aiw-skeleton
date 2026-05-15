if (-not (Test-Path "nginx.conf")) {
    Write-Host "ERROR: nginx.conf not found at repo root. Aborting." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  AppInWhats - Production Build" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  1. ng build   -> frontend/dist/" -ForegroundColor Gray
Write-Host "  2. tsc        -> backend/dist/" -ForegroundColor Gray
Write-Host "  3. Wipe volumes + images" -ForegroundColor Gray
Write-Host "  4. Rebuild production Docker images" -ForegroundColor Gray
Write-Host "  Then commit both dist folders and push." -ForegroundColor Gray
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# --- Step 1: ng build ---
Write-Host "Step 1/4 - Compiling frontend (ng build)..." -ForegroundColor Yellow
Write-Host ""

docker build --target builder -f ./containers/Dockerfile.frontend -t aiw-frontend-builder-tmp .

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "Frontend build failed. Aborting." -ForegroundColor Red
    exit 1
}

$containerId = docker create aiw-frontend-builder-tmp
docker cp "${containerId}:/app/dist" ./frontend/
docker rm $containerId | Out-Null
docker rmi aiw-frontend-builder-tmp | Out-Null

Write-Host ""
Write-Host "frontend/dist/ generated." -ForegroundColor Green

# --- Step 2: tsc ---
Write-Host ""
Write-Host "Step 2/4 - Compiling backend (tsc)..." -ForegroundColor Yellow
Write-Host ""

docker build --target builder -f ./containers/Dockerfile.backend -t aiw-backend-builder-tmp .

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "Backend build failed. Aborting." -ForegroundColor Red
    exit 1
}

$containerId = docker create aiw-backend-builder-tmp
docker cp "${containerId}:/app/dist" ./backend/
docker rm $containerId | Out-Null
docker rmi aiw-backend-builder-tmp | Out-Null

Write-Host ""
Write-Host "backend/dist/ generated." -ForegroundColor Green

# --- Step 3: Wipe ---
Write-Host ""
Write-Host "Step 3/4 - Stopping containers and wiping volumes..." -ForegroundColor Yellow

docker compose -f docker-compose.yml -f docker-compose.prod.yml down -v --rmi local

# --- Step 4: Rebuild production images ---
Write-Host ""
Write-Host "Step 4/4 - Building production images..." -ForegroundColor Yellow
Write-Host ""

docker compose -f docker-compose.yml -f docker-compose.prod.yml build frontend
if ($LASTEXITCODE -ne 0) { Write-Host "Frontend image build failed." -ForegroundColor Red; exit 1 }

docker compose -f docker-compose.yml -f docker-compose.prod.yml build backend
if ($LASTEXITCODE -ne 0) { Write-Host "Backend image build failed." -ForegroundColor Red; exit 1 }

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Done. Commit frontend/dist + backend/dist and push." -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""

docker compose -f docker-compose.yml -f docker-compose.prod.yml up
