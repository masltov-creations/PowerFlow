[CmdletBinding()]
param(
    [string]$Version,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$appProject = Join-Path $repo 'src\PowerFlow.App\PowerFlow.App.csproj'
$setupProject = Join-Path $repo 'tools\PowerFlow.Setup\PowerFlow.Setup.csproj'
$artifacts = Join-Path $repo 'artifacts\installer'
$appPublish = Join-Path $artifacts 'app'
$setupPublish = Join-Path $artifacts 'setup'
$release = Join-Path $artifacts 'release'
$payloadZip = Join-Path $artifacts 'PowerFlow-payload.zip'
$setupArtifact = Join-Path $release 'PowerFlow-Setup.exe'

function Invoke-Checked([string]$File, [string[]]$Arguments) {
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$File failed with exit code $LASTEXITCODE." }
}

Push-Location $repo
try {
    if (-not $Version) {
        $PSNativeCommandUseErrorActionPreference = $false
        $tag = (& git describe --tags --exact-match 2>$null)
        if ($LASTEXITCODE -eq 0 -and $tag -and $tag.StartsWith('v')) { $Version = $tag.Substring(1) }
        else { $Version = '0.1.0-dev' }
        $PSNativeCommandUseErrorActionPreference = $true
    }

    if (-not $SkipTests) { Invoke-Checked 'dotnet' @('test', 'PowerFlow.sln', '-c', 'Release', '--nologo', '--verbosity', 'minimal') }

    Remove-Item $artifacts -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $appPublish, $setupPublish, $release | Out-Null

    # WinUI 3 unpackaged apps require their generated PRI/XBF resources beside the executable.
    # `dotnet publish` currently omits those app-owned resources for this project, so the
    # distribution payload is produced from an explicit self-contained Release build instead.
    Invoke-Checked 'dotnet' @('build', $appProject, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:DebugType=None', '-p:DebugSymbols=false', ('-p:PathMap={0}=/_/' -f $repo), '-o', $appPublish, '--nologo', '--verbosity', 'minimal')
    foreach ($required in @('PowerFlow.App.exe', 'PowerFlow.App.pri', 'App.xbf', 'Dashboard\MainWindow.xbf', 'coreclr.dll')) {
        if (-not (Test-Path (Join-Path $appPublish $required) -PathType Leaf)) { throw "App build payload is missing required WinUI/runtime file: $required" }
    }

    Compress-Archive -Path (Join-Path $appPublish '*') -DestinationPath $payloadZip -CompressionLevel Optimal -Force

    Invoke-Checked 'dotnet' @(
        'publish', $setupProject,
        '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:PublishTrimmed=false',
        '-p:DebugType=None',
        '-p:DebugSymbols=false',
        "-p:PowerFlowPayload=$payloadZip",
        "-p:Version=$Version",
        '-o', $setupPublish,
        '--nologo'
    )

    $builtSetup = Join-Path $setupPublish 'PowerFlow.Setup.exe'
    if (-not (Test-Path $builtSetup)) { throw 'Setup publish did not produce PowerFlow.Setup.exe.' }
    Copy-Item $builtSetup $setupArtifact -Force
    Invoke-Checked $setupArtifact @('--verify-package')

    $hash = (Get-FileHash $setupArtifact -Algorithm SHA256).Hash
    [IO.File]::WriteAllText((Join-Path $release 'PowerFlow-Setup.exe.sha256'), "$hash  PowerFlow-Setup.exe`n", [Text.UTF8Encoding]::new($false))

    [pscustomobject]@{
        Version = $Version
        Setup = $setupArtifact
        Sha256 = $hash
        AppPayload = $payloadZip
    } | Format-List
}
finally {
    Pop-Location
}
