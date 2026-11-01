# Starts the MonteCarlo backend (applies DB migrations on startup) and the React UI.
# Usage:  .\start.ps1
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

if (-not (Test-Path "$root\MonteCarlo.UI\node_modules")) {
    Write-Host "Installing UI packages..." -ForegroundColor Cyan
    Push-Location "$root\MonteCarlo.UI"; npm install; Pop-Location
}

Write-Host "Starting backend on http://localhost:5080 (Swagger at /swagger)..." -ForegroundColor Cyan
Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$root\MonteCarlo.Backend'; dotnet run --project src/MonteCarlo.Api --launch-profile http"

Write-Host "Starting UI on http://localhost:5173 ..." -ForegroundColor Cyan
Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$root\MonteCarlo.UI'; npm run dev"

Start-Sleep -Seconds 8
Start-Process "http://localhost:5173"
