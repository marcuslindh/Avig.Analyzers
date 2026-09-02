# Avig.Analyzers

Tolv Roslyn-regler som fungerar i vilket C#-projekt som helst — formatering, namngivning,
dokumentationskrav och auth-hygien. AVIG0001–AVIG0012, fem med code fix.

Ingen av reglerna kräver ett visst ramverk, en viss arkitektur eller en viss basklass. De
som luktar ASP.NET matchar på typnamn, inte på ASP.NET-symboler, så de fungerar även i ett
projekt som rullar sin egen controller-bas.

## Installera

```bash
dotnet add package Avig.Analyzers
```

Paketet är en `DevelopmentDependency`: reglerna körs vid kompilering och följer inte med
vidare till dem som refererar ditt paket.

## Reglerna är avstängda tills du säger till

Elva av tolv regler ligger på `suggestion` som standard, och AVIG0006 är helt avstängd. En
nyinstallation kan alltså inte bryta ditt bygge — inte ens med `TreatWarningsAsErrors`.
Undantaget är AVIG0010, som är `warning` direkt: en felstavad rollsträng ger tyst nekad
eller tyst beviljad åtkomst och kostar ingenting att åtgärda.

Att välja severity är ditt beslut, inte paketets. Kopiera `avig-analyzers.editorconfig`
från paketets rot in i din egen `.editorconfig` och stryk det du inte vill ha:

```ini
[*.cs]
dotnet_diagnostic.AVIG0001.severity = warning
dotnet_diagnostic.AVIG0007.severity = none
```

Förslag på införande: börja på `suggestion`, höj till `warning` när koden är ren, och till
`error` först när du vill att den ska förbli det.

En sak som förvånar: `suggestion` syns i IDE:t och för `dotnet format`, men **inte** i
utdata från `dotnet build`. Tycker du att paketet inte gör någonting efter installationen
är det förmodligen därför — höj en regel till `warning` så syns den.

## Översikt

