# Applies EF Core migrations to the database in ConnectionStrings__Puluj (or the default local one).
param([string]$Connection = $env:ConnectionStrings__Puluj)
$ErrorActionPreference = "Stop"
if ($Connection) { $env:ConnectionStrings__Puluj = $Connection }
dotnet ef database update -p "$PSScriptRoot/../src/Puluj.Infrastructure" -s "$PSScriptRoot/../src/Puluj.Infrastructure"
