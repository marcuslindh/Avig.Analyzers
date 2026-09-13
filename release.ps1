#Requires -Version 7.0
<#
.SYNOPSIS
    Cuts a release: bumps the version, closes the release-tracking entry, verifies the package,
    commits and tags.

.DESCRIPTION
    The tag is what publishes. Pushing vX.Y.Z runs .github/workflows/publish.yml, which builds
    the package again on a runner and pushes it to nuget.org through Trusted Publishing.

    This script does everything up to that point and then stops, because a tag that has not left
    the machine can still be deleted and a published version cannot be replaced. Pass -Push to
    go all the way in one command.

    The package is packed and shape-checked BEFORE the tag is created, so a release that cannot
    be built never becomes a tag. If that verification fails, the working tree is restored.

    Versions carry at ten instead of running on. A bare run bumps the minor number, so 1.1.0
    becomes 1.2.0 and 1.9.0 becomes 1.10.0 - but 1.10.0 becomes 2.0.0, because ten is the last
    value a segment takes rather than the first it refuses. Patch carries the same way, into
    minor, which can carry on into major: 1.10.10 bumps to 2.0.0.

.PARAMETER Minor
    Bump the second number and reset the third. The default when no bump is named, since a
    release of this package is usually new rules.

.PARAMETER Patch
    Bump the third number. For a release that adds no rules.

.PARAMETER Major
    Bump the first number and reset the rest.

.PARAMETER Version
    Set an explicit version instead of bumping, e.g. 2.0.0 or 2.0.0-rc.1.

.PARAMETER Push
    Push the branch and the tag when everything succeeded, which starts the publish workflow.

.PARAMETER DryRun
    Report what would happen and change nothing.

.PARAMETER Branch
    The branch a release may be cut from. Defaults to main.

.EXAMPLE
    ./release.ps1
    New rules went in: 1.1.0 becomes 1.2.0, verified, committed and tagged locally.

.EXAMPLE
    ./release.ps1 -Patch -Push
    A documentation fix goes out without a second command.

.EXAMPLE
    ./release.ps1 -DryRun
    Says which version a bare run would produce, and changes nothing.
#>
param(
    [switch]$Patch,
    [switch]$Minor,
    [switch]$Major,
    [string]$Version,
    [switch]$Push,
    [switch]$DryRun,
    [string]$Branch = "main"
)

$ErrorActionPreference = "Stop"

# Ten is the last value a version segment takes, not the first it refuses: 1.9.0 bumps to
# 1.10.0, and 1.10.0 carries into 2.0.0. Change this one number to change the whole scheme.
$SegmentCeiling = 10

$root      = $PSScriptRoot
$project   = Join-Path $root "src\Avig.Analyzers\Avig.Analyzers.csproj"
$shipped   = Join-Path $root "src\Avig.Analyzers\AnalyzerReleases.Shipped.md"
$unshipped = Join-Path $root "src\Avig.Analyzers\AnalyzerReleases.Unshipped.md"
$publish   = Join-Path $root "publish.ps1"

function Fail([string]$Message) {
    Write-Host "      $Message" -ForegroundColor Red
    exit 1
}

function Step-Version([int]$MajorPart, [int]$MinorPart, [int]$PatchPart, [string]$Part) {
    switch ($Part) {
        "major" { $MajorPart++; $MinorPart = 0; $PatchPart = 0 }
        "minor" { $MinorPart++; $PatchPart = 0 }
        default { $PatchPart++ }
    }

    # Carried in order, so a patch bump can travel all the way: 1.10.10 becomes 2.0.0.
    if ($PatchPart -gt $SegmentCeiling) {
        $PatchPart = 0
        $MinorPart++
    }

    if ($MinorPart -gt $SegmentCeiling) {
        $MinorPart = 0
        $MajorPart++
    }

    return "$MajorPart.$MinorPart.$PatchPart"
}

function Save-Text([string]$Path, [string]$Text) {
    # UTF-8 without BOM, and the line endings already in the string. .gitattributes pins this
    # repository to CRLF and AVIG0006 enforces it, so nothing here may normalise to LF.
    [System.IO.File]::WriteAllText($Path, $Text, [System.Text.UTF8Encoding]::new($false))
}