| Regel | Kategori | Standard | Code fix | Vad den fångar |
|---|---|---|---|---|
| [AVIG0001](#avig0001) | Formatting | suggestion | ✔ | Blankrad saknas efter avslutande klammer |
| [AVIG0002](#avig0002) | Naming | suggestion | | Enteckensnamn på identifierare |
| [AVIG0003](#avig0003) | Formatting | suggestion | ✔ | Blankrad saknas före `return` |
| [AVIG0004](#avig0004) | Formatting | suggestion | ✔ | Blankrad saknas före `try` |
| [AVIG0005](#avig0005) | Formatting | suggestion | ✔ | Blankrad saknas före `while` |
| [AVIG0006](#avig0006) | Formatting | **av** | ✔ | Radslut som inte är CRLF |
| [AVIG0007](#avig0007) | Design | suggestion | | Metod returnerar null vid fel |
| [AVIG0008](#avig0008) | Design | suggestion | | `I*Repository`-metod utan `CancellationToken` |
| [AVIG0009](#avig0009) | Documentation | suggestion | | Publik controller eller repository-interface utan `<summary>` |
| [AVIG0010](#avig0010) | Design | **warning** | | Roll-, scope- eller claimnamn som strängliteral |
| [AVIG0011](#avig0011) | Documentation | suggestion | | `<param>`/`<returns>` utan `<summary>` |
| [AVIG0012](#avig0012) | Security | suggestion | | Controller-action utan explicit auth-beslut |

De fem med code fix går att rätta i klump:

```bash
dotnet format analyzers --diagnostics AVIG0001 AVIG0003 AVIG0004 AVIG0005 --severity info
```

---

## AVIG0001

**Blankrad efter avslutande klammer**

Ett block som slutar och en sats som börjar på raden direkt under läses som ett stycke,
fast det är två.

```csharp
// ⚠ AVIG0001
if (user is null)
{
    return NotFound();
}
logger.LogInformation("Hittade {Id}", user.Id);

// ✔
if (user is null)
{
    return NotFound();
}

logger.LogInformation("Hittade {Id}", user.Id);
```

Klamrar mitt i en konstruktion är undantagna. `try { }` följt av `catch`, och `catch { }`
följt av ännu ett `catch` eller `finally`, larmar aldrig — där skulle en blankrad bryta
isär något som hänger ihop:

```csharp
// ✔ — ingen av de här klamrarna larmar
try
{
    Send(payload);
}
catch (HttpRequestException)
{
    Retry();
}
finally
{
    stream.Dispose();
}
```

Preprocessordirektiv räknas som blankrad, eftersom de äter sin egen radbrytning.

---

## AVIG0002

**Enteckensnamn**

`d` betyder ingenting när du läser koden två månader senare, och det går inte att söka
efter.

```csharp
// ⚠ AVIG0002 — u, d
foreach (var u in users)
{
    var d = u.CreatedAt;
}

// ✔
foreach (var user in users)
{
    var createdAt = user.CreatedAt;
}
```

Regeln träffar lokala variabler, parametrar, `foreach`-variabler, catch-deklarationer och
out-variabler. Discarden `_` är undantagen:

```csharp
// ✔
if (cache.TryGetValue(key, out _))
{
    return true;
}
```

---

## AVIG0003

**Blankrad före return**

```csharp
// ⚠ AVIG0003
public int Sum(int[] values)
{
    int total = 0;
    foreach (int value in values)
    {
        total += value;
    }
    return total;
}

// ✔
public int Sum(int[] values)
{
    int total = 0;
    foreach (int value in values)
    {
        total += value;
    }

    return total;
}
```

Guard clauses är undantagna. En `return` som ligger direkt i ett `if`-block larmar aldrig
— att kräva en blankrad i ett tvåradersblock skulle göra vakten svårare att läsa, inte
lättare:

```csharp
// ✔
if (values.Length == 0)
{
    logger.LogDebug("Tom indata");
    return 0;
}
```

Detsamma gäller när `return` är blockets första sats.

---

## AVIG0004

**Blankrad före try**

```csharp
// ⚠ AVIG0004
var payload = BuildPayload(order);
try
{
    await client.SendAsync(payload, cancellationToken);
}
catch (HttpRequestException exception)
{
    logger.LogWarning(exception, "Leverans misslyckades");
}

// ✔
var payload = BuildPayload(order);

try
{
    await client.SendAsync(payload, cancellationToken);
}
catch (HttpRequestException exception)
{
    logger.LogWarning(exception, "Leverans misslyckades");
}
```

Ett `try` som är blockets första sats larmar inte.

---

## AVIG0005

**Blankrad före while**

```csharp
// ⚠ AVIG0005
int remaining = queue.Count;
while (remaining > 0)
{
    remaining--;
}

// ✔
int remaining = queue.Count;

while (remaining > 0)
{
    remaining--;
}
```

`do { … } while (…);` träffas aldrig — det avslutande `while` är en annan nod i
syntaxträdet, och en blankrad där vore ett syntaxfel.

---

## AVIG0006

**CRLF-radslut**

**Avstängd som standard.** I ett LF-repo skulle varenda fil larma på varenda rad. Kolla
`.gitattributes` innan du slår på den:

```ini
dotnet_diagnostic.AVIG0006.severity = error
```

Regeln kräver att varje radslut är `\r\n`. Den tittar bara på radslutstrivia, aldrig på
innehållet i verbatim- eller raw string-literaler, så en literal som medvetet innehåller
`\n` skrivs aldrig om:

```csharp
// ✔ — den här LF:en rörs inte, den är data och inte formatering
const string Payload = "first\nsecond";
```

Code fix finns.

---

## AVIG0007

**Metod returnerar null vid fel**

En metod som ger ett värde när allt gick bra och `null` när det inte gjorde det använder
null som felsignal. Felet försvinner ur signaturen, och varje anropare måste minnas
nullkontrollen på egen hand.

```csharp
// ⚠ AVIG0007 — null på en väg, värde på en annan
public Order? Parse(string raw)
{
    if (string.IsNullOrWhiteSpace(raw))
    {
        return null;
    }

    return new Order(raw);
}

// ✔ — felet syns i typen
public Result<Order> Parse(string raw)
{
    if (string.IsNullOrWhiteSpace(raw))
    {
        return Result<Order>.Failure("Tom indata");
    }

    return Result<Order>.Success(new Order(raw));
}
```

Regeln är namnbaserad och neutral: returnerar metoden redan en typ som heter `Result`
eller `Option` — vems som helst, i vilken namnrymd som helst — hoppas den över. Paketet
har ingen åsikt om vilket resultatmönster du använder, bara att du använder ett.

Metoder som *bara* returnerar null, eller *bara* returnerar värden, larmar heller inte.
Det är blandningen som är signalen. Ternärer och switch-uttryck genomsöks; lambdas och
lokala funktioner bedöms för sig.

---

## AVIG0008

**Repository-metod utan CancellationToken**

```csharp
// ⚠ AVIG0008
public interface IOrderRepository
{
    Task<Order?> GetAsync(int id);
    Task<int> SaveAsync(Order order);
}

// ✔
public interface IOrderRepository
{
    Task<Order?> GetAsync(int id, CancellationToken cancellationToken);
    Task<int> SaveAsync(Order order, CancellationToken cancellationToken);
}
```

En token som trådas genom hela anropsstacken och sedan tappas vid databasgränsen köper
ingenting: ett avbrutet anrop kör sitt arbete klart ändå. Att ta token som sista parameter
är konventionen både BCL och EF Core följer, och den ger CA2016 och MA0040 något att
faktiskt kontrollera — utan den har de ingen metod att vidarebefordra till.

Träffar interface som heter `I…Repository` och vars medlemmar returnerar `Task` eller
`ValueTask`. Synkrona medlemmar och interface med andra namn lämnas i fred.

---

## AVIG0009

**Publik controller eller repository-interface utan XML-summary**

```csharp
// ⚠ AVIG0009
public class OrdersController : ControllerBase { }

public interface IOrderRepository { }

// ✔
/// <summary>Läser och lägger beställningar.</summary>
public class OrdersController : ControllerBase { }

/// <summary>Lagring av <see cref="Order"/>.</summary>
public interface IOrderRepository { }
```

CS1591 kan inte göra det här. Den kräver `GenerateDocumentationFile`, och då krävs doc på
varenda publik medlem — inklusive varje DTO-property. Den här regeln träffar bara de två
kategorier där kontraktet faktiskt är någon annans problem.

Har du egna markörinterface som också bör dokumenteras kan du lista dem:

```ini
dotnet_code_quality.AVIG0009.additional_documented_interfaces = IAppCommand,IAppQuery
```

Icke-publika typer larmar aldrig.

---

## AVIG0010

**Roll-, scope- eller claimnamn som strängliteral**

Enda regeln som är `warning` direkt ur lådan. En felstavad rollsträng ger tyst nekad eller
tyst beviljad åtkomst — aldrig ett kompileringsfel.

```csharp
// ⚠ AVIG0010
if (User.IsInRole("Administrator"))
{
    return Ok(secret);
}

[Authorize(Policy = "CanEditOrders")]
public IActionResult Edit(int id) => View();

// ✔
public static class RoleNames
{
    public const string Administrator = "Administrator";
}

public static class PolicyNames
{
    public const string CanEditOrders = "CanEditOrders";
}

if (User.IsInRole(RoleNames.Administrator))
{
    return Ok(secret);
}

[Authorize(Policy = PolicyNames.CanEditOrders)]
public IActionResult Edit(int id) => View();
```

Träffar strängliteraler till `IsInRole`, `HasClaim`, `FindFirst`, `FindFirstValue`,
`FindAll`, `RequireRole`, `RequireClaim`, `RequireScope` och `HasScope`, samt `Policy`,
`Roles` och `AuthenticationSchemes` på `[Authorize]`. Ett `[Authorize]` utan argument
larmar inte.

---

## AVIG0011

**Doc-kommentar med `<param>` men utan `<summary>`**

```csharp
// ⚠ AVIG0011
/// <param name="id">Beställningens id.</param>
/// <returns>Beställningen, eller null.</returns>
public Order? Get(int id) => repository.Find(id);

// ✔
/// <summary>Läser en enskild beställning.</summary>
/// <param name="id">Beställningens id.</param>
/// <returns>Beställningen, eller null när den inte finns.</returns>
public Order? Get(int id) => repository.Find(id);
```

En kommentar som bara beskriver delarna säger aldrig vad medlemmen är till för. Regeln
frågar aldrig efter dokumentation som inte finns — den fäller bara när någon redan börjat
dokumentera.

`<inheritdoc/>` räknas som dokumenterad. Kontrollen är dessutom scopad per token, så en
typs `<summary>` tillgodoräknas aldrig dess medlemmar:

```csharp
// ⚠ AVIG0011 — klassens summary gäller klassen, inte metoden
/// <summary>Lagring av beställningar.</summary>
public class Repository
{
    /// <param name="id">Beställningens id.</param>
    public Order? Get(int id) => Find(id);
}
```

Regeln fungerar oavsett om projektet sätter `GenerateDocumentationFile` eller inte.
Ersätter Roslynator RCS1139, som bara finns i VS-tillägget och inte i NuGet-paketet.

---

## AVIG0012

**Controller-action utan explicit auth-beslut**

```csharp
// ⚠ AVIG0012 — ingen har bestämt någonting
[HttpGet("orders/{id}")]
public IActionResult Get(int id) => Ok(repository.Find(id));

// ✔ — skyddad
[Authorize]
[HttpGet("orders/{id}")]
public IActionResult Get(int id) => Ok(repository.Find(id));

// ✔ — medvetet öppen
[AllowAnonymous]
[HttpGet("health")]
public IActionResult Health() => Ok();
```

Regeln kräver inte auktorisering. Den kräver att beslutet syns vid signaturen, så att
"anonym" är något en granskare kan *se* i stället för att härleda ur en frånvaro.

**Åtgärden är inte alltid `[AllowAnonymous]`.** Har din host ett globalt `AuthorizeFilter`
är en oannoterad action *stängd*, och `[AllowAnonymous]` skulle öppna den. Saknas filtret
är den öppen. Regeln rapporterar båda fallen — att läsaren inte kan skilja dem åt är precis
invändningen — men vilken fix som är rätt beror på hosten.

Ett `[Authorize]` på klassen eller på en basklass räknas för alla actions i den:

```csharp
// ✔ — inget larm på Get
[Authorize]
public abstract class SecuredController : ControllerBase { }

public class OrdersController : SecuredController
{
    [HttpGet("orders/{id}")]
    public IActionResult Get(int id) => Ok();
}
```

Egna auth-attribut som implementerar `IAuthorizationFilter` eller
`IAsyncAuthorizationFilter` räknas automatiskt. Övriga kan listas:

```ini
dotnet_code_quality.AVIG0012.additional_auth_attributes = HmacAuthenticated,InternalOnly
```

Auktorisering inuti metodkroppen är osynlig här och rapporteras ändå. Det är med flit: en
vakt som bara finns i kroppen skyddar inte nästa action någon lägger till i samma
controller.

---

## Varför netstandard2.0 och inte net10.0

Analysator-assemblyn är byggd för `netstandard2.0`. Det är inte eftersläpning.

Roslyn laddar analysatorn in i *kompilatorprocessen* (`csc` / `VBCSCompiler`), inte i din
app, och den processen kör på .NET Framework när Visual Studio är värden. NuGet lägger
dessutom analysator-DLL:en på `analyzers/dotnet/cs` — en sökväg helt utan TFM-dimension, så
det finns ingen mekanism att skicka en `net10.0`-DLL till vissa konsumenter och en annan
till resten.

Ditt projekt får gärna vara `net10.0`. Det har ingenting med den här raden att göra.

Paketet refererar Roslyn 4.11.0, vilket når ner till SDK 8.0.4xx och VS 17.11. En analysator
måste referera en Roslyn-version som är lika med eller äldre än konsumentens kompilator, så
en bumpning hade låst ute alla som inte kör senaste SDK.

## Kommer från Medusa.Analyzers

Reglerna är utbrutna ur ett internt analyzerprojekt. De elva som krävde det projektets egna
typer — Result-mönstret, CQRS-konventioner — följde inte med. Migrerar du en `.editorconfig`
är det här mappningen:

| Avig.Analyzers | Medusa.Analyzers |
|---|---|
| AVIG0001 | MEDUSA0001 |
| AVIG0002 | MEDUSA0002 |
| AVIG0003 | MEDUSA0006 |
| AVIG0004 | MEDUSA0007 |
| AVIG0005 | MEDUSA0008 |
| AVIG0006 | MEDUSA0009 |
| AVIG0007 | MEDUSA0010 |
| AVIG0008 | MEDUSA0016 |
| AVIG0009 | MEDUSA0018 |
| AVIG0010 | MEDUSA0021 |
| AVIG0011 | MEDUSA0026 |
| AVIG0012 | MEDUSA0028 |

Tre saker skiljer utöver numreringen: AVIG0007 letar inte längre efter en specifik
`Result`-namnrymd, AVIG0009 tappade sina Command-, Query- och Handler-kategorier till förmån
för `additional_documented_interfaces`, och alla defaults sänktes från `warning` till
`suggestion`.

## Bygga och släppa

```powershell
dotnet build Avig.Analyzers.slnx -c Release
dotnet test Avig.Analyzers.slnx -c Release
./publish.ps1                    # bumpar patch, testar, packar, formkontrollerar
./publish.ps1 -NoBump            # samma utan versionsbump
```

`publish.ps1` pushar aldrig själv. Det är med flit — publicering till nuget.org är ett
manuellt steg, och API-nyckeln ska aldrig hamna i repot.

## Licens

MIT. Se [LICENSE](LICENSE).
