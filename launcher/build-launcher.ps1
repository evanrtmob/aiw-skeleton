param(
    [switch]$Open
)

$ErrorActionPreference = "Stop"
$Here = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $Here
$Src = Join-Path $Here "AppInWhatsLauncher.cs"
$Out = Join-Path $Root "AppInWhats-DevBuild.exe"
$Csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path $Csc)) {
    throw "No se encontro csc.exe (.NET Framework 4). No hace falta Visual Studio; suele venir con Windows."
}

$Icon = Join-Path $Here "appinwhats.ico"
if (-not (Test-Path $Icon)) {
    Write-Host "Generando icono..." -ForegroundColor DarkCyan
    python (Join-Path $Here "make_icon.py")
    if (-not (Test-Path $Icon)) { throw "Falta appinwhats.ico" }
}

Write-Host "Compilando AppInWhats-DevBuild.exe ..." -ForegroundColor Cyan
& $Csc /nologo /target:winexe /optimize+ `
    /win32icon:"$Icon" `
    /out:"$Out" `
    /reference:System.Windows.Forms.dll `
    /reference:System.Drawing.dll `
    /reference:System.dll `
    /reference:System.Core.dll `
    "$Src"

if ($LASTEXITCODE -ne 0) {
    throw "Fallo la compilacion"
}

Write-Host "OK -> $Out" -ForegroundColor Green

if ($Open) {
    Start-Process $Out
}
