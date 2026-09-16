<#
.SYNOPSIS
    Local build + sign + publish pipeline for Themia.
    Uses SimplySign Desktop (Certum) for Windows Authenticode code signing.

.DESCRIPTION
    1. Bumps version in tauri.conf.json and Cargo.toml
    2. Verifies SimplySign Desktop is running and a code signing cert is available
    3. Builds the app (Tauri invokes signtool via signCommand)
    4. Verifies the signature on the output installer
    5. Commits, tags, and pushes main to origin (crazy-rabbit-software/themia)
    6. Publishes the signed artifacts as a GitHub release in that repository

.PARAMETER Bump
    Semver bump type: patch, minor, or major. Mutually exclusive with -Version.

.PARAMETER Version
    Explicit version string (e.g., "0.7.0"). Mutually exclusive with -Bump.

.PARAMETER SkipPublish
    Build and sign only, skip git push and GitHub release upload.

.EXAMPLE
    .\scripts\release-local.ps1 -Bump patch
    .\scripts\release-local.ps1 -Version 0.7.0
    .\scripts\release-local.ps1 -Bump minor -SkipPublish
#>

param(
    [Parameter(Mandatory=$false)]
    [ValidateSet("patch", "minor", "major")]
    [string]$Bump,

    [Parameter(Mandatory=$false)]
    [string]$Version,

    [switch]$SkipPublish,

    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

# ── Load .env ────────────────────────────────────────────────────────────────

$envFile = Join-Path $Root ".env"
if (Test-Path $envFile) {
    Get-Content $envFile | ForEach-Object {
        if ($_ -match '^\s*([^#][^=]+?)\s*=\s*(.+)\s*$') {
            [Environment]::SetEnvironmentVariable($Matches[1], $Matches[2], "Process")
        }
    }
    Write-Host "Loaded .env" -ForegroundColor Green
} else {
    Write-Host "No .env file found - using existing environment variables." -ForegroundColor Yellow
}
Set-Location $Root

# Google's Desktop OAuth client secret is compiled in from the environment
# (widgets/office/email, [S-53]). A build without it cannot sign in to Google.
if (-not $env:THEMIA_GOOGLE_CLIENT_SECRET) {
    Write-Error "THEMIA_GOOGLE_CLIENT_SECRET is not set. Add it to .env before releasing."
    exit 1
}

$TauriConf = Join-Path $Root "src-tauri\tauri.conf.json"
$CargoToml = Join-Path $Root "src-tauri\Cargo.toml"
$BundleDir = Join-Path $Root "target\release\bundle\nsis"
$Repo = "crazy-rabbit-software/themia"

# Source, tags and releases all live in $Repo. Check the branch and the
# remote before anything is bumped or built, so a release can never push
# somewhere else.
if (-not $SkipPublish) {
    $branch = git rev-parse --abbrev-ref HEAD
    if ($branch -ne "main") {
        Write-Error "Releases are cut from main (current branch: $branch)."
        exit 1
    }
    $origin = git remote get-url origin
    if ($origin -notmatch "github\.com[/:]$([regex]::Escape($Repo))(\.git)?/?$") {
        Write-Error "origin points at $origin, not $Repo. Refusing to release."
        exit 1
    }
}

# ── Helpers ──────────────────────────────────────────────────────────────────

function Get-CurrentVersion {
    $conf = Get-Content $TauriConf -Raw | ConvertFrom-Json
    return $conf.version
}

function Bump-Semver([string]$current, [string]$type) {
    $parts = $current.Split(".")
    switch ($type) {
        "major" { $parts[0] = [int]$parts[0] + 1; $parts[1] = "0"; $parts[2] = "0" }
        "minor" { $parts[1] = [int]$parts[1] + 1; $parts[2] = "0" }
        "patch" { $parts[2] = [int]$parts[2] + 1 }
    }
    return $parts -join "."
}

function Set-Version([string]$newVersion) {
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)

    # tauri.conf.json - regex replace to preserve formatting
    $json = [System.IO.File]::ReadAllText($TauriConf, $utf8NoBom)
    $json = $json -replace '("version"\s*:\s*")[^"]*(")', "`${1}$newVersion`${2}"
    [System.IO.File]::WriteAllText($TauriConf, $json, $utf8NoBom)

    # Cargo.toml - replace version in [package] section
    $cargo = [System.IO.File]::ReadAllText($CargoToml, $utf8NoBom)
    $cargo = $cargo -replace '(?m)^(version\s*=\s*")[^"]*(")', "`${1}$newVersion`${2}"
    [System.IO.File]::WriteAllText($CargoToml, $cargo, $utf8NoBom)
}

function Write-Step([string]$msg) {
    Write-Host "`n=== $msg ===" -ForegroundColor Cyan
}

