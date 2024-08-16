namespace Atom.Web.Browsers.NativeMessaging.Signals.Client;

/// <summary>
/// Представляет сигнал установки расширения.
/// </summary>
/// <inheritdoc/>
public class InstalledSignal(IReadOnlyDictionary<string, object?> properties) : Signal(properties);