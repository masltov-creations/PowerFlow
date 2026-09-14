[CmdletBinding()]
param(
    [switch]$RemoveUserData
)

$ErrorActionPreference = 'Stop'
$installRoot = Join-Path $env:LOCALAPPDATA 'Programs\PowerFlow'
$userDataRoot = Join-Path $env:LOCALAPPDATA 'PowerFlow'
$startMenuShortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\PowerFlow.lnk'
$desktopShortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'PowerFlow.lnk'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PowerFlow'
$appExe = Join-Path $installRoot 'PowerFlow.App.exe'

if (Test-Path $appExe) {
    $relay = Start-Process -FilePath $appExe -ArgumentList '--shutdown' -PassThru -ErrorAction SilentlyContinue
    if ($relay) { $null = $relay.WaitForExit(5000) }
    $deadline = [DateTimeOffset]::Now.AddSeconds(15)
    while ((Get-Process PowerFlow.App -ErrorAction SilentlyContinue) -and [DateTimeOffset]::Now -lt $deadline) { Start-Sleep -Milliseconds 250 }
}

$currentRun = (Get-ItemProperty -Path $runKey -Name PowerFlow -ErrorAction SilentlyContinue).PowerFlow
if ($currentRun -and $currentRun.Contains($installRoot, [StringComparison]::OrdinalIgnoreCase)) {
    Remove-ItemProperty -Path $runKey -Name PowerFlow -ErrorAction SilentlyContinue
}
Remove-Item $startMenuShortcut -Force -ErrorAction SilentlyContinue
Remove-Item $desktopShortcut -Force -ErrorAction SilentlyContinue
Remove-Item $uninstallKey -Recurse -Force -ErrorAction SilentlyContinue

if (Test-Path $installRoot) {
    $self = [IO.Path]::GetFullPath($PSCommandPath)
    if ($self.StartsWith(([IO.Path]::GetFullPath($installRoot).TrimEnd('\') + '\'), [StringComparison]::OrdinalIgnoreCase)) {
        $tempScript = Join-Path $env:TEMP ('PowerFlow-Uninstall-' + [Guid]::NewGuid().ToString('N') + '.ps1')
        Copy-Item $PSCommandPath $tempScript -Force
        $arguments = @('-NoProfile','-ExecutionPolicy','Bypass','-File',$tempScript)
        if ($RemoveUserData) { $arguments += '-RemoveUserData' }
        Start-Process powershell.exe -ArgumentList $arguments
        exit 0
    }
    Remove-Item $installRoot -Recurse -Force
}

if ($RemoveUserData -and (Test-Path $userDataRoot)) { Remove-Item $userDataRoot -Recurse -Force }
Write-Output 'PowerFlow uninstalled. User data was preserved unless -RemoveUserData was specified.'
