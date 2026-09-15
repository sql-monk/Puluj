<#
.SYNOPSIS  Runs one command under a machine-wide mutex, so parallel agents (or terminals) do not build the same
           working tree at once: dotnet build/test and the Vite build share obj/, bin/, tsbuildinfo and wwwroot.
           Usage: pwsh -File scripts/with-lock.ps1 dotnet build src/Puluj.Admin/Puluj.Admin.csproj
                  pwsh -File scripts/with-lock.ps1 npm run build
           Waits up to 20 minutes for the lock; exits with the command's exit code.
#>
param([Parameter(Mandatory = $true, ValueFromRemainingArguments = $true)][string[]]$Command)
$ErrorActionPreference = "Stop"
$mutex = New-Object System.Threading.Mutex($false, "Global\Puluj.Build")
$acquired = $false
try {
    try { $acquired = $mutex.WaitOne([TimeSpan]::FromMinutes(20)) }
    catch [System.Threading.AbandonedMutexException] { $acquired = $true } # the previous holder died: the lock is ours
    if (-not $acquired) { Write-Error "with-lock: could not acquire Global\Puluj.Build within 20 minutes"; exit 75 }
    $exe = $Command[0]
    # npm / npx / dotnet-tools ship an extensionless POSIX script next to the .cmd; PowerShell may pick the former and
    # spawn an empty cmd.exe that exits 0 without running anything. Prefer the .cmd shim on Windows.
    if ($IsWindows -or $env:OS -eq "Windows_NT") {
        foreach ($ext in @(".cmd", ".exe", ".bat")) {
            $shim = Get-Command "$exe$ext" -ErrorAction SilentlyContinue
            if ($shim) { $exe = $shim.Source; break }
        }
    }
    $rest = if ($Command.Length -gt 1) { $Command[1..($Command.Length - 1)] } else { @() }
    & $exe @rest
    if ($null -eq $LASTEXITCODE) { exit 0 }
    exit $LASTEXITCODE
} finally {
    if ($acquired) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
