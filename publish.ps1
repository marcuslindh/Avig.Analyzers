#Requires -Version 7.0
<#
.SYNOPSIS
    Bygger, testar, packar och formkontrollerar Avig.Analyzers.

.DESCRIPTION
    Skriptet pushar aldrig sjalv. Publicering till nuget.org ar ett manuellt steg, och
    API-nyckeln ska varken hamna i repot eller i skriptets shell-historik. Det som skrivs
    ut pa slutet ar kommandot du kor for hand.

.PARAMETER NoBump
    Behall nuvarande version i stallet for att oka patch-siffran.

.PARAMETER SkipTests
    Hoppa over testkorningen. Anvands nar du redan kort dem.

.PARAMETER SkipVerify
    Hoppa over formkontrollen av nupkg:en. Gor det inte utan sarskild anledning.

.PARAMETER Feed
    Mapp att kopiera det byggda paketet till for lokal roktestning.
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
    [xml]$csproj = Get-Content $project
    foreach ($group in $csproj.Project.PropertyGroup) {
        if ($group.Version) { $group.Version = $NewVersion }
    }

    $csproj.Save((Resolve-Path $project))
}

# --- [1/5] Tester ---
if ($SkipTests) {
    Write-Host "[1/5] Tester: HOPPAS OVER (-SkipTests)" -ForegroundColor Yellow
} else {
    Write-Host "[1/5] Kor tester..." -ForegroundColor Yellow
    dotnet test $tests -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) {
        Write-Host "      Tester misslyckades - packar inte." -ForegroundColor Red
        exit 1
    }

    Write-Host "      OK" -ForegroundColor Green
}

# --- [2/5] Version ---
Write-Host "[2/5] Version" -ForegroundColor Yellow
$current = Get-CurrentVersion
if (-not $current) {
    Write-Host "      Hittade ingen <Version> i csproj." -ForegroundColor Red
    exit 1
}

if ($NoBump) {
    $version = $current
    Write-Host "      $version (ingen bump)" -ForegroundColor Yellow
} else {
    $parts = $current.Split('.')
    $version = "$($parts[0]).$($parts[1]).$([int]$parts[2] + 1)"
    Set-Version $version
    Write-Host "      $current -> $version" -ForegroundColor Green
}

# --- [3/5] Pack ---
# ContinuousIntegrationBuild gor sokvagarna deterministiska, vilket ar det SourceLink
# behover for att peka ratt fran de inbaddade symbolerna.
Write-Host "[3/5] Packar..." -ForegroundColor Yellow
dotnet pack $project -c Release --nologo -v quiet -p:ContinuousIntegrationBuild=true
if ($LASTEXITCODE -ne 0) {
    Write-Host "      Pack misslyckades." -ForegroundColor Red
    exit 1
}

$binRelease = Join-Path $root "src\Avig.Analyzers\bin\Release"
$package = Get-ChildItem -Path $binRelease -Filter "Avig.Analyzers.$version.nupkg" | Select-Object -First 1
if (-not $package) {
    Write-Host "      Hittade inget paket for version $version." -ForegroundColor Red
    exit 1
}

Write-Host "      $($package.Name)" -ForegroundColor Green

# --- [4/5] Formkontroll ---
# Ett analysatorpaket som packats fel installeras utan att klaga och gor sedan ingenting:
# DLL:en hamnar i lib/ och refereras som vanlig kod i stallet for att koras av
# kompilatorn. Det syns inte pa installationen, bara pa innehallet - darfor den har
# kontrollen. Ta inte bort den.
if ($SkipVerify) {
    Write-Host "[4/5] Formkontroll: HOPPAS OVER (-SkipVerify)" -ForegroundColor Yellow
} else {
    Write-Host "[4/5] Kontrollerar paketets innehall..." -ForegroundColor Yellow

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
        Write-Host "      FEL: analyzers/dotnet/cs/Avig.Analyzers.dll saknas." -ForegroundColor Red
        Write-Host "      Paketet skulle installeras utan att en enda regel kordes." -ForegroundColor Red
        exit 1
    }

    if ($libEntries) {
        Write-Host "      FEL: paketet innehaller lib/ - analysatorn skulle refereras som kod:" -ForegroundColor Red
        $libEntries | ForEach-Object { Write-Host "        $_" -ForegroundColor Red }
        exit 1
    }

    if (-not $readme) {
        Write-Host "      FEL: README.md saknas i paketroten." -ForegroundColor Red
        exit 1
    }

    if (-not $template) {
        Write-Host "      FEL: avig-analyzers.editorconfig saknas i paketroten." -ForegroundColor Red
        exit 1
    }

    Write-Host "      analyzers/dotnet/cs/Avig.Analyzers.dll finns, inget lib/" -ForegroundColor Green
    Write-Host "      README.md + avig-analyzers.editorconfig finns i roten" -ForegroundColor Green
}

# --- [5/5] Lokalt flode ---
Write-Host "[5/5] Kopierar till lokalt flode..." -ForegroundColor Yellow
if (Test-Path $Feed) {
    Copy-Item $package.FullName -Destination $Feed -Force
    Write-Host "      $Feed\$($package.Name)" -ForegroundColor Green
} else {
    Write-Host "      $Feed finns inte - paketet ligger kvar i bin\Release" -ForegroundColor Yellow
}

# --- Paminnelser ---
$unshippedHasRules = (Get-Content $unshipped | Where-Object { $_ -match '^\s*AVIG\d{4}\s*\|' }).Count -gt 0
if ($unshippedHasRules) {
    Write-Host ""
    Write-Host "PAMINNELSE: AnalyzerReleases.Unshipped.md innehaller regler." -ForegroundColor Yellow
    Write-Host "Flytta dem till Shipped under '## Release $version' innan du publicerar," -ForegroundColor Yellow
    Write-Host "annars sager paketet att reglerna aldrig slappts." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=== Avig.Analyzers v$version klart ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Publicera manuellt (API-nyckeln far inte checkas in):" -ForegroundColor Cyan
Write-Host ""
Write-Host "    `$env:NUGET_API_KEY = '<din nyckel fran nuget.org>'" -ForegroundColor Gray
Write-Host "    dotnet nuget push `"$($package.FullName)`" ``" -ForegroundColor Gray
Write-Host "        --api-key `$env:NUGET_API_KEY ``" -ForegroundColor Gray
Write-Host "        --source https://api.nuget.org/v3/index.json" -ForegroundColor Gray
Write-Host ""
Write-Host "Skriptet pushar inte sjalv. Det ar med flit." -ForegroundColor Cyan

if (-not $NoBump) {
    Write-Host ""
    Write-Host "Glom inte:" -ForegroundColor Cyan
    Write-Host "    git add -A; git commit -m `"Release v$version`"; git tag v$version; git push --follow-tags" -ForegroundColor Gray
}

Write-Host ""
