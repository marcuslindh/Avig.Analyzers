# Avig.Analyzers

Eighteen Roslyn rules that work in any C# project — formatting, naming, documentation
requirements, async hygiene and auth hygiene. AVIG0001–AVIG0023, seven with a code fix.

## Why this exists

Most of the code in my projects is written by an AI agent now, and that moved the bottleneck.
Producing a working implementation is cheap; reading it closely enough to trust it is not. A
model will happily emit forty statements with no blank line between them, name a variable `d`,
block on `.Result`, and build the log message with string interpolation. It compiles, it passes
the tests, and it is exhausting to review at the speed it arrives.

**A convention in a prompt is advice; a diagnostic is feedback.** A style guide or a
`CLAUDE.md` gets read, agreed with, and drifted away from three files later, and nothing says
when. A rule in the build output is different: the agent runs `dotnet build`, sees `AVIG0014`,
and fixes it before handing the work over. Every rule here started as a review comment I got
tired of writing twice.

That is why blank-line rules sit next to deadlock rules. The formatting ones cost the writer
nothing — and generated code least of all, since nobody typed it — and buy a diff you can skim.
The rest are the mistakes models make by default, because the wrong version is the shorter one
to write.

None of it replaces reading the code; it removes the layer a machine should have handled. And
nothing in these rules knows who typed the code — they work the same on what you wrote
yourself.

## Install

```bash
dotnet add package Avig.Analyzers
```

The package is a `DevelopmentDependency`: the rules run at compile time and do not flow on
to whoever references your package.

## The rules stay quiet until you say otherwise

Thirteen of the eighteen rules default to `suggestion`, and AVIG0006 is disabled entirely.
A fresh install cannot break your build — not even with `TreatWarningsAsErrors`.

Four rules are `warning` out of the box: AVIG0010, AVIG0014, AVIG0015 and AVIG0023. A rule
earns that only when a `suggestion` would defeat its purpose. The first three catch something
that fails *silently in production* and never produces a compile error — a misspelled role
string silently denies or silently grants access, a blocking call on a task only deadlocks
under load, and a dropped token means the work keeps running after the caller gave up.
AVIG0023 is there for the mirror-image reason: it exists to stop a note being forgotten, and a
marker that only ever appears in the IDE of whoever opens that file has already been
forgotten.

Choosing severities is your decision, not the package's. Copy `avig-analyzers.editorconfig`
from the package root into your own `.editorconfig` and delete what you do not want:

```ini
[*.cs]
dotnet_diagnostic.AVIG0001.severity = warning
dotnet_diagnostic.AVIG0007.severity = none
```

One thing that surprises people: `suggestion` shows up in the IDE and to `dotnet format`, but
**not** in `dotnet build` output. If the package appears to do nothing after installation,
that is probably why — raise one rule to `warning` and it becomes visible.

Those are the defaults a stranger gets. What I actually run is the other end of the scale.

## Recommended setup

**Every rule at `error`, and `TreatWarningsAsErrors` in every project.** That is what I use,
and it follows directly from the section above: a rule that does not fail the build is a rule
an agent does not have to fix.

A warning is advisory. It scrolls past in the build log, it accumulates, and both people and
models learn to read "builds with 40 warnings" as success. An error cannot be deferred — it is
a precondition for the work being finished at all. That is the whole difference between a
convention I hope for and a convention I get. It also ends the negotiation at the call site:
there is nothing to weigh up, because the code does not build until the rule is satisfied.

In your `.editorconfig`:

