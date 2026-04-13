# DOM/BOM File Templates

## Назначение

Этот документ фиксирует минимальные file templates для первого code pass.

Он не вводит новые policy decisions, а переводит уже принятые правила в повторяемые заготовки файлов.

## Interface template

```csharp
namespace Atom.Net.Browsing.BOM.Networking;

/// <summary>
/// Spec-shaped contract generated from the accepted Web IDL surface.
/// </summary>
public interface IUrl
{
}
```

## Delegate template

```csharp
namespace Atom.Net.Browsing.BOM.Scheduling;

/// <summary>
/// Named callback contract mapped from Web IDL callback provenance.
/// </summary>
public delegate void IdleRequestCallback(IIdleDeadline deadline);
```

## Supporting contract template

```csharp
namespace Atom.Net.Browsing.BOM.Scheduling;

/// <summary>
/// Supporting contract mapped from a Web IDL dictionary used by a public signature.
/// </summary>
public sealed record IdleRequestOptions;
```

## Enum template

```csharp
namespace Atom.Net.Browsing.BOM.Permissions;

/// <summary>
/// Closed vocabulary mapped from the accepted Web IDL enum surface.
/// </summary>
public enum PermissionState
{
}
```

## Template rules

- one public entity per file
- namespace follows owning folder
- interface names keep I-prefix only for interfaces
- delegates do not receive I-prefix
- supporting contract files stay separate from interface files
- XML summary remains minimal and provenance-safe until real member surface is added