Write-Host ""
Write-Host "=== Avig.Analyzers release ===" -ForegroundColor Cyan
if ($DryRun) {
    Write-Host "    DRY RUN - nothing is written, committed or pushed" -ForegroundColor Yellow
}

Write-Host ""

# --- [1/6] The repository has to be in a state a tag can describe ---
Write-Host "[1/6] Checking the working tree..." -ForegroundColor Yellow

$currentBranch = (git -C $root rev-parse --abbrev-ref HEAD).Trim()
if ($currentBranch -ne $Branch) {
    Fail "On branch '$currentBranch', not '$Branch'. Release from $Branch, or pass -Branch."
}

if ((git -C $root status --porcelain)) {
    Fail "The working tree has uncommitted changes. A tag must describe a committed state."
}

git -C $root fetch origin --tags --quiet
if ($LASTEXITCODE -ne 0) {
    Fail "Could not reach origin."
}

$behind = [int](git -C $root rev-list --count "$Branch..origin/$Branch")
if ($behind -gt 0) {
    Fail "$Branch is $behind commit(s) behind origin/$Branch. Pull first."
}

Write-Host "      clean, on $Branch, up to date with origin" -ForegroundColor Green

# --- [2/6] Work out the new version ---
Write-Host "[2/6] Version" -ForegroundColor Yellow

$csproj = Get-Content $project -Raw
$currentMatch = ([regex]'<Version>([^<]+)</Version>').Match($csproj)
if (-not $currentMatch.Success) {
    Fail "Found no <Version> in the csproj."
}

$current = $currentMatch.Groups[1].Value

$named = @($Patch, $Minor, $Major, [bool]$Version) | Where-Object { $_ }
if ($named.Count -gt 1) {
    Fail "Pick one of -Patch, -Minor, -Major or -Version."
}

if ($Version) {
    if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.\-]+)?$') {
        Fail "'$Version' is not a version. Expected 1.2.3 or 1.2.3-rc.1."
    }

    $next = $Version
    $part = "explicit"
} else {
    $core = ($current -split '-')[0]
    $parts = $core.Split('.')
    if ($parts.Count -ne 3) {
        Fail "Cannot bump '$current' - expected three numbers."
    }

    # Minor by default: a release of this package is usually new rules.
    $part = if ($Major) { "major" } elseif ($Patch) { "patch" } else { "minor" }
    $next = Step-Version ([int]$parts[0]) ([int]$parts[1]) ([int]$parts[2]) $part
}

if ($next -eq $current) {
    Fail "$next is already the current version."
}

$currentCore = [version]($current -split '-')[0]
$nextCore = [version]($next -split '-')[0]
if ($nextCore -lt $currentCore) {
    Fail "$next is lower than the current $current. Versions on nuget.org only go up."
}

$tag = "v$next"

if ((git -C $root tag -l $tag)) {
    Fail "Tag $tag already exists locally. Delete it or pick another version."
}

if ((git -C $root ls-remote --tags origin "refs/tags/$tag")) {
    Fail "Tag $tag already exists on origin. A published version cannot be replaced."
}

$carried = ""
if ($part -ne "explicit" -and $part -ne "major" -and $nextCore.Major -gt $currentCore.Major) {
    $carried = "   carried into a new major"
} elseif ($part -eq "patch" -and $nextCore.Minor -gt $currentCore.Minor) {
    $carried = "   carried into a new minor"
}

Write-Host "      $current -> $next   (tag $tag)$carried" -ForegroundColor Green

# --- [3/6] Close the release-tracking entry ---
# A rule left in Unshipped ships in the package while the tracking file says it was never
# released, which is what consumers read to answer "which version introduced this rule".
Write-Host "[3/6] Release tracking" -ForegroundColor Yellow

$unshippedText = Get-Content $unshipped -Raw
$unshippedBody = ($unshippedText -split "`r?`n" | Where-Object { $_ -notmatch '^\s*;' }) -join "`r`n"
$hasRules = $unshippedBody -match '^\s*AVIG\d{4}\s*\|'