```ini
[*.cs]

# Formatting
dotnet_diagnostic.AVIG0001.severity = error
dotnet_diagnostic.AVIG0003.severity = error
dotnet_diagnostic.AVIG0004.severity = error
dotnet_diagnostic.AVIG0005.severity = error
dotnet_diagnostic.AVIG0006.severity = error   # only in a CRLF repository, see below

# Naming and documentation
dotnet_diagnostic.AVIG0002.severity = error
dotnet_diagnostic.AVIG0009.severity = error
dotnet_diagnostic.AVIG0011.severity = error

# Design
dotnet_diagnostic.AVIG0007.severity = error
dotnet_diagnostic.AVIG0008.severity = error
dotnet_diagnostic.AVIG0014.severity = error
dotnet_diagnostic.AVIG0015.severity = error
dotnet_diagnostic.AVIG0020.severity = error
dotnet_diagnostic.AVIG0021.severity = error
dotnet_diagnostic.AVIG0022.severity = error

# Security
dotnet_diagnostic.AVIG0010.severity = error
dotnet_diagnostic.AVIG0012.severity = error

# Maintainability
dotnet_diagnostic.AVIG0023.severity = error
```

AVIG0006 belongs in that list only if the repository is genuinely CRLF — check
`.gitattributes` first, or every file errors on every line.

Then once at the repository root, in a `Directory.Build.props` every project inherits:

```xml
<Project>
  <PropertyGroup>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

The two settings do different jobs. The `.editorconfig` lines put *these* rules at error.
`TreatWarningsAsErrors` covers everything else — compiler warnings, the CA analyzers, nullable
warnings — which is a larger commitment than adopting this package, and worth taking on
deliberately rather than as a side effect.

### Getting there on an existing codebase

Do not flip all seventeen to error on a repository with history. You get thousands of errors,
you do not read them, and you turn the package off. In this order instead:

1. **Run the formatting family through `dotnet format` in its own commit**, with the rules
   still at `suggestion`:

   ```bash
   dotnet format analyzers --diagnostics AVIG0001 AVIG0003 AVIG0004 AVIG0005 --severity info
   ```

   It touches a lot of lines and no behaviour, so it must not be mixed with anything else in
   review. Then raise those four to `error`; from that point they never come back.

2. **Take AVIG0020 and AVIG0022 next, in the IDE, one file at a time.** Their code fixes change
   signatures and behaviour, so they want a human looking at each one.

3. **Raise the rest one rule at a time**, fixing as you go. One rule per commit keeps both the
   diff and the blame readable, and tells you quickly which rule is a poor fit for this
   codebase.

On a new project there is nothing to migrate: set all of them to error on day one and stop
thinking about it.

### When a rule is wrong

With everything at error you need a legitimate way out, or the first painful call site becomes
an argument for disabling the rule everywhere. Suppress it at the site, with the reason:

```csharp
// The task is already completed here — this is the IsCompleted fast path.
#pragma warning disable AVIG0014
int value = task.Result;
#pragma warning restore AVIG0014
```

One justified line at the call site is a far better outcome than
`dotnet_diagnostic.AVIG0014.severity = none` at the top of the repository: the exception stays
visible, reviewable and local. If you find yourself writing the same suppression a fourth time,
that is the rule telling you something — either the code has a pattern worth extracting, or
that one rule does not belong in this codebase. Turn down the rule, not the set.

## Overview

| Rule | Category | Default | Code fix | What it catches |
|---|---|---|---|---|
| [AVIG0001](#avig0001) | Formatting | suggestion | ✔ | Missing blank line after a closing brace |
| [AVIG0002](#avig0002) | Naming | suggestion | | Single-character identifier |
| [AVIG0003](#avig0003) | Formatting | suggestion | ✔ | Missing blank line before `return` |
| [AVIG0004](#avig0004) | Formatting | suggestion | ✔ | Missing blank line before `try` |
| [AVIG0005](#avig0005) | Formatting | suggestion | ✔ | Missing blank line before `while` |
| [AVIG0006](#avig0006) | Formatting | **off** | ✔ | Line ending that is not CRLF |
| [AVIG0007](#avig0007) | Design | suggestion | | Method returns null to signal failure |
| [AVIG0008](#avig0008) | Design | suggestion | | `I*Repository` method without a `CancellationToken` |
| [AVIG0009](#avig0009) | Documentation | suggestion | | Public controller or repository interface without `<summary>` |
| [AVIG0010](#avig0010) | Design | **warning** | | Role, scope or claim name written as a string literal |
| [AVIG0011](#avig0011) | Documentation | suggestion | | `<param>`/`<returns>` without `<summary>` |
| [AVIG0012](#avig0012) | Security | suggestion | | Controller action without an explicit auth decision |
| [AVIG0014](#avig0014) | Design | **warning** | | `.Result`, `.Wait()`, `GetAwaiter().GetResult()` |
| [AVIG0015](#avig0015) | Design | **warning** | | Awaited call drops a `CancellationToken` that is in scope |
| [AVIG0020](#avig0020) | Design | suggestion | ✔ | `DateTime.Now`, `DateTime.Today`, `DateTimeOffset.Now` |
| [AVIG0021](#avig0021) | Design | suggestion | | Log message built instead of templated |
| [AVIG0022](#avig0022) | Design | suggestion | ✔ | Public member hands out a mutable collection |
| [AVIG0023](#avig0023) | Maintainability | **warning** | | TODO comment without a tracking reference |

The numbering has gaps. AVIG0013 and AVIG0016–AVIG0019 are reserved for rules that were
proposed but not built; a diagnostic id is permanent, so they are not reused.

Seven rules have code fixes. The formatting family can be applied in bulk with `dotnet format`;
AVIG0020 and AVIG0022 change signatures and behaviour, so they want a human looking at each one.
[Recommended setup](#recommended-setup) has the order to adopt them in.

---

## AVIG0001

**Blank line after a closing brace**

A block that ends and a statement that starts on the very next line read as one paragraph,
when they are two.

```csharp
// ⚠ AVIG0001
if (user is null)
{
    return NotFound();
}
logger.LogInformation("Found {Id}", user.Id);

