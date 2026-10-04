<#
.SYNOPSIS
    Build Themia's Microsoft Store package: an UNSIGNED .msixupload (and the .msix inside it).

.DESCRIPTION
    [S-1060] Publishes the app with Native AOT as a single-project MSIX (-p:ThemiaPackage=true, which
    switches WindowsPackageType to MSIX for this build only) from src/Themia.App/Package.appxmanifest,
    and leaves in -OutDir:

        Themia_<version>_x64.msixupload   what Partner Center takes (the .msix plus its symbols)
        Themia_<version>_x64.msix         the same package, for inspection or a local sideload test

    Nothing is signed: the Store signs the package with its own certificate for the reserved
    publisher. This script never creates, imports, trusts or references a certificate.

    The version comes from the manifest's Identity/@Version (the one place it is kept; the Store
    wants the fourth part 0) and is stamped into Themia.exe as well, so About shows what the Store
    installed.

    While the manifest still carries the identity placeholders (see the comment at its top), the
    package builds, but Partner Center will refuse it; the script says so.

    Sideload test (manual, the user's call - it needs a certificate this script will not make):
    see docs/STORE.md.

.PARAMETER OutDir
    Where the packages go. Default: artifacts\store under the repository root (git-ignored).

.PARAMETER ArtifactsPath
    The dotnet --artifacts-path for the build's intermediate files. Default: artifacts\store-build.
#>
[CmdletBinding()]
param(
    [string] $OutDir,
    [string] $ArtifactsPath
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src\Themia.App\Themia.App.csproj'
$manifestPath = Join-Path $repoRoot 'src\Themia.App\Package.appxmanifest'
if (-not $OutDir) { $OutDir = Join-Path $repoRoot 'artifacts\store' }
if (-not $ArtifactsPath) { $ArtifactsPath = Join-Path $repoRoot 'artifacts\store-build' }
$OutDir = [System.IO.Path]::GetFullPath($OutDir)
$ArtifactsPath = [System.IO.Path]::GetFullPath($ArtifactsPath)

# The ILCompiler shells out to vswhere to find the MSVC linker (see publish.ps1).
$vswhereDir = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if (-not (Test-Path (Join-Path $vswhereDir 'vswhere.exe'))) {
    throw "vswhere.exe not found under '$vswhereDir'. Visual Studio Build Tools with the C++ workload are required for Native AOT."
}
if ($env:PATH -notlike "*$vswhereDir*") { $env:PATH = "$vswhereDir;$env:PATH" }

# ---- The manifest: version and identity -------------------------------------------------------
[xml] $manifest = Get-Content -LiteralPath $manifestPath -Raw
$identity = $manifest.Package.Identity
$version = [string] $identity.Version
if ($version -notmatch '^(\d+)\.(\d+)\.(\d+)\.0$') {
    throw "Package.appxmanifest Identity/@Version is '$version'; the Store needs Major.Minor.Build.0."
}
$appVersion = "$($Matches[1]).$($Matches[2]).$($Matches[3])"

$placeholders = @()
if ($identity.Name -like '*.RESERVE') { $placeholders += "Identity/@Name ($($identity.Name))" }
if ($identity.Publisher -eq 'CN=00000000-0000-0000-0000-000000000000') { $placeholders += "Identity/@Publisher ($($identity.Publisher))" }

Write-Host "Packaging Themia $version (Native AOT, MSIX, unsigned)..." -ForegroundColor Cyan

$stage = Join-Path $ArtifactsPath 'AppPackages'
if (Test-Path $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }

# AppxPackageDir ends in a forward slash: a trailing backslash before the closing quote would escape
# the quote. The MSIX tooling packs the Native AOT publish output (no managed Themia.dll ships).
& dotnet publish $project `
    -c Release `
    --artifacts-path $ArtifactsPath `
    -p:ThemiaPackage=true `
    -p:Version=$appVersion `
    -p:GenerateAppxPackageOnBuild=true `
    -p:UapAppxPackageBuildMode=StoreUpload `
    -p:AppxBundle=Never `
    -p:AppxPackageSigningEnabled=false `
    -p:AppxSymbolPackageEnabled=false `
    "-p:AppxPackageDir=$stage/"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

# ---- Collect and check ------------------------------------------------------------------------
$upload = @(Get-ChildItem $stage -Recurse -Filter '*.msixupload')
$msix = @(Get-ChildItem $stage -Recurse -Filter '*.msix')
if ($upload.Count -ne 1 -or $msix.Count -ne 1) {
    throw "Expected one .msixupload and one .msix under '$stage', found $($upload.Count) and $($msix.Count)."
}

# The package must hold the native executable and the compiled XAML (the published build dies at
# the first InitializeComponent without them), and must not be signed.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($msix[0].FullName)
try {
    $names = $zip.Entries | ForEach-Object { $_.FullName }
} finally {
    $zip.Dispose()
}
foreach ($required in @('Themia.exe', 'AppxManifest.xml', 'resources.pri')) {
    if ($names -notcontains $required) { throw "The package has no $required." }
}
if (-not ($names | Where-Object { $_ -like '*.xbf' })) { throw 'The package has no compiled XAML (.xbf).' }
if ($names -contains 'AppxSignature.p7x') { throw 'The package is signed; the Store upload must not be.' }
if ($names | Where-Object { $_ -like 'Themia.dll' -or $_ -like 'Themia.Core.dll' }) {
    throw 'The package holds managed Themia assemblies: the Native AOT publish did not run.'
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$uploadOut = Join-Path $OutDir "Themia_${version}_x64.msixupload"
$msixOut = Join-Path $OutDir "Themia_${version}_x64.msix"
Copy-Item -LiteralPath $upload[0].FullName -Destination $uploadOut -Force
Copy-Item -LiteralPath $msix[0].FullName -Destination $msixOut -Force

Write-Host ''
Write-Host ('  {0,-14} {1:N1} MB  {2}' -f 'msixupload', ((Get-Item $uploadOut).Length / 1MB), $uploadOut) -ForegroundColor Green
Write-Host ('  {0,-14} {1:N1} MB  {2}' -f 'msix', ((Get-Item $msixOut).Length / 1MB), $msixOut) -ForegroundColor Green
Write-Host ('  {0,-14} {1}' -f 'files', $names.Count) -ForegroundColor Green

if ($placeholders.Count -gt 0) {
    Write-Host ''
    Write-Warning ("Partner Center will refuse this upload: the manifest still has placeholders for " + ($placeholders -join ' and ') +
        ". Copy the values from Partner Center > Product identity into src\Themia.App\Package.appxmanifest.")
}
