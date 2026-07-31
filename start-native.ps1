param(
    [switch]$Remote,   # API remota (desarrollo.appinwhats.com); no arranca backend local
    [switch]$Install   # Ejecuta npm install en frontend/backend si hace falta
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$FrontendDir = Join-Path $Root "frontend"
$BackendDir = Join-Path $Root "backend"
$Port = 50080
$script:ChildProcs = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()

function Assert-Command([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "No se encontro '$Name' en el PATH. Instala Node.js LTS y vuelve a intentarlo."
    }
}

function Ensure-NodeModules([string]$Dir, [string]$Label) {
    $modules = Join-Path $Dir "node_modules"
    if ((Test-Path $modules) -and -not $Install) {
        return
    }
    Write-Host "  npm install ($Label)..." -ForegroundColor DarkCyan
    Push-Location $Dir
    try {
        npm install --legacy-peer-deps
        if ($LASTEXITCODE -ne 0) {
            throw "npm install fallo en $Label"
        }
    } finally {
        Pop-Location
    }
}

function Start-NativeProcess {
    param(
        [string]$WorkDir,
        [string]$FilePath,
        [string[]]$Arguments,
        [hashtable]$EnvVars = @{}
    )

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $FilePath
    $psi.Arguments = ($Arguments | ForEach-Object {
        if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ }
    }) -join ' '
    $psi.WorkingDirectory = $WorkDir
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $false
    $psi.RedirectStandardError = $false
    $psi.CreateNoWindow = $false

    foreach ($key in [System.Environment]::GetEnvironmentVariables().Keys) {
        try { $psi.Environment[$key] = [System.Environment]::GetEnvironmentVariable($key) } catch {}
    }
    foreach ($key in $EnvVars.Keys) {
        $psi.Environment[$key] = [string]$EnvVars[$key]
    }

    $proc = New-Object System.Diagnostics.Process
    $proc.StartInfo = $psi
    [void]$proc.Start()
    $script:ChildProcs.Add($proc)
    return $proc
}

function Stop-NativeProcesses {
    foreach ($proc in @($script:ChildProcs)) {
        if ($null -eq $proc) { continue }
        try {
            if (-not $proc.HasExited) {
                # Mata el arbol (npx/node hijos incluidos)
                Start-Process -FilePath "taskkill.exe" -ArgumentList @("/PID", "$($proc.Id)", "/T", "/F") -WindowStyle Hidden -Wait -ErrorAction SilentlyContinue | Out-Null
            }
        } catch {}
    }
    $script:ChildProcs.Clear()
}

Assert-Command "node"
Assert-Command "npm"
Assert-Command "npx"

$Host.UI.RawUI.WindowTitle = "AppInWhats Native"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  AppInWhats - Start NATIVE" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

if ($Remote) {
    Write-Host "  Mode    " -NoNewline; Write-Host " REMOTE (sin backend local)" -ForegroundColor Yellow
    Write-Host "  App     " -NoNewline -ForegroundColor Green; Write-Host " -> http://localhost:$Port"
    Write-Host "  API     " -NoNewline -ForegroundColor Blue; Write-Host " -> https://desarrollo.appinwhats.com"
    Write-Host "  Files   " -NoNewline -ForegroundColor DarkYellow; Write-Host " -> https://desarrollo.appinwhats.com/files/"
} else {
    Write-Host "  Mode    " -NoNewline; Write-Host " LOCAL (frontend + backend nativos)" -ForegroundColor Cyan
    Write-Host "  App     " -NoNewline -ForegroundColor Green; Write-Host " -> http://localhost:$Port"
    Write-Host "  API     " -NoNewline -ForegroundColor Blue; Write-Host " -> http://127.0.0.1:3001 (via proxy)"
    Write-Host "  Files   " -NoNewline -ForegroundColor DarkYellow; Write-Host " -> https://desarrollo.appinwhats.com/files/"
}

Write-Host "  Tip     " -NoNewline -ForegroundColor Magenta; Write-Host " Misma terminal. Ctrl+C para parar todo."
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

Ensure-NodeModules $FrontendDir "frontend"

$npxCmd = (Get-Command npx.cmd -ErrorAction SilentlyContinue)
if (-not $npxCmd) { $npxCmd = Get-Command npx }
$npxPath = $npxCmd.Source

try {
    if (-not $Remote) {
        Ensure-NodeModules $BackendDir "backend"

        $cfgPath = Join-Path $BackendDir "config.cfg"
        if (-not (Test-Path $cfgPath)) {
            throw "Falta backend/config.cfg"
        }
        $cfgHost = (Select-String -Path $cfgPath -Pattern '^\s*host\s*=\s*(.+)$' | Select-Object -First 1).Matches.Groups[1].Value.Trim()
        if ($cfgHost -eq "172.17.0.1") {
            Write-Host "AVISO: config.cfg usa host=172.17.0.1 (gateway Docker)." -ForegroundColor Yellow
            Write-Host "En modo nativo suele hacer falta localhost o el host MySQL real (p.ej. desarrollo.appinwhats.com)." -ForegroundColor Yellow
            Write-Host ""
        }

        $envFile = Join-Path $BackendDir "scr\.env"
        if (-not (Test-Path $envFile)) {
            Write-Host "AVISO: no existe backend/scr/.env (SECRET_KEY / JWT). El login puede fallar." -ForegroundColor Yellow
            Write-Host ""
        }

        Write-Host "Arrancando backend en :3001 ..." -ForegroundColor Green
        [void](Start-NativeProcess -WorkDir $BackendDir -FilePath $npxPath -Arguments @(
            "--yes", "nodemon",
            "--watch", "scr",
            "--ext", "ts",
            "--exec", "npx --yes ts-node scr/index.ts"
        ) -EnvVars @{
            AIW_CONFIG_CFG = $cfgPath
            PORT = "3001"
            NODE_OPTIONS = "--max-old-space-size=768"
        })
        Start-Sleep -Seconds 2
    }

    $proxyFile = if ($Remote) {
        Join-Path $FrontendDir "proxy.conf.native.remote.json"
    } else {
        Join-Path $FrontendDir "proxy.conf.native.json"
    }

    Write-Host "Arrancando frontend en :$Port ..." -ForegroundColor Green
    Write-Host "Proxy: $proxyFile" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "Abre http://localhost:$Port" -ForegroundColor Green
    Write-Host "Ctrl+C para detener frontend y backend." -ForegroundColor DarkGray
    Write-Host ""

    $frontProc = Start-NativeProcess -WorkDir $FrontendDir -FilePath $npxPath -Arguments @(
        "--yes", "ng", "serve",
        "--host", "0.0.0.0",
        "--port", "$Port",
        "--proxy-config", $proxyFile
    ) -EnvVars @{
        NODE_OPTIONS = "--max-old-space-size=4096"
        NG_CLI_ANALYTICS = "false"
    }

    while (-not $frontProc.HasExited) {
        Start-Sleep -Seconds 1
        if (-not $Remote) {
            $deadBackend = $script:ChildProcs | Where-Object { $_.Id -ne $frontProc.Id -and $_.HasExited }
            if ($deadBackend) {
                Write-Host "`nEl backend se ha detenido. Cerrando..." -ForegroundColor Yellow
                break
            }
        }
    }
}
finally {
    Write-Host "`nDeteniendo procesos nativos..." -ForegroundColor Yellow
    Stop-NativeProcesses
    Write-Host "Listo." -ForegroundColor Green
}