function New-ReleaseNotes([string]$tag) {
    # Find previous tag (latest tag reachable from HEAD^, excluding current tag)
    $prevTag = git describe --tags --abbrev=0 "$tag^" 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $prevTag) {
        $range = $tag
    } else {
        $range = "$prevTag..$tag"
    }

    $commits = git log $range --pretty=format:"%s" --no-merges 2>$null
    if (-not $commits) { $commits = @() }
    if ($commits -isnot [array]) { $commits = @($commits) }

    $features = @()
    $fixes = @()

    foreach ($line in $commits) {
        $l = $line.Trim()
        if (-not $l) { continue }
        # Only surface user-facing commits (feat: / fix:). Skip chore/refactor/docs/etc.
        if ($l -match '^feat(\([^)]+\))?:\s*(.+)$') {
            $features += $Matches[2]
        } elseif ($l -match '^fix(\([^)]+\))?:\s*(.+)$') {
            $fixes += $Matches[2]
        }
    }

    $sb = New-Object System.Text.StringBuilder
    if ($features.Count -gt 0) {
        [void]$sb.AppendLine("## New")
        foreach ($f in $features) { [void]$sb.AppendLine("- $f") }
        [void]$sb.AppendLine("")
    }
    if ($fixes.Count -gt 0) {
        [void]$sb.AppendLine("## Fixed")
        foreach ($f in $fixes) { [void]$sb.AppendLine("- $f") }
        [void]$sb.AppendLine("")
    }
    if ($sb.Length -eq 0) {
        [void]$sb.AppendLine("Maintenance release.")
    }
    return $sb.ToString().TrimEnd()
}

# ── Resolve version ──────────────────────────────────────────────────────────

if ($Bump -and $Version) {
    Write-Error "Specify either -Bump or -Version, not both."
    exit 1
}
if (-not $Bump -and -not $Version) {
    Write-Error "Specify -Bump (patch|minor|major) or -Version (e.g., 0.7.0)."
    exit 1
}

$currentVersion = Get-CurrentVersion
if ($Bump) {
    $newVersion = Bump-Semver $currentVersion $Bump
} else {
    $newVersion = $Version
}
$tag = "v$newVersion"

Write-Step "Release: $currentVersion -> $newVersion ($tag)"

# ── Run integration tests ────────────────────────────────────────────────────

if ($SkipTests) {
    Write-Step "Skipping integration tests (-SkipTests)"
    Write-Host "WARNING: e2e gate bypassed. Untested release." -ForegroundColor Yellow
} else {
    Write-Step "Building debug binary for e2e tests"
    npm run test:e2e:build
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Debug build for e2e tests failed."
        exit 1
    }

    Write-Step "Running integration tests"
    npm run test:e2e
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Integration tests failed. Aborting release."
        exit 1
    }
    Write-Host "All integration tests passed." -ForegroundColor Green
}

# ── Check SimplySign Desktop ─────────────────────────────────────────────────

Write-Step "Checking SimplySign Desktop"

$simplysign = Get-Process -Name "SimplySign*" -ErrorAction SilentlyContinue
if (-not $simplysign) {
    Write-Error "SimplySign Desktop is not running. Please open it and authenticate before running this script."
    exit 1
}
Write-Host "SimplySign Desktop is running." -ForegroundColor Green

# Check for code signing certificate in the store
$certs = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue
if (-not $certs -or $certs.Count -eq 0) {
    Write-Error "No code signing certificate found in CurrentUser\My. Is SimplySign authenticated?"
    exit 1
}
$cert = $certs[0]
Write-Host "Found code signing cert: $($cert.Subject)" -ForegroundColor Green
Write-Host "  Thumbprint: $($cert.Thumbprint)" -ForegroundColor Gray
Write-Host "  Expires:    $($cert.NotAfter)" -ForegroundColor Gray

# ── Bump version ──────────────────────────────────────────────────────────────

Write-Step "Bumping version to $newVersion"
Set-Version $newVersion
Write-Host "Updated tauri.conf.json and Cargo.toml" -ForegroundColor Green

# ── Build ─────────────────────────────────────────────────────────────────────

Write-Step "Building (Tauri will sign via certificateThumbprint)"

# Tauri update signing keys (same as CI)
if ($env:TAURI_SIGNING_PRIVATE_KEY) {
    Write-Host "TAURI_SIGNING_PRIVATE_KEY is set - updater artifacts will be signed." -ForegroundColor Green
} else {
    Write-Host "WARNING: TAURI_SIGNING_PRIVATE_KEY not set - updater .sig files won't be generated." -ForegroundColor Yellow
    Write-Host "Set it if you need the auto-updater to work." -ForegroundColor Yellow
}

npm run build
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed."
    exit 1
}

# ── Verify signature ─────────────────────────────────────────────────────────

