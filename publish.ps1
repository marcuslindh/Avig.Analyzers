#Requires -Version 7.0
<#
.SYNOPSIS
    Builds, tests, packs and shape-checks Avig.Analyzers.

.DESCRIPTION
    The script never pushes on its own. Publishing to nuget.org is a manual step, and the
    API key must end up neither in the repository nor in the script's shell history. What is
    printed at the end is the command you run by hand.

.PARAMETER NoBump
    Keep the current version instead of incrementing the patch number.

.PARAMETER SkipTests
    Skip the test run. Use when you have already run them.

.PARAMETER SkipVerify
    Skip the shape check of the nupkg. Do not do this without a specific reason.

.PARAMETER Feed
    Folder to copy the built package into for local smoke testing.
#>
param(
    [switch]$NoBump,
    [switch]$SkipTests,
    [switch]$SkipVerify,
    [string]$Feed = "G:\LocalNugetRepo"
)

$ErrorActionPreference = "Stop"

$root     = $PSScriptRoot
$project  = Join-Path $root "src\Avig.Analyzers\Avig.Analyzers.csproj"
$tests    = Join-Path $root "tests\Avig.Analyzers.Tests\Avig.Analyzers.Tests.csproj"
$unshipped = Join-Path $root "src\Avig.Analyzers\AnalyzerReleases.Unshipped.md"

Write-Host ""
Write-Host "=== Avig.Analyzers ===" -ForegroundColor Cyan
Write-Host ""

function Get-CurrentVersion {
    [xml]$csproj = Get-Content $project
    $node = $csproj.Project.PropertyGroup | Where-Object { $_.Version }

    return [string]$node.Version
}

function Set-Version([string]$NewVersion) {
    # A text replace, deliberately, not [xml].Save(). XmlDocument rewrites the entire file
    # on save: it drops the blank lines between PropertyGroups and normalises the line
    # endings, which turns a one-element version bump into a 70-line diff. The version is a
    # single element and editing it must not reformat everything around it.
    $content = Get-Content $project -Raw
    $updated = ([regex]'<Version>[^<]*</Version>').Replace($content, "<Version>$NewVersion</Version>", 1)
    if ($updated -eq $content) {
        Write-Host "      Could not rewrite <Version> in the csproj." -ForegroundColor Red
        exit 1
    }

    [System.IO.File]::WriteAllText((Resolve-Path $project), $updated, [System.Text.UTF8Encoding]::new($false))
}

# --- [1/5] Tests ---
if ($SkipTests) {
    Write-Host "[1/5] Tests: SKIPPED (-SkipTests)" -ForegroundColor Yellow
} else {
    Write-Host "[1/5] Running tests..." -ForegroundColor Yellow
    dotnet test $tests -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) {
        Write-Host "      Tests failed - not packing." -ForegroundColor Red
        exit 1
    }

    Write-Host "      OK" -ForegroundColor Green
}

# --- [2/5] Version ---
Write-Host "[2/5] Version" -ForegroundColor Yellow
$current = Get-CurrentVersion
if (-not $current) {
    Write-Host "      Found no <Version> in the csproj." -ForegroundColor Red
    exit 1
}

if ($NoBump) {
    $version = $current
    Write-Host "      $version (no bump)" -ForegroundColor Yellow
} else {
    $parts = $current.Split('.')
    $version = "$($parts[0]).$($parts[1]).$([int]$parts[2] + 1)"
    Set-Version $version
    Write-Host "      $current -> $version" -ForegroundColor Green
}

# --- [3/5] Pack ---
# ContinuousIntegrationBuild makes the paths deterministic, which is what SourceLink needs
# in order to point correctly from the embedded symbols.
Write-Host "[3/5] Packing..." -ForegroundColor Yellow
dotnet pack $project -c Release --nologo -v quiet -p:ContinuousIntegrationBuild=true
if ($LASTEXITCODE -ne 0) {
    Write-Host "      Pack failed." -ForegroundColor Red
    exit 1
}

$binRelease = Join-Path $root "src\Avig.Analyzers\bin\Release"
$package = Get-ChildItem -Path $binRelease -Filter "Avig.Analyzers.$version.nupkg" | Select-Object -First 1
if (-not $package) {
    Write-Host "      Found no package for version $version." -ForegroundColor Red
    exit 1
}

