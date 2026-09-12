<#
.SYNOPSIS  Rebuilds and (re)starts Puluj.Api and Puluj.Worker in the background for local development.
           Logs: $env:TEMP\puluj-api.log, $env:TEMP\puluj-worker.log. Use -Stop to only stop them.
           -Public builds the SPA into wwwroot and binds the Api to all interfaces (LAN access, no Vite needed);
           the firewall rule for port 5257 is added once and needs an elevated shell.
#>
param([switch]$Stop, [switch]$ResetDb, [switch]$Public)
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
$apiUrl = "http://localhost:5257"
if ($Public) {
    Push-Location "$root\web"; try { npm run build | Select-String -Pattern "error|built in" } finally { Pop-Location }
    $env:ASPNETCORE_URLS = "http://0.0.0.0:5257"
    if (-not (Get-NetFirewallRule -DisplayName "Puluj API" -ErrorAction SilentlyContinue)) {
        try { New-NetFirewallRule -DisplayName "Puluj API" -Direction Inbound -Protocol TCP -LocalPort 5257 -Action Allow | Out-Null }
        catch { Write-Warning "Firewall rule not added (run once as admin): New-NetFirewallRule -DisplayName 'Puluj API' -Direction Inbound -Protocol TCP -LocalPort 5257 -Action Allow" }
    }
    # Interface behind the default route = the one other machines on the LAN reach us through (skips WSL/Hyper-V vNICs).
    $ifIndex = (Get-NetRoute -DestinationPrefix "0.0.0.0/0" -ErrorAction SilentlyContinue | Sort-Object RouteMetric, InterfaceMetric | Select-Object -First 1).InterfaceIndex
    $lanIp = if ($ifIndex) { (Get-NetIPAddress -AddressFamily IPv4 -InterfaceIndex $ifIndex | Select-Object -First 1).IPAddress }
    if ($lanIp) { $apiUrl = "http://${lanIp}:5257" }
    # launchSettings.json would pin the Api back to localhost, so the launch profile is skipped when binding publicly.
    $apiArgs = "run --no-build --no-launch-profile --project `"$root\src\Puluj.Api`""
} else {
    Remove-Item Env:ASPNETCORE_URLS -ErrorAction SilentlyContinue
    $apiArgs = "run --no-build --project `"$root\src\Puluj.Api`""
}
Start-Process dotnet -ArgumentList "run --no-build --project `"$root\src\Puluj.Worker`"" -RedirectStandardOutput "$env:TEMP\puluj-worker.log" -RedirectStandardError "$env:TEMP\puluj-worker.err" -WindowStyle Hidden
Start-Sleep 2
Start-Process dotnet -ArgumentList $apiArgs -RedirectStandardOutput "$env:TEMP\puluj-api.log" -RedirectStandardError "$env:TEMP\puluj-api.err" -WindowStyle Hidden
Write-Host "Started. API: $apiUrl  Frontend dev: cd web; npm run dev"