// ✔
if (user is null)
{
    return NotFound();
}

logger.LogInformation("Found {Id}", user.Id);
```

Braces in the middle of a construct are exempt. `try { }` followed by `catch`, and `catch { }`
followed by another `catch` or `finally`, never report — a blank line there would pull apart
something that belongs together:

```csharp
// ✔ — none of these braces report
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

Preprocessor directives count as a blank line, since they consume their own line break.

---

## AVIG0002

**Single-character identifier**

`d` means nothing when you read the code two months later, and it cannot be searched for.

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

The rule covers local variables, parameters, `foreach` variables, catch declarations and out
variables. The discard `_` is exempt:

```csharp
// ✔
if (cache.TryGetValue(key, out _))
{
    return true;
}
```

---

## AVIG0003

**Blank line before return**

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

Guard clauses are exempt. A `return` sitting directly inside an `if` block never reports —
demanding a blank line inside a two-line block would make the guard harder to read, not
easier:

```csharp
// ✔
if (values.Length == 0)
{
    logger.LogDebug("Empty input");
    return 0;
}
```

The same goes for a `return` that is the first statement in its block.

---

## AVIG0004

**Blank line before try**

```csharp
// ⚠ AVIG0004
var payload = BuildPayload(order);
try
{
    await client.SendAsync(payload, cancellationToken);
}
catch (HttpRequestException exception)
{
    logger.LogWarning(exception, "Delivery failed");
}

// ✔
var payload = BuildPayload(order);

try
{
    await client.SendAsync(payload, cancellationToken);
}
catch (HttpRequestException exception)
{
    logger.LogWarning(exception, "Delivery failed");
}
```

A `try` that is the first statement in its block does not report.

---

## AVIG0005

**Blank line before while**

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

`do { … } while (…);` is never matched — the trailing `while` is a different node in the
syntax tree, and a blank line there would be a syntax error.

---

## AVIG0006

**CRLF line endings**

**Disabled by default.** In an LF repository every file would report on every line. Check
`.gitattributes` before turning it on:

```ini
dotnet_diagnostic.AVIG0006.severity = error
```

The rule requires every line ending to be `\r\n`. It only looks at end-of-line trivia, never
at the contents of verbatim or raw string literals, so a literal that deliberately contains
`\n` is never rewritten:

```csharp
// ✔ — this LF is left alone, it is data and not formatting
const string Payload = "first\nsecond";
```

