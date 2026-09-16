<#
.SYNOPSIS
    Local Tauri build with .env loaded.

.DESCRIPTION
    Loads .env from repo root so TAURI_SIGNING_PRIVATE_KEY (+ password) are
    available for the updater artifact signing step, then runs `tauri build`.
    Extra args are forwarded to `tauri build`.

.EXAMPLE
    .\scripts\build-local.ps1
    .\scripts\build-local.ps1 --debug
    .\scripts\build-local.ps1 --no-bundle
#>

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

$envFile = Join-Path $Root ".env"
if (Test-Path $envFile) {
    Get-Content $envFile | ForEach-Object {
        if ($_ -match '^\s*([^#][^=]+?)\s*=\s*(.+)\s*$') {
            [Environment]::SetEnvironmentVariable($Matches[1], $Matches[2], "Process")
        }
    }
    Write-Host "Loaded .env" -ForegroundColor Green
} else {
    Write-Host "No .env file found - using existing environment variables." -ForegroundColor Yellow
}

Set-Location $Root
& npx tauri build @args
exit $LASTEXITCODE