Write-Host "      $($package.Name)" -ForegroundColor Green

# --- [4/5] Shape check ---
# An analyzer package that was packed wrong installs without complaint and then does
# nothing: the DLL ends up in lib/ and is referenced as ordinary code instead of being run
# by the compiler. That is invisible at install time and only visible in the contents -
# hence this check. Do not remove it.
if ($SkipVerify) {
    Write-Host "[4/5] Shape check: SKIPPED (-SkipVerify)" -ForegroundColor Yellow
} else {
    Write-Host "[4/5] Checking the package contents..." -ForegroundColor Yellow

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
    try {
        $entries = $zip.Entries | ForEach-Object { $_.FullName }
    } finally {
        $zip.Dispose()
    }

    $analyzerDll = $entries | Where-Object { $_ -eq "analyzers/dotnet/cs/Avig.Analyzers.dll" }
    $libEntries  = $entries | Where-Object { $_ -like "lib/*" }
    $readme      = $entries | Where-Object { $_ -eq "README.md" }
    $template    = $entries | Where-Object { $_ -eq "avig-analyzers.editorconfig" }

    if (-not $analyzerDll) {
        Write-Host "      ERROR: analyzers/dotnet/cs/Avig.Analyzers.dll is missing." -ForegroundColor Red
        Write-Host "      The package would install without a single rule ever running." -ForegroundColor Red
        exit 1
    }

    if ($libEntries) {
        Write-Host "      ERROR: the package contains lib/ - the analyzer would be referenced as code:" -ForegroundColor Red
        $libEntries | ForEach-Object { Write-Host "        $_" -ForegroundColor Red }
        exit 1
    }

    if (-not $readme) {
        Write-Host "      ERROR: README.md is missing from the package root." -ForegroundColor Red
        exit 1
    }

    if (-not $template) {
        Write-Host "      ERROR: avig-analyzers.editorconfig is missing from the package root." -ForegroundColor Red
        exit 1
    }

    Write-Host "      analyzers/dotnet/cs/Avig.Analyzers.dll present, no lib/" -ForegroundColor Green
    Write-Host "      README.md + avig-analyzers.editorconfig present in the root" -ForegroundColor Green
}

# --- [5/5] Local feed ---
Write-Host "[5/5] Copying to the local feed..." -ForegroundColor Yellow
if (Test-Path $Feed) {
    Copy-Item $package.FullName -Destination $Feed -Force
    Write-Host "      $Feed\$($package.Name)" -ForegroundColor Green
} else {
    Write-Host "      $Feed does not exist - the package stays in bin\Release" -ForegroundColor Yellow
}

# --- Reminders ---
$unshippedHasRules = (Get-Content $unshipped | Where-Object { $_ -match '^\s*AVIG\d{4}\s*\|' }).Count -gt 0
if ($unshippedHasRules) {
    Write-Host ""
    Write-Host "REMINDER: AnalyzerReleases.Unshipped.md contains rules." -ForegroundColor Yellow
    Write-Host "Move them to Shipped under '## Release $version' before publishing," -ForegroundColor Yellow
    Write-Host "otherwise the package claims the rules were never released." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=== Avig.Analyzers v$version done ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Publish manually (the API key must not be checked in):" -ForegroundColor Cyan
Write-Host ""
Write-Host "    `$env:NUGET_API_KEY = '<your key from nuget.org>'" -ForegroundColor Gray
Write-Host "    dotnet nuget push `"$($package.FullName)`" ``" -ForegroundColor Gray
Write-Host "        --api-key `$env:NUGET_API_KEY ``" -ForegroundColor Gray
Write-Host "        --source https://api.nuget.org/v3/index.json" -ForegroundColor Gray
Write-Host ""
Write-Host "The script does not push on its own. That is deliberate." -ForegroundColor Cyan

if (-not $NoBump) {
    Write-Host ""
    Write-Host "Do not forget:" -ForegroundColor Cyan
    Write-Host "    git add -A; git commit -m `"Release v$version`"; git tag v$version; git push --follow-tags" -ForegroundColor Gray
}

Write-Host ""