Write-Step "Verifying Authenticode signature"

$installer = Get-ChildItem "$BundleDir\*${newVersion}*-setup.exe" | Select-Object -First 1
if (-not $installer) {
    Write-Error "No installer .exe found in $BundleDir"
    exit 1
}

Write-Host "Verifying: $($installer.Name)"
$sigCheck = Get-AuthenticodeSignature $installer.FullName
if ($sigCheck.Status -eq "Valid") {
    Write-Host "Signature VALID" -ForegroundColor Green
    Write-Host "  Signer: $($sigCheck.SignerCertificate.Subject)" -ForegroundColor Gray
    Write-Host "  Timestamp: $($sigCheck.TimeStamperCertificate.Subject)" -ForegroundColor Gray
} else {
    Write-Error "Signature verification failed: $($sigCheck.Status). StatusMessage: $($sigCheck.StatusMessage)"
    exit 1
}

Write-Host "`nBuild artifacts:" -ForegroundColor Cyan
Get-ChildItem $BundleDir | ForEach-Object { Write-Host "  $($_.Name)  ($([math]::Round($_.Length / 1MB, 1)) MB)" }

if ($SkipPublish) {
    Write-Host "`nDone (publish skipped). Artifacts in: $BundleDir" -ForegroundColor Green
    exit 0
}

# ── Git commit + tag + push ──────────────────────────────────────────────────

Write-Step "Committing and tagging"

git add src-tauri/tauri.conf.json src-tauri/Cargo.toml Cargo.lock
git commit -m "chore: release $tag"
git tag $tag
Write-Host "Created commit and tag $tag" -ForegroundColor Green

Write-Step "Pushing to origin"
git push origin main
git push origin $tag
Write-Host "Pushed commit and tag" -ForegroundColor Green

# ── Publish to the release repo ───────────────────────────────────────────────

Write-Step "Publishing to $Repo"

$pubUrl = "https://github.com/$Repo/releases/download/$tag"
$outDir = Join-Path $BundleDir "pub"
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

# Copy installer with versioned name (single canonical artifact)
$versionedExe = Join-Path $outDir "themia-installer-$tag.exe"
$versionedSig = Join-Path $outDir "themia-installer-$tag.exe.sig"
Copy-Item $installer.FullName $versionedExe

# Generate latest.json from the current build's .sig file
$sigFile = "$($installer.FullName).sig"

if (Test-Path $sigFile) {
    Copy-Item $sigFile $versionedSig
    $sig = Get-Content $sigFile -Raw
    $pubDate = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    $downloadUrl = "$pubUrl/themia-installer-$tag.exe"
    $latestObj = @{
        version = $newVersion
        notes = ""
        pub_date = $pubDate
        platforms = @{
            "windows-x86_64" = @{
                signature = $sig.Trim()
                url = $downloadUrl
            }
        }
    }
    $latestJson = $latestObj | ConvertTo-Json -Depth 5
    [System.IO.File]::WriteAllText((Join-Path $outDir "latest.json"), $latestJson, (New-Object System.Text.UTF8Encoding $false))
    Write-Host "Generated latest.json for updater" -ForegroundColor Green
} else {
    Write-Host "WARNING: No .sig file found - latest.json not generated. Auto-updater won't work." -ForegroundColor Yellow
}

# Create release on public repo
$releaseToken = $env:RELEASE_TOKEN
if (-not $releaseToken) {
    Write-Error "RELEASE_TOKEN environment variable not set. Cannot publish to $Repo."
    exit 1
}

$env:GH_TOKEN = $releaseToken

# Generate changelog from git history between previous tag and this tag
$notesBody = New-ReleaseNotes $tag
$notesFile = Join-Path $outDir "release-notes.md"
[System.IO.File]::WriteAllText($notesFile, $notesBody, (New-Object System.Text.UTF8Encoding $false))
Write-Host "Generated release notes:" -ForegroundColor Green
Write-Host $notesBody -ForegroundColor Gray

gh release create $tag `
    --repo $Repo `
    --title "Themia $tag" `
    --notes-file $notesFile `
    --latest 2>$null

# Upload artifacts (single installer + its .sig + updater manifest)
$filesToUpload = @($versionedExe)

if (Test-Path $versionedSig) { $filesToUpload += $versionedSig }
if (Test-Path (Join-Path $outDir "latest.json")) { $filesToUpload += (Join-Path $outDir "latest.json") }

gh release upload $tag $filesToUpload --repo $Repo --clobber

# Restore GH_TOKEN
Remove-Item Env:\GH_TOKEN -ErrorAction SilentlyContinue

Write-Step "Release $tag published successfully!"
Write-Host "Public release: https://github.com/$Repo/releases/tag/$tag" -ForegroundColor Green