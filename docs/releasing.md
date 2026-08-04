# Releasing OpenQCY Desktop

The release workflow turns a semantic version tag into three public files:

- `OpenQCY-Desktop-Setup.exe`: recommended per-user Windows installer
- `OpenQCY-Desktop-win-x64.zip`: portable, self-contained build
- `SHA256SUMS.txt`: SHA-256 hashes for both downloads

## Publish a release

Start from an up-to-date, clean `main` branch and choose the next semantic version:

```powershell
git switch main
git pull --ff-only
git tag v0.1.0
git push origin v0.1.0
```

The [`release` workflow](../.github/workflows/release.yml) tests the protocol library, publishes the self-contained app, builds the installer, creates the checksums, and opens the GitHub release with generated notes.

The tag and application version must follow `MAJOR.MINOR.PATCH`. The installer filename intentionally stays stable so the `releases/latest/download` links in the README always point to the newest version.

## Build the installer locally

Install .NET 10 and Inno Setup 6.7 or later, then run:

```powershell
pwsh ./scripts/build-installer.ps1 -Version 0.1.0
```

The generated files are written only below `artifacts/` and are ignored by Git.

## Code signing

Community builds are currently unsigned. Before presenting the installer as a trusted publisher without SmartScreen warnings, sign both the application and installer with an Authenticode certificate and protect the certificate as a GitHub Actions secret. Checksums verify integrity, but they do not replace publisher signing.