A code fix is available.

---

## AVIG0007

**Method returns null to signal failure**

A method that yields a value when everything went well and `null` when it did not is using
null as an error signal. The failure disappears from the signature, and every caller has to
remember the null check on its own.

```csharp
// ⚠ AVIG0007 — null on one path, a value on another
public Order? Parse(string raw)
{
    if (string.IsNullOrWhiteSpace(raw))
    {
        return null;
    }

    return new Order(raw);
}

// ✔ — the failure is visible in the type
public Result<Order> Parse(string raw)
{
    if (string.IsNullOrWhiteSpace(raw))
    {
        return Result<Order>.Failure("Empty input");
    }

    return Result<Order>.Success(new Order(raw));
}
```

The rule is name-based and neutral: if the method already returns a type called `Result` or
`Option` — anyone's, in any namespace — it is skipped. The package has no opinion about which
result pattern you use, only that you use one.

Methods that return *only* null, or *only* values, do not report either. The mix is the
signal. Ternaries and switch expressions are searched; lambdas and local functions are judged
on their own.

---

## AVIG0008

**Repository method without a CancellationToken**

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

A token that is threaded through the whole call stack and then dropped at the data-access
boundary buys nothing: a cancelled call runs its work to completion anyway. Taking the token
as the last parameter is the convention both the BCL and EF Core follow, and it gives CA2016
and MA0040 something to actually enforce — without it they have no method to forward to.

Matches interfaces named `I…Repository` whose members return `Task` or `ValueTask`.
Synchronous members and interfaces with other names are left alone.

---

## AVIG0009

**Public controller or repository interface without an XML summary**

```csharp
// ⚠ AVIG0009
public class OrdersController : ControllerBase { }

public interface IOrderRepository { }

// ✔
/// <summary>Reads and places orders.</summary>
public class OrdersController : ControllerBase { }

/// <summary>Storage for <see cref="Order"/>.</summary>
public interface IOrderRepository { }
```

CS1591 cannot do this. It requires `GenerateDocumentationFile`, and then demands docs on every
public member — including every DTO property. This rule covers only the two categories where
the contract is genuinely somebody else's problem.

If you have your own marker interfaces that should be documented, list them:

```ini
dotnet_code_quality.AVIG0009.additional_documented_interfaces = IAppCommand,IAppQuery
```

Non-public types never report.

---

## AVIG0010

**Role, scope or claim name written as a string literal**

`warning` out of the box. A misspelled role string silently denies or silently grants access —
never a compile error.

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

Matches string literals passed to `IsInRole`, `HasClaim`, `FindFirst`, `FindFirstValue`,
`FindAll`, `RequireRole`, `RequireClaim`, `RequireScope` and `HasScope`, plus `Policy`, `Roles`
and `AuthenticationSchemes` on `[Authorize]`. An `[Authorize]` without arguments does not
report.

---

## AVIG0011

**Doc comment with `<param>` but no `<summary>`**

```csharp
// ⚠ AVIG0011
/// <param name="id">The order id.</param>
/// <returns>The order, or null.</returns>
public Order? Get(int id) => repository.Find(id);

// ✔
/// <summary>Reads a single order.</summary>
/// <param name="id">The order id.</param>
/// <returns>The order, or null when it does not exist.</returns>
public Order? Get(int id) => repository.Find(id);
```

A comment that only describes the parts never says what the member is for. The rule never asks
for documentation that does not exist — it only fires once somebody has started documenting.

`<inheritdoc/>` counts as documented. The check is also scoped per token, so a type's
`<summary>` is never credited to its members:

```csharp
// ⚠ AVIG0011 — the class summary describes the class, not the method
/// <summary>Storage for orders.</summary>
public class Repository
{
    /// <param name="id">The order id.</param>
    public Order? Get(int id) => Find(id);
}
```

The rule works whether or not the project sets `GenerateDocumentationFile`. It replaces
Roslynator RCS1139, which only ships in the VS extension and not in the NuGet package.

---

## AVIG0012

