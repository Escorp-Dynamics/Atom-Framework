#pragma warning disable CA1040
using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Input;

/// <summary>
/// Представляет базовый интерфейс для реализации действия, используемого без устройства ввода.
/// </summary>
[JsonDerivedType(typeof(PauseAction))]
public interface INoneSourceAction;