if ($hasRules) {
    $ruleCount = ([regex]::Matches($unshippedBody, '(?m)^\s*AVIG\d{4}\s*\|')).Count
    Write-Host "      moving $ruleCount rule(s) to Shipped under '## Release $next'" -ForegroundColor Green
} else {
    Write-Host "      nothing unshipped, Shipped left alone" -ForegroundColor Yellow
}

if (-not $DryRun) {
    $updatedCsproj = ([regex]'<Version>[^<]*</Version>').Replace($csproj, "<Version>$next</Version>", 1)
    Save-Text $project $updatedCsproj

    if ($hasRules) {
        $shippedText = (Get-Content $shipped -Raw).TrimEnd("`r", "`n")
        $section = $unshippedBody.Trim("`r", "`n")
        Save-Text $shipped "$shippedText`r`n`r`n## Release $next`r`n`r`n$section`r`n"

        $header = ($unshippedText -split "`r?`n" | Where-Object { $_ -match '^\s*;' }) -join "`r`n"
        Save-Text $unshipped "$header`r`n"
    }
}

# --- [4/6] Prove the release is buildable before it becomes a tag ---
Write-Host "[4/6] Verifying the package..." -ForegroundColor Yellow

if ($DryRun) {
    Write-Host "      skipped in a dry run" -ForegroundColor Yellow
} else {
    # No pipeline wrapper: publish.ps1 reports through Write-Host, so its output goes straight
    # to the host and nothing would arrive here to indent.
    & $publish -NoBump
    if ($LASTEXITCODE -ne 0) {
        Write-Host "      publish.ps1 failed - restoring the working tree, no tag created." -ForegroundColor Red
        git -C $root checkout -- $project $shipped $unshipped
        exit 1
    }

    Write-Host "      tests, pack and shape check passed" -ForegroundColor Green
}

# --- [5/6] Commit and tag ---
Write-Host "[5/6] Commit and tag" -ForegroundColor Yellow

$message = "Release $tag"
if ($hasRules) {
    $message = "$message`n`nClose the release-tracking entry: the unshipped rules move to Shipped under Release $next."
}

if ($DryRun) {
    Write-Host "      would commit '$message' and tag $tag" -ForegroundColor Yellow
} else {
    git -C $root add -- $project $shipped $unshipped
    git -C $root commit --quiet -m $message
    if ($LASTEXITCODE -ne 0) {
        Fail "Commit failed."
    }

    git -C $root tag -a $tag -m "Avig.Analyzers $next"
    if ($LASTEXITCODE -ne 0) {
        Fail "Tagging failed. The release commit is still there; remove it or retag by hand."
    }

    Write-Host "      committed and tagged $tag" -ForegroundColor Green
}

# --- [6/6] The push is what publishes ---
Write-Host "[6/6] Push" -ForegroundColor Yellow

if ($DryRun) {
    Write-Host "      would push $Branch and $tag" -ForegroundColor Yellow
} elseif ($Push) {
    git -C $root push origin $Branch
    if ($LASTEXITCODE -ne 0) {
        Fail "Pushing $Branch failed. The tag is still local; nothing published."
    }

    git -C $root push origin $tag
    if ($LASTEXITCODE -ne 0) {
        Fail "Pushing $tag failed. Nothing published."
    }

    Write-Host "      pushed - the publish workflow is starting" -ForegroundColor Green
} else {
    Write-Host "      not pushed (pass -Push to do it here)" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=== $tag prepared ===" -ForegroundColor Cyan
Write-Host ""

if ($DryRun) {
    Write-Host "Nothing was changed. Run it again without -DryRun." -ForegroundColor Cyan
} elseif ($Push) {
    Write-Host "Watch it publish:" -ForegroundColor Cyan
    Write-Host "    https://github.com/marcuslindh/Avig.Analyzers/actions" -ForegroundColor Gray
} else {
    Write-Host "Nothing has left this machine yet. To publish:" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "    git push origin $Branch && git push origin $tag" -ForegroundColor Gray
    Write-Host ""
    Write-Host "To undo instead:" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "    git tag -d $tag; git reset --hard HEAD~1" -ForegroundColor Gray
}

Write-Host ""
