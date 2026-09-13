# Avig.Analyzers

Public NuGet package with eighteen project-independent Roslyn rules in the range
AVIG0001–AVIG0023. The first twelve were extracted from `Medusa.Analyzers` (the Medusa
repository) — the ones that do not require Medusa's own types. AVIG0014, AVIG0015 and
AVIG0020–AVIG0023 were written here.

Everything in this repository is written in English: documentation, comments, commit
messages and script output alike.

## Layout

| Path | What |
|---|---|
| `src/Avig.Analyzers/` | analyzers + code fixes, `netstandard2.0` |
| `tests/Avig.Analyzers.Tests/` | xUnit + `Microsoft.CodeAnalysis.*.Testing`, `net10.0` |
| `README.md` | both the repo readme and the `PackageReadmeFile` — one file, no drift |
| `publish.ps1` | version, test, pack, verify package shape. Never pushes. |

## Rules that apply when you change things here

**`netstandard2.0` in the analyzer project is not negotiable.** The analyzer is loaded into
the compiler process, which runs on .NET Framework under Visual Studio, and NuGet places the
DLL at `analyzers/dotnet/cs` — a path with no TFM dimension. A `net10.0` analyzer is silently
never loaded at all. The consumer's project is welcome to be net10.0; that is a different
thing.

**New rules take the next free number and must be added to `AnalyzerReleases.Unshipped.md`,**
otherwise RS2008 fails the build. Exception: AVIG0013 and AVIG0016–AVIG0019 are reserved for
proposed but unbuilt rules and must not be reused — a diagnostic id is permanent. The next
free number is therefore AVIG0024. `Category` and `Severity` in the table have to match the
`DiagnosticDescriptor` exactly (RS2001/RS2002).

**The default severity is `Info`, and AVIG0006 is `isEnabledByDefault: false`.** That is
deliberate: a public package that starts alarming the moment it is installed gets
uninstalled. The consumer raises severities themselves through `.editorconfig`.

`Warning` is the exception, and it has a criterion: a rule earns it only when `Info` would
defeat the rule's own purpose, because a suggestion never reaches `dotnet build` output. Four
rules meet it. Three fail **silently in production** and never produce a compile error, so the
build log is the only place the problem can surface — AVIG0010 (a misspelled role string
silently denies or grants access), AVIG0014 (a blocking call on a task only deadlocks under
load) and AVIG0015 (a dropped token makes the work uncancellable). AVIG0023 meets it from the
other direction: the rule exists to stop a note being forgotten, and a marker visible only in
one developer's IDE is already forgotten. No further rule may become `Warning` without an
argument of that kind;
`RuleContractTests.Only_the_silent_failure_rules_warn_out_of_the_box` fails the change
otherwise. Do not change this without also changing the README,
`avig-analyzers.editorconfig` and `AnalyzerReleases.Shipped.md`.

**Every rule has a `## AVIGxxxx` section in the README with before/after code examples,** and
`helpLinkUri` points at that anchor through `AvigRuleDocs.For(...)`. Add a rule without a
README section and its help link is a 404.

**`Microsoft.CodeAnalysis.CSharp.Workspaces` stays at 4.11.0.** An analyzer must reference a
Roslyn version equal to or older than the consumer's compiler. Bumping it locks out everyone
not on the latest SDK.

**The repository runs its own rules on itself, all at `error`.** `.editorconfig` sets every
AVIG rule to error and `Directory.Build.props` sets `TreatWarningsAsErrors`, which is the
setup the README recommends to consumers. The analyzer is loaded through the
`OutputItemType="Analyzer"` reference in `Avig.Analyzers.Tests.csproj` — it cannot be wired
into the analyzer project itself, because csc would load the DLL that build is about to
overwrite. A rule that is unbearable to live with therefore shows up here before it reaches
anyone else.

**AVIG0007 is the one rule turned off for this repository,** and the reason is in the
`.editorconfig` next to it: all twelve hits were private syntax-tree helpers returning null
for "not applicable", not for "an error occurred". That follows the README's own advice to
turn down the rule rather than write the same suppression twelve times. If AVIG0007 is ever
narrowed to externally visible members, delete that line and let it run.

**Releases go out from `.github/workflows/publish.yml` when a `v*` tag is pushed,** through
NuGet Trusted Publishing — no API key anywhere. Two things about that file are load-bearing.
Its **file name** is part of the policy registered on nuget.org, so renaming it silently breaks
publishing until the policy is changed to match. And the `NuGet/login` step has to stay
**last**, immediately before the push: the key it returns is valid for one hour, and a job that
requests it early can find it expired by the time it pushes. The job also refuses to publish
when the tag and `<Version>` in the csproj disagree.

**Never package into `lib/`.** `IncludeBuildOutput=false` plus the `PackAnalyzerAssembly`
target is what makes this package an analyzer rather than an ordinary library. `publish.ps1`
checks for it and aborts — that check must not be removed.

## Building and releasing

```powershell
dotnet build Avig.Analyzers.slnx -c Release
dotnet test Avig.Analyzers.slnx -c Release
./publish.ps1                    # bumps patch, tests, packs, verifies
./publish.ps1 -NoBump            # the same without a version bump
```

The script never pushes, and that has not changed. What publishes is the tag: commit the
version and the release-tracking move, then `git tag vX.Y.Z` and push it. The workflow runs
`publish.ps1 -NoBump` on a windows-latest runner - the script uses Windows path separators, and
the repository enforces CRLF - and pushes the nupkg it produced.