**Controller action without an explicit auth decision**

```csharp
// ⚠ AVIG0012 — nobody has decided anything
[HttpGet("orders/{id}")]
public IActionResult Get(int id) => Ok(repository.Find(id));

// ✔ — protected
[Authorize]
[HttpGet("orders/{id}")]
public IActionResult Get(int id) => Ok(repository.Find(id));

// ✔ — deliberately open
[AllowAnonymous]
[HttpGet("health")]
public IActionResult Health() => Ok();
```

The rule does not require authorization. It requires the decision to be visible at the
signature, so that "anonymous" is something a reviewer can *see* rather than infer from an
absence.

**The fix is not always `[AllowAnonymous]`.** If your host has a global `AuthorizeFilter`, an
unannotated action is *closed*, and `[AllowAnonymous]` would open it. Without the filter it is
open. The rule reports both cases — that the reader cannot tell them apart is precisely the
objection — but which fix is right depends on the host.

An `[Authorize]` on the class or on a base class counts for every action in it:

```csharp
// ✔ — no report on Get
[Authorize]
public abstract class SecuredController : ControllerBase { }

public class OrdersController : SecuredController
{
    [HttpGet("orders/{id}")]
    public IActionResult Get(int id) => Ok();
}
```

Custom auth attributes implementing `IAuthorizationFilter` or `IAsyncAuthorizationFilter` are
recognised automatically. Others can be listed:

```ini
dotnet_code_quality.AVIG0012.additional_auth_attributes = HmacAuthenticated,InternalOnly
```

Authorization inside the method body is invisible here and reports anyway. That is deliberate:
a guard that only exists in the body does not protect the next action somebody adds to the
same controller.

---

## AVIG0014

**Blocking call on an asynchronous operation**

`warning` out of the box.

```csharp
// ⚠ AVIG0014
public Order Get(int id)
{
    return _repository.GetAsync(id).Result;
}

public void Save(Order order)
{
    _repository.SaveAsync(order).Wait();
    _repository.FlushAsync().ConfigureAwait(false).GetAwaiter().GetResult();
}

// ✔
public async Task<Order> GetAsync(int id, CancellationToken cancellationToken)
{
    return await _repository.GetAsync(id, cancellationToken);
}
```

Blocking a thread on a task that has not completed is the classic deadlock: on a host with a
synchronization context the continuation needs the very thread that is now sleeping, and
neither side ever moves again. Where it does not deadlock it still occupies a pool thread for
the duration, and the exception comes back wrapped in an `AggregateException` — so the `catch`
clause that used to work stops matching.

The compilation entry point is exempt: `Main` has to block somewhere, even though on a modern
SDK it should be `async` instead. **Constructors are not exempt.** A constructor cannot be made
async, but that is an argument for moving the work out of it, not for hiding the deadlock.

The rule does not know that `if (task.IsCompleted)` makes the read harmless. Such places exist,
and they are few enough to suppress one at a time.

---

## AVIG0015

**Awaited call drops a CancellationToken that is in scope**

`warning` out of the box.

```csharp
// ⚠ AVIG0015 — the token is in the signature and goes nowhere
public async Task<Order?> GetAsync(int id, CancellationToken cancellationToken)
{
    return await _repository.FindAsync(id);
}

// ⚠ AVIG0015 — compiles, and is exactly as uncancellable
public async Task<Order?> GetAsync(int id, CancellationToken cancellationToken)
{
    return await _repository.FindAsync(id, CancellationToken.None);
}

// ✔
public async Task<Order?> GetAsync(int id, CancellationToken cancellationToken)
{
    return await _repository.FindAsync(id, cancellationToken);
}
```

AVIG0008 requires the token to be in the signature. This rule requires it to actually go
somewhere. A token that is accepted and then not forwarded is worse than no token at all: the
signature promises cancellation, callers write code that depends on it, and the work runs to
completion anyway.

The rule reports when the callee has a `CancellationToken` parameter that was omitted, and when
an overload exists with the same parameters plus a trailing token. `CancellationToken.None` and
`default` count as passing nothing.

