[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PayloadPath,
    [switch]$NoDesktopShortcut,
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'
$installRoot = Join-Path $env:LOCALAPPDATA 'Programs\PowerFlow'
$userDataRoot = Join-Path $env:LOCALAPPDATA 'PowerFlow'
$legacyReleaseRoot = Join-Path $userDataRoot 'App\releases'
$startMenuShortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\PowerFlow.lnk'
$desktopShortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'PowerFlow.lnk'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PowerFlow'
$appName = 'PowerFlow.App.exe'

function Test-UnderPath([string]$Path, [string]$Root) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    $fullRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    return $fullPath.StartsWith($fullRoot, [StringComparison]::OrdinalIgnoreCase)
}

function Get-PowerFlowProcesses {
    @(Get-CimInstance Win32_Process -Filter "Name='PowerFlow.App.exe'" -ErrorAction SilentlyContinue)
}

function Stop-ManagedPowerFlow {
    $running = Get-PowerFlowProcesses
    if ($running.Count -eq 0) { return }
    $paths = @($running | ForEach-Object ExecutablePath | Where-Object { $_ } | Select-Object -Unique)
    $unmanaged = @($paths | Where-Object { -not (Test-UnderPath $_ $installRoot) -and -not (Test-UnderPath $_ $legacyReleaseRoot) })
    if ($unmanaged.Count -gt 0) { throw 'Another PowerFlow copy is running outside the installed location. Close that development/portable copy and retry.' }
    if ($paths.Count -gt 0) {
        $relay = Start-Process -FilePath $paths[0] -ArgumentList '--shutdown' -PassThru
        if (-not $relay.WaitForExit(5000)) { throw 'PowerFlow shutdown relay did not exit promptly.' }
    }
    $deadline = [DateTimeOffset]::Now.AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 250
        $remaining = @(Get-PowerFlowProcesses | Where-Object { $_.ExecutablePath -and ((Test-UnderPath $_.ExecutablePath $installRoot) -or (Test-UnderPath $_.ExecutablePath $legacyReleaseRoot)) })
        if ($remaining.Count -eq 0) { return }
    } while ([DateTimeOffset]::Now -lt $deadline)
    throw 'PowerFlow did not close cleanly. Install stopped without force-killing it.'
}

function New-PowerFlowShortcut([string]$Path, [string]$Target) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Path) | Out-Null
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($Path)
    $shortcut.TargetPath = $Target
    $shortcut.Arguments = '--dashboard'
    $shortcut.WorkingDirectory = $installRoot
    $shortcut.IconLocation = "$Target,0"
    $shortcut.Description = 'Open PowerFlow'
    $shortcut.Save()
}

$payload = [IO.Path]::GetFullPath($PayloadPath)
$sourceExe = Join-Path $payload $appName
if (-not (Test-Path $sourceExe -PathType Leaf)) { throw "Payload does not contain $appName." }

Stop-ManagedPowerFlow
$parent = Split-Path -Parent $installRoot
New-Item -ItemType Directory -Force -Path $parent | Out-Null
$staging = Join-Path $parent ('.PowerFlow-install-' + [Guid]::NewGuid().ToString('N'))
$backup = Join-Path $parent ('.PowerFlow-backup-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $staging | Out-Null

try {
    Copy-Item (Join-Path $payload '*') $staging -Recurse -Force
    Copy-Item $PSCommandPath (Join-Path $staging 'Install-PowerFlow.ps1') -Force
    Copy-Item (Join-Path $PSScriptRoot 'Uninstall-PowerFlow.ps1') (Join-Path $staging 'Uninstall-PowerFlow.ps1') -Force
    if (Test-Path $installRoot) { Move-Item $installRoot $backup }
    Move-Item $staging $installRoot

    $installedExe = Join-Path $installRoot $appName
    New-PowerFlowShortcut $startMenuShortcut $installedExe
    if ($NoDesktopShortcut) { Remove-Item $desktopShortcut -Force -ErrorAction SilentlyContinue }
    else { New-PowerFlowShortcut $desktopShortcut $installedExe }

    New-Item -Path $runKey -Force | Out-Null
    Set-ItemProperty -Path $runKey -Name PowerFlow -Value ('"{0}" --background' -f $installedExe)

    New-Item -Path $uninstallKey -Force | Out-Null
    Set-ItemProperty -Path $uninstallKey -Name DisplayName -Value 'PowerFlow'
    Set-ItemProperty -Path $uninstallKey -Name Publisher -Value 'PowerFlow'
    Set-ItemProperty -Path $uninstallKey -Name InstallLocation -Value $installRoot
    Set-ItemProperty -Path $uninstallKey -Name DisplayIcon -Value $installedExe
    Set-ItemProperty -Path $uninstallKey -Name UninstallString -Value ('powershell.exe -NoProfile -ExecutionPolicy Bypass -File "{0}"' -f (Join-Path $installRoot 'Uninstall-PowerFlow.ps1'))
    Set-ItemProperty -Path $uninstallKey -Name NoModify -Type DWord -Value 1
    Set-ItemProperty -Path $uninstallKey -Name NoRepair -Type DWord -Value 1

    if (Test-Path $backup) { Remove-Item $backup -Recurse -Force }
    if (-not $NoLaunch) { Start-Process -FilePath $installedExe -ArgumentList '--dashboard' }

    [pscustomobject]@{
        Installed = $true
        InstallRoot = $installRoot
        UserDataPreserved = $userDataRoot
        DesktopShortcut = -not [bool]$NoDesktopShortcut
    } | Format-List
}
catch {
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue }
    if (Test-Path $backup) {
        if (Test-Path $installRoot) { Remove-Item $installRoot -Recurse -Force -ErrorAction SilentlyContinue }
        Move-Item $backup $installRoot
    }
    throw
}
