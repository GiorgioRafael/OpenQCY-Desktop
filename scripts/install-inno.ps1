[CmdletBinding()]
param(
    [string]$Version = '6.7.3',
    [string]$Sha256 = '9C73C3BAE7ED48D44112A0F48E66742C00090BDB5BEF71D9D3C056C66E97B732'
)

$ErrorActionPreference = 'Stop'
$programFilesX86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
$existingCompiler = @(
    (Join-Path $programFilesX86 'Inno Setup 7\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
    (Join-Path $programFilesX86 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1

if ($existingCompiler) {
    Write-Host "Inno Setup already installed: $existingCompiler"
    exit 0
}

$releaseTag = 'is-' + $Version.Replace('.', '_')
$downloadUrl = "https://github.com/jrsoftware/issrc/releases/download/$releaseTag/innosetup-$Version.exe"
$downloadPath = Join-Path ([IO.Path]::GetTempPath()) "OpenQCY-InnoSetup-$Version.exe"

try {
    Invoke-WebRequest -Uri $downloadUrl -OutFile $downloadPath
    $actualHash = (Get-FileHash -LiteralPath $downloadPath -Algorithm SHA256).Hash
    if ($actualHash -ne $Sha256) {
        throw "Inno Setup hash mismatch. Expected $Sha256, got $actualHash."
    }

    $process = Start-Process -FilePath $downloadPath -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-' -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        throw "Inno Setup installation failed with exit code $($process.ExitCode)"
    }
}
finally {
    if (Test-Path -LiteralPath $downloadPath) {
        Remove-Item -LiteralPath $downloadPath -Force
    }
}

Write-Host "Inno Setup $Version installed successfully."
