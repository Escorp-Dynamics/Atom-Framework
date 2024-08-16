using Atom.Web.Browsers.NativeMessaging.Signals.Client;

namespace Atom.Web.Browsers.NativeMessaging.Signals;

/// <summary>
/// Представляет базовую реализацию сигнала.
/// </summary>
public abstract class Signal
{
    /// <summary>
    /// Инициализирует новый экземпляр класса <see cref="Signal"/>.
    /// </summary>
    /// <param name="properties">Свойства сигнала.</param>
    protected Signal(IReadOnlyDictionary<string, object?> properties) { }

    /// <summary>
    /// Пытается получить экземпляр сигнала по его имени.
    /// </summary>
    /// <param name="name">Имя сигнала.</param>
    /// <param name="properties">Свойства сигнала.</param>
    /// <param name="signal">Экземпляр сигнала.</param>
    /// <returns><c>True</c>, если процедура была успешна, иначе <c>false</c>.</returns>
    public static bool TryGetByName(string name, IReadOnlyDictionary<string, object?> properties, out Signal? signal)
    {
        signal = name switch
        {
            "installed" => new InstalledSignal(properties),
            _ => default,
        };

        return signal is not null;
    }

    /// <summary>
    /// Пытается получить экземпляр сигнала по его имени.
    /// </summary>
    /// <param name="name">Имя сигнала.</param>
    /// <param name="signal">Экземпляр сигнала.</param>
    /// <returns><c>True</c>, если процедура была успешна, иначе <c>false</c>.</returns>
    public static bool TryGetByName(string name, out Signal? signal) => TryGetByName(name, new Dictionary<string, object?>(), out signal);
}