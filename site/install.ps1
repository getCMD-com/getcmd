#Requires -Version 5.1
<#
.SYNOPSIS
    Installs getcmd on Windows.

.DESCRIPTION
    irm https://getcmd.com/install.ps1 | iex

    Downloads the win-x64 release archive and SHA256SUMS from GitHub, verifies
    the checksum, extracts getcmd.exe to %LOCALAPPDATA%\getcmd and adds that
    folder to the user PATH. Set GETCMD_VERSION to install a specific version.
#>
[CmdletBinding()]
param(
    [string]$Version = $env:GETCMD_VERSION,
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'getcmd')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$repo = 'getCMD-com/getcmd'
$base = "https://github.com/$repo/releases"

$arch = $env:PROCESSOR_ARCHITECTURE
$rid = switch ($arch) {
    'AMD64' { 'win-x64' }
    'ARM64' { 'win-arm64' }
    default { throw "getcmd install: unsupported architecture $arch (x64 and ARM64 are supported)" }
}

if (-not $Version) {
    # /releases/latest ignores pre-releases; fall back to the newest release of any kind.
    try {
        $Version = (Invoke-RestMethod "https://api.github.com/repos/$repo/releases/latest").tag_name
    } catch {
        $Version = (Invoke-RestMethod "https://api.github.com/repos/$repo/releases?per_page=1")[0].tag_name
    }
}
$Version = $Version -replace '^v', ''
if (-not $Version) { throw 'getcmd install: could not determine the latest release; set GETCMD_VERSION' }

$tmp = Join-Path ([IO.Path]::GetTempPath()) ("getcmd-install-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
try {
    Invoke-WebRequest "$base/download/v$Version/SHA256SUMS" -OutFile (Join-Path $tmp 'SHA256SUMS')
    $sums = Get-Content (Join-Path $tmp 'SHA256SUMS')

    # Older releases have no ARM64 build; the x64 one runs under emulation.
    if ($rid -eq 'win-arm64' -and -not ($sums -match ' getcmd-\S+-win-arm64\.zip$')) {
        Write-Host "No win-arm64 build in v$Version; installing win-x64 (runs under emulation)"
        $rid = 'win-x64'
    }

    $archive = "getcmd-$Version-$rid.zip"
    Write-Host "Installing getcmd $Version ($rid)"
    Invoke-WebRequest "$base/download/v$Version/$archive" -OutFile (Join-Path $tmp $archive)

    $pattern = ' ' + [regex]::Escape($archive) + '$'
    $line = $sums | Where-Object { $_ -match $pattern } | Select-Object -First 1
    if (-not $line) { throw "getcmd install: $archive is not listed in SHA256SUMS" }
    $expected = ($line -split ' ')[0].ToLowerInvariant()
    $actual = (Get-FileHash (Join-Path $tmp $archive) -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $expected) { throw "getcmd install: checksum mismatch for ${archive}: expected $expected, got $actual" }
    Write-Host 'Checksum verified'

    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
    Expand-Archive -Path (Join-Path $tmp $archive) -DestinationPath $tmp -Force
    # Move replaces the file in one step, so a running getcmd is not overwritten in place.
    Move-Item -Path (Join-Path $tmp 'getcmd.exe') -Destination (Join-Path $InstallDir 'getcmd.exe') -Force
    Write-Host "Installed $(Join-Path $InstallDir 'getcmd.exe')"
} finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}

$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
$entries = @($userPath -split ';' | Where-Object { $_ })
if ($entries -notcontains $InstallDir) {
    [Environment]::SetEnvironmentVariable('Path', (($entries + $InstallDir) -join ';'), 'User')
    Write-Host "Added $InstallDir to your user PATH (open a new terminal to pick it up)"
}
if (($env:Path -split ';') -notcontains $InstallDir) {
    $env:Path = "$InstallDir;$env:Path"
}

Write-Host ''
Write-Host 'Next steps:'
Write-Host '  getcmd hook claude --install   # add the PreToolUse hook to ~/.claude/settings.json'
Write-Host '  getcmd doctor                  # check the installation'
