param([Parameter(Mandatory)][string]$Name)
$ErrorActionPreference = "Stop"
dotnet ef migrations add $Name -p "$PSScriptRoot/../src/Puluj.Infrastructure" -s "$PSScriptRoot/../src/Puluj.Infrastructure" -o Persistence/Migrations
