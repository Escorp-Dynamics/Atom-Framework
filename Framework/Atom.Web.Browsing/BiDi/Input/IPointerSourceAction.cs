#pragma warning disable CA1040
using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Input;

/// <summary>
/// Представляет базовый интерфейс для реализации действия, используемого с устройством ввода типа указателя.
/// </summary>
[JsonDerivedType(typeof(PointerDownAction))]
[JsonDerivedType(typeof(PointerUpAction))]
[JsonDerivedType(typeof(PointerMoveAction))]
[JsonDerivedType(typeof(PauseAction))]
public interface IPointerSourceAction;