Deliberately narrower than CA2016, which fires at every call site with a token overload. This
one only looks at `await`ed calls — where the cost of running past a cancellation is actually
paid — and only when a token is in scope to forward. `ConfigureAwait` and `WithCancellation`
are peeled off on the way in, and a token on an enclosing method is found from inside a lambda
too.

---

## AVIG0020

**Local time instead of UTC**

```csharp
// ⚠ AVIG0020
order.PlacedAt = DateTime.Now;
var today = DateTime.Today;

// ✔
order.PlacedAt = DateTime.UtcNow;
var today = DateTime.UtcNow.Date;
```

Local time is a property of whichever machine the code happens to run on. Two servers in
different zones disagree, a value written in March cannot be read back in November, and the
hour that repeats when daylight saving time ends makes ordering by timestamp ambiguous — a
whole hour of rows that sort into each other. None of it shows up in a test suite that runs in
a single zone.

Store and compare in UTC, and convert to local time at the point of display, where the reader's
zone is actually known. `DateTime.Today` is the same defect with the failure moved to midnight:
it is the machine's idea of what today is.

The code fix swaps `Now` for `UtcNow` and `Today` for `UtcNow.Date`. The next step — an injected
`TimeProvider` instead of a static clock call — is more than this rule wants an opinion about,
but it makes that step possible: there is only one place left to replace.

---

## AVIG0021

**Log message built instead of templated**

```csharp
// ⚠ AVIG0021
_logger.LogInformation($"Order {id} placed by {customer}");
_logger.LogInformation(string.Format("Order {0} placed", id));

// ✔
_logger.LogInformation("Order {OrderId} placed by {Customer}", id, customer);
```

The message template is the only thing that lets a structured sink tell one event from another.
`"Order {OrderId} placed"` is *one* event with a searchable field; `$"Order {id} placed"` is a
new, unique message string per order. Grouping, alerting on a rate and filtering on a value all
stop working, and the interpolation is evaluated even when the level is disabled.

Recognition is by shape, not by reference: the method name has to be a log-level name
(`LogInformation`, `Information`, `Warning`, `Fatal` …) and the receiver's type name has to
contain `Logger` or be exactly `Log`. That covers `ILogger`, Serilog's static `Log` and your own
wrappers without the package referencing any of them. More can be listed:

```ini
dotnet_code_quality.AVIG0021.additional_logger_types = Telemetry,AuditTrail
```

Only the argument that lands on the template parameter is examined — an interpolated string as
a *value* bound to a placeholder is odd, but not this rule's problem. An interpolated string
with no holes (`$"Order placed"`) does not report either: there is nothing in it to turn into a
field.

---

## AVIG0022

**Public member hands out a mutable collection**

```csharp
// ⚠ AVIG0022
public class Basket
{
    private readonly List<Line> _lines = new();

    public List<Line> Lines => _lines;

    public Task<List<Line>> LoadAsync() => ...;
}

// ✔
public class Basket
{
    private readonly List<Line> _lines = new();

    public IReadOnlyList<Line> Lines => _lines;

    public Task<IReadOnlyList<Line>> LoadAsync() => ...;
}
```

Two separate problems in one signature. The caller can add to and remove from a collection it
never owned, mutating state behind the declaring type's back with no way for it to notice. And
the concrete type is now part of the contract: switching the field to an array, an
`ImmutableArray` or a lazy sequence later becomes a binary-breaking change, even though nothing
about the meaning of the member changed.

Returning the read-only interface costs nothing — `List<T>` already implements
`IReadOnlyList<T>`, so the body stays as it is. The code fix swaps `List<T>` and `T[]` for
`IReadOnlyList<T>`, `Dictionary<K,V>` for `IReadOnlyDictionary<K,V>` and `HashSet<T>` for
`IReadOnlyCollection<T>` (`IReadOnlySet<T>` only exists from .NET 5, and a netstandard2.0
consumer has to be able to take the fix).

