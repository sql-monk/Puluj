<#
.SYNOPSIS  Rebuilds and (re)starts Puluj.Worker, Puluj.Api (map, :5257) and Puluj.Admin (admin panel, :5258) in the
           background for local development. Console output: $env:TEMP\puluj-{worker,api,admin}.log; the services also
           write rolling files into <repo>\logs\ (the admin panel reads them). Use -Stop to only stop them.
           -Public builds both SPAs into the wwwroot folders and binds Api and Admin to all interfaces (LAN access,
           no Vite needed); the firewall rules for 5257/5258 are added once and need an elevated shell.
#>
param([switch]$Stop, [switch]$ResetDb, [switch]$Public)
$ErrorActionPreference = "Stop"
$root = Resolve-Path "$PSScriptRoot\.."
Get-Process -Name "Puluj.Api", "Puluj.Worker", "Puluj.Admin" -ErrorAction SilentlyContinue | Stop-Process -Force
if ($Stop) { return }
if ($ResetDb) {
    $env:PGPASSWORD = "puluj"
    psql -h localhost -U puluj -d postgres -q -c "DROP DATABASE IF EXISTS puluj;" -c "CREATE DATABASE puluj;"
    psql -h localhost -U puluj -d puluj -q -c "CREATE EXTENSION IF NOT EXISTS postgis;"
}
dotnet build "$root\Puluj.sln" | Select-String -Pattern " error |Build succeeded" | Select-Object -Unique
New-Item -ItemType Directory -Force "$root\logs" | Out-Null
$env:DOTNET_ENVIRONMENT = "Development"; $env:ASPNETCORE_ENVIRONMENT = "Development"
$apiUrl = "http://localhost:5257"
$adminUrl = "http://localhost:5258"

function Add-FirewallRuleOnce([string]$name, [int]$port) {
    if (-not (Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue)) {
        try { New-NetFirewallRule -DisplayName $name -Direction Inbound -Protocol TCP -LocalPort $port -Action Allow | Out-Null }
        catch { Write-Warning "Firewall rule not added (run once as admin): New-NetFirewallRule -DisplayName '$name' -Direction Inbound -Protocol TCP -LocalPort $port -Action Allow" }
    }
}

if ($Public) {
    Push-Location "$root\web"; try { npm run build | Select-String -Pattern "error|built in" } finally { Pop-Location }
    Add-FirewallRuleOnce "Puluj API" 5257
    Add-FirewallRuleOnce "Puluj Admin" 5258
    # Interface behind the default route = the one other machines on the LAN reach us through (skips WSL/Hyper-V vNICs).
    $ifIndex = (Get-NetRoute -DestinationPrefix "0.0.0.0/0" -ErrorAction SilentlyContinue | Sort-Object RouteMetric, InterfaceMetric | Select-Object -First 1).InterfaceIndex
    $lanIp = if ($ifIndex) { (Get-NetIPAddress -AddressFamily IPv4 -InterfaceIndex $ifIndex | Select-Object -First 1).IPAddress }
    if ($lanIp) { $apiUrl = "http://${lanIp}:5257"; $adminUrl = "http://${lanIp}:5258" }
    # launchSettings.json would pin the services back to localhost, so the launch profiles are skipped when binding publicly.
    $apiArgs = "run --no-build --no-launch-profile --project `"$root\src\Puluj.Api`""
    $adminArgs = "run --no-build --no-launch-profile --project `"$root\src\Puluj.Admin`""
    $apiBind = "http://0.0.0.0:5257"; $adminBind = "http://0.0.0.0:5258"
} else {
    $apiArgs = "run --no-build --project `"$root\src\Puluj.Api`""
    $adminArgs = "run --no-build --project `"$root\src\Puluj.Admin`""
    $apiBind = $null; $adminBind = $null
}
Remove-Item Env:ASPNETCORE_URLS -ErrorAction SilentlyContinue
# The Worker applies migrations (including the puluj_reader / puluj_admin roles the other two services log in with).
Start-Process dotnet -ArgumentList "run --no-build --project `"$root\src\Puluj.Worker`"" -RedirectStandardOutput "$env:TEMP\puluj-worker.log" -RedirectStandardError "$env:TEMP\puluj-worker.err" -WindowStyle Hidden
Start-Sleep 3
if ($apiBind) { $env:ASPNETCORE_URLS = $apiBind }
Start-Process dotnet -ArgumentList $apiArgs -RedirectStandardOutput "$env:TEMP\puluj-api.log" -RedirectStandardError "$env:TEMP\puluj-api.err" -WindowStyle Hidden
if ($adminBind) { $env:ASPNETCORE_URLS = $adminBind } else { Remove-Item Env:ASPNETCORE_URLS -ErrorAction SilentlyContinue }
Start-Process dotnet -ArgumentList $adminArgs -RedirectStandardOutput "$env:TEMP\puluj-admin.log" -RedirectStandardError "$env:TEMP\puluj-admin.err" -WindowStyle Hidden
Remove-Item Env:ASPNETCORE_URLS -ErrorAction SilentlyContinue
Write-Host "Started. Map: $apiUrl  Admin: $adminUrl  Frontend dev: cd web; npm run dev (map) / npm run dev:admin (admin)"
