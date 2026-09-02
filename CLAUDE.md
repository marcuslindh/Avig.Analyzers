# Avig.Analyzers

Publikt NuGet-paket med tolv projektoberoende Roslyn-regler, AVIG0001–AVIG0012.
Reglerna är utbrutna ur `Medusa.Analyzers` (Medusa-repot) — de som inte kräver
Medusas egna typer.

## Layout

| Sökväg | Vad |
|---|---|
| `src/Avig.Analyzers/` | analysatorer + code fixes, `netstandard2.0` |
| `tests/Avig.Analyzers.Tests/` | xUnit + `Microsoft.CodeAnalysis.*.Testing`, `net10.0` |
| `README.md` | både repo-readme och `PackageReadmeFile` — en fil, ingen drift |
| `publish.ps1` | version, test, pack, formkontroll av nupkg. Pushar aldrig. |

## Regler som gäller när du ändrar här

**`netstandard2.0` i analyzerprojektet är inte förhandlingsbart.** Analysatorn laddas
in i kompilatorprocessen, som kör på .NET Framework i Visual Studio, och NuGet lägger
DLL:en på `analyzers/dotnet/cs` — en sökväg utan TFM-dimension. En `net10.0`-analyzer
laddas tyst inte alls. Konsumentens projekt får gärna vara net10.0; det är en annan sak.

**Nya regler tar nästa lediga nummer och måste in i `AnalyzerReleases.Unshipped.md`,**
annars fäller RS2008 bygget. `Category` och `Severity` i tabellen måste matcha
`DiagnosticDescriptor` exakt (RS2001/RS2002).

**Default-severity är `Info` för allt utom AVIG0010 (`Warning`), och AVIG0006 är
`isEnabledByDefault: false`.** Det är medvetet: ett publikt paket som börjar larma
direkt vid installation blir avinstallerat. Konsumenten höjer själv via `.editorconfig`.
Ändra inte detta utan att också ändra README och `AnalyzerReleases.Shipped.md`.

**Varje regel har en `## AVIGxxxx`-sektion i README med före/efter-kodexempel,** och
`helpLinkUri` pekar på det ankaret via `AvigRuleDocs.For(...)`. Lägger du till en regel
utan README-sektion blir hjälplänken en 404.

**`Microsoft.CodeAnalysis.CSharp.Workspaces` ska stanna på 4.11.0.** En analysator måste
referera en Roslyn-version som är lika med eller äldre än konsumentens kompilator.
Att bumpa låser ute alla som inte kör senaste SDK.

**Paketera aldrig till `lib/`.** `IncludeBuildOutput=false` + `PackAnalyzerAssembly`-targetet
är det som gör paketet till en analysator i stället för ett vanligt bibliotek.
`publish.ps1` kontrollerar detta och avbryter — den kontrollen ska inte tas bort.

## Bygga och släppa

```powershell
dotnet build Avig.Analyzers.slnx -c Release
dotnet test Avig.Analyzers.slnx -c Release
./publish.ps1                    # bumpar patch, testar, packar, verifierar
./publish.ps1 -NoBump            # samma utan versionsbump
```

Publicering till nuget.org är manuell och sker aldrig från skriptet.