`Task<List<T>>` counts as much as a bare `List<T>`. Exempt are `byte[]` — the currency of
hashes, streams and wire payloads, where `IReadOnlyList<byte>` would be a downgrade at every
call site — along with overrides and interface implementations, where the type is decided
elsewhere and a report here could not be acted on.

---

## AVIG0023

**TODO comment without a tracking reference**

`warning` out of the box.

```csharp
// ⚠ AVIG0023
// TODO: handle the empty basket
// FIXME this rounds the wrong way
// HACK - works until the migration lands

// ✔
// TODO(#412): handle the empty basket
// FIXME ORDERS-77 rounds the wrong way
// HACK until https://github.com/acme/shop/issues/9 lands
```

The rule does not object to TODO comments. It objects to a TODO nobody is coming back to:
written in a hurry, waved through by every reviewer since, and invisible to whatever list the
team actually works from. An issue id or a link turns the note into something with an owner and
a place in a queue. Without one it is a comment documenting a decision not to decide.

This matters more the more of the code is generated. `TODO: handle the error case` is the
cheapest possible way for a model to finish a method, and it reads as diligence. Requiring a
reference leaves two honest options: file the issue, or do the work now. It is also why the
rule is a `warning` — at `suggestion` it would never reach the build log, and a marker nobody
sees is precisely the thing the rule exists to prevent.

The markers are `TODO`, `FIXME`, `HACK` and `XXX`, matched case-insensitively as whole words,
in ordinary comments and in XML doc comments. `TodoListItem` and `XXXL` are not markers. A
marker inside a string literal is data, not a note to self, and is never reported.

A reference is an issue number (`#123`), an issue key (`ABC-123`) or a URL, and it has to sit
in the same comment as the marker — a block comment can carry it on any of its lines. Both
halves are configurable:

```ini
dotnet_code_quality.AVIG0023.additional_markers = NOTE,REVIEW
dotnet_code_quality.AVIG0023.reference_pattern = (#\d+)|(WORK-\d+)
```

`reference_pattern` replaces the built-in one rather than adding to it. A pattern that does not
parse falls back to the built-in one instead of taking the build down with an analyzer crash.

---

## Why netstandard2.0 and not net10.0

The analyzer assembly targets `netstandard2.0`. That is not lagging behind.

Roslyn loads the analyzer into the *compiler process* (`csc` / `VBCSCompiler`), not into your
app, and that process runs on .NET Framework when Visual Studio is the host. NuGet also places
the analyzer DLL at `analyzers/dotnet/cs` — a path with no TFM dimension at all, so there is no
mechanism for shipping a `net10.0` DLL to some consumers and a different one to the rest.

Your project is welcome to be `net10.0`. It has nothing to do with this line.

The package references Roslyn 4.11.0, which reaches down to SDK 8.0.4xx and VS 17.11. An
analyzer must reference a Roslyn version equal to or older than the consumer's compiler, so
bumping it would lock out everyone not on the latest SDK.

## Origins in Medusa.Analyzers

The first twelve rules were extracted from an internal analyzer project. The eleven that
required that project's own types — the Result pattern, CQRS conventions — did not come along.
If you are migrating an `.editorconfig`, this is the mapping:

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

AVIG0014, AVIG0015, AVIG0020–AVIG0022 and AVIG0023 have no Medusa counterpart — they were
written here.

Three things differ beyond the numbering: AVIG0007 no longer looks for a specific `Result`
namespace, AVIG0009 lost its Command, Query and Handler categories in favour of
`additional_documented_interfaces`, and every default was lowered from `warning` to
`suggestion`.

## Building and releasing

```powershell
dotnet build Avig.Analyzers.slnx -c Release
dotnet test Avig.Analyzers.slnx -c Release
./publish.ps1                    # bumps patch, tests, packs, verifies package shape
./publish.ps1 -NoBump            # the same without a version bump
```

`publish.ps1` never pushes on its own. That is deliberate — publishing to nuget.org is a manual
step, and the API key must never end up in the repository.

## License

MIT. See [LICENSE](LICENSE).
