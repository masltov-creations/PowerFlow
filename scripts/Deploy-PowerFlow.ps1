[CmdletBinding()]
param(
    [switch]$SkipTests,
    [ValidateRange(1, 10)]
    [int]$KeepReleases = 2
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $repo 'src\PowerFlow.App\PowerFlow.App.csproj'
$appTests = Join-Path $repo 'tests\PowerFlow.App.Tests\PowerFlow.App.Tests.csproj'
$windowsTests = Join-Path $repo 'tests\PowerFlow.Windows.Tests\PowerFlow.Windows.Tests.csproj'
$buildOutput = Join-Path $repo 'src\PowerFlow.App\bin\Release\net8.0-windows10.0.19041.0\win-x64'
$releaseRoot = Join-Path $env:LOCALAPPDATA 'PowerFlow\App\releases'
$currentPointer = Join-Path $env:LOCALAPPDATA 'PowerFlow\App\current.txt'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

function Invoke-Checked([string]$file, [string[]]$arguments) {
    & $file @arguments
    if ($LASTEXITCODE -ne 0) { throw "$file failed with exit code $LASTEXITCODE." }
}

function Get-PowerFlowProcesses {
    @(Get-CimInstance Win32_Process -Filter "Name='PowerFlow.App.exe'" -ErrorAction SilentlyContinue)
}

function Wait-PowerFlowStopped([TimeSpan]$timeout) {
    $deadline = [DateTimeOffset]::Now + $timeout
    do {
        if ((Get-PowerFlowProcesses).Count -eq 0) { return }
        Start-Sleep -Milliseconds 250
    } while ([DateTimeOffset]::Now -lt $deadline)
    throw 'PowerFlow did not exit after the graceful shutdown request. Deployment was stopped without force-killing it.'
}

Push-Location $repo
try {
    if (-not $SkipTests) {
        Invoke-Checked 'dotnet' @('test', $appTests, '-c', 'Release', '--no-restore')
        Invoke-Checked 'dotnet' @('test', $windowsTests, '-c', 'Release', '--no-restore')
    }
    Invoke-Checked 'dotnet' @('build', $appProject, '-c', 'Release', '--no-restore')

    $sourceExe = Join-Path $buildOutput 'PowerFlow.App.exe'
    if (-not (Test-Path $sourceExe)) { throw "Release build did not produce $sourceExe" }

    $commit = (& git rev-parse --short=12 HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($commit)) { throw 'Unable to resolve the PowerFlow Git commit.' }
    $dirty = -not [string]::IsNullOrWhiteSpace((& git status --porcelain))
    $releaseId = '{0}-{1}{2}' -f (Get-Date -Format 'yyyyMMdd-HHmmss'), $commit, $(if ($dirty) { '-dirty' } else { '' })
    $target = Join-Path $releaseRoot $releaseId
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    Copy-Item (Join-Path $buildOutput '*') $target -Recurse -Force
    $targetExe = Join-Path $target 'PowerFlow.App.exe'
    if (-not (Test-Path $targetExe)) { throw 'Versioned release copy is incomplete.' }

    $running = Get-PowerFlowProcesses
    if ($running.Count -gt 0) {
        $paths = @($running | ForEach-Object ExecutablePath | Where-Object { $_ } | Select-Object -Unique)
        if ($paths.Count -ne 1) { throw "Refusing deployment with ambiguous PowerFlow runtimes: $($paths -join ', ')" }
        $shutdown = Start-Process -FilePath $paths[0] -ArgumentList '--shutdown' -PassThru
        if (-not $shutdown.WaitForExit(5000)) { throw 'PowerFlow shutdown relay process did not exit promptly.' }
        Wait-PowerFlowStopped ([TimeSpan]::FromSeconds(15))
        Start-Sleep -Milliseconds 500
    }

    $started = Start-Process -FilePath $targetExe -ArgumentList '--background' -PassThru
    Start-Sleep -Seconds 8
    $live = Get-PowerFlowProcesses
    if ($live.Count -ne 1) { throw "Expected one PowerFlow runtime after deployment; found $($live.Count)." }
    if (-not [string]::Equals($live[0].ExecutablePath, $targetExe, [StringComparison]::OrdinalIgnoreCase)) {
        throw "PowerFlow started from an unexpected path: $($live[0].ExecutablePath)"
    }

    $expectedRunValue = '"{0}" --background' -f $targetExe
    $actualRunValue = (Get-ItemProperty $runKey -ErrorAction Stop).PowerFlow
    if (-not [string]::Equals($actualRunValue, $expectedRunValue, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Startup registration mismatch. Expected '$expectedRunValue'; found '$actualRunValue'."
    }

    New-Item -ItemType Directory -Path (Split-Path -Parent $currentPointer) -Force | Out-Null
    [IO.File]::WriteAllText($currentPointer, $targetExe + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))

    $keep = @(Get-ChildItem $releaseRoot -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending | Select-Object -First $KeepReleases | ForEach-Object FullName)
    Get-ChildItem $releaseRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $keep -notcontains $_.FullName -and -not [string]::Equals($_.FullName, $target, [StringComparison]::OrdinalIgnoreCase) } |
        Remove-Item -Recurse -Force

    [pscustomobject]@{
        ReleaseId = $releaseId
        ProcessId = $live[0].ProcessId
        ExecutablePath = $targetExe
        StartupVerified = $true
        TestsSkipped = [bool]$SkipTests
    } | Format-List
}
finally {
    Pop-Location
}