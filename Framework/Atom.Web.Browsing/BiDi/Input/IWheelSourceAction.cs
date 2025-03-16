#pragma warning disable CA1040
using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Input;

/// <summary>
/// Представляет базовый интерфейс для реализации действия, используемого с устройством ввода типа колеса.
/// </summary>
[JsonDerivedType(typeof(WheelScrollAction))]
[JsonDerivedType(typeof(PauseAction))]
public interface IWheelSourceAction;