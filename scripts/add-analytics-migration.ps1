# Adds a migration to the analytics schema (Puluj.Analytics: its own DbContext and migrations history).
param([Parameter(Mandatory)][string]$Name)
$ErrorActionPreference = "Stop"
dotnet ef migrations add $Name -p "$PSScriptRoot/../src/Puluj.Analytics" -s "$PSScriptRoot/../src/Puluj.Analytics" -o Persistence/Migrations
