[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.2.0',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('win-x64')]
    [string]$RuntimeIdentifier = 'win-x64',

    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$artifactsRoot = Join-Path $repositoryRoot 'artifacts'
$publishDirectory = Join-Path $artifactsRoot 'publish'
$installerDirectory = Join-Path $artifactsRoot 'installer'
$installerScript = Join-Path $repositoryRoot 'installer\OpenQCY.iss'

function Reset-GeneratedDirectory {
    param([Parameter(Mandatory)][string]$Path)

    $resolvedArtifactsRoot = [IO.Path]::GetFullPath($artifactsRoot).TrimEnd('\') + '\'
    $resolvedPath = [IO.Path]::GetFullPath($Path)
    if (-not $resolvedPath.StartsWith($resolvedArtifactsRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean a path outside artifacts: $resolvedPath"
    }

    if (Test-Path -LiteralPath $resolvedPath) {
        Remove-Item -LiteralPath $resolvedPath -Recurse -Force
    }

    New-Item -ItemType Directory -Path $resolvedPath -Force | Out-Null
}

function Find-InnoCompiler {
    $programFilesX86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
    $candidates = @(
        (Join-Path $programFilesX86 'Inno Setup 7\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
        (Join-Path $programFilesX86 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

    if (-not $candidates) {
        throw 'Inno Setup was not found. Install it with: winget install --id JRSoftware.InnoSetup -e'
    }

    return $candidates | Select-Object -First 1
}

Reset-GeneratedDirectory -Path $installerDirectory

Push-Location $repositoryRoot
try {
    if ($SkipPublish) {
        $publishedExecutable = Join-Path $publishDirectory 'OpenQCY.Desktop.exe'
        if (-not (Test-Path -LiteralPath $publishedExecutable)) {
            throw "Published application was not found: $publishedExecutable"
        }
    }
    else {
        Reset-GeneratedDirectory -Path $publishDirectory

        dotnet restore OpenQCY.Desktop.csproj -r $RuntimeIdentifier -p:PublishReadyToRun=true
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet restore failed with exit code $LASTEXITCODE"
        }

        $publishArguments = @(
            'publish',
            'OpenQCY.Desktop.csproj',
            '-c', $Configuration,
            '-p:Platform=x64',
            '-r', $RuntimeIdentifier,
            '--self-contained', 'true',
            '-p:WindowsAppSDKSelfContained=true',
            "-p:Version=$Version",
            '--no-restore',
            '-o', $publishDirectory
        )
        & dotnet @publishArguments
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed with exit code $LASTEXITCODE"
        }
    }

    $iscc = Find-InnoCompiler
    $innoArguments = @(
        "/DAppVersion=$Version",
        "/DSourceDir=$publishDirectory",
        "/DOutputDir=$installerDirectory",
        $installerScript
    )
    & $iscc @innoArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup failed with exit code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}

$installer = Join-Path $installerDirectory 'OpenQCY-Desktop-Setup.exe'
if (-not (Test-Path -LiteralPath $installer)) {
    throw "Installer was not generated: $installer"
}

$hash = Get-FileHash -LiteralPath $installer -Algorithm SHA256
Write-Host "Installer: $installer"
Write-Host "SHA-256:  $($hash.Hash)"
