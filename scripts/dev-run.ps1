<#
.SYNOPSIS  Rebuilds and (re)starts Puluj.Api and Puluj.Worker in the background for local development.
           Logs: $env:TEMP\puluj-api.log, $env:TEMP\puluj-worker.log. Use -Stop to only stop them.
#>
param([switch]$Stop, [switch]$ResetDb)
$ErrorActionPreference = "Stop"
$root = Resolve-Path "$PSScriptRoot\.."
Get-Process -Name "Puluj.Api", "Puluj.Worker" -ErrorAction SilentlyContinue | Stop-Process -Force
if ($Stop) { return }
if ($ResetDb) {
    $env:PGPASSWORD = "puluj"
    psql -h localhost -U puluj -d postgres -q -c "DROP DATABASE IF EXISTS puluj;" -c "CREATE DATABASE puluj;"
    psql -h localhost -U puluj -d puluj -q -c "CREATE EXTENSION IF NOT EXISTS postgis;"
}
dotnet build "$root\Puluj.sln" | Select-String -Pattern " error |Build succeeded" | Select-Object -Unique
$env:DOTNET_ENVIRONMENT = "Development"; $env:ASPNETCORE_ENVIRONMENT = "Development"
Start-Process dotnet -ArgumentList "run --no-build --project `"$root\src\Puluj.Worker`"" -RedirectStandardOutput "$env:TEMP\puluj-worker.log" -RedirectStandardError "$env:TEMP\puluj-worker.err" -WindowStyle Hidden
Start-Sleep 2
Start-Process dotnet -ArgumentList "run --no-build --project `"$root\src\Puluj.Api`"" -RedirectStandardOutput "$env:TEMP\puluj-api.log" -RedirectStandardError "$env:TEMP\puluj-api.err" -WindowStyle Hidden
Write-Host "Started. API: http://localhost:5257  Frontend dev: cd web; npm run dev"
