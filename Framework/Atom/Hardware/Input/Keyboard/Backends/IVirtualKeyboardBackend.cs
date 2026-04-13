namespace Atom.Hardware.Input.Backends;

/// <summary>
/// Платформенный бэкенд виртуальной клавиатуры.
/// </summary>
internal interface IVirtualKeyboardBackend : IAsyncDisposable
{
    /// <summary>
    /// Идентификатор виртуального устройства в системе.
    /// </summary>
    string DeviceIdentifier { get; }

    /// <summary>
    /// Инициализирует виртуальное устройство клавиатуры.
    /// </summary>
    ValueTask InitializeAsync(VirtualKeyboardSettings settings, CancellationToken cancellationToken);

    /// <summary>
    /// Нажимает клавишу (без отпускания).
    /// </summary>
    void KeyDown(ConsoleKey key);

    /// <summary>
    /// Отпускает клавишу.
    /// </summary>
    void KeyUp(ConsoleKey key);

    /// <summary>
    /// Нажимает модификатор (Shift/Ctrl/Alt).
    /// </summary>
    void ModifierDown(ConsoleModifiers modifier);

    /// <summary>
    /// Отпускает модификатор (Shift/Ctrl/Alt).
    /// </summary>
    void ModifierUp(ConsoleModifiers modifier);
}
