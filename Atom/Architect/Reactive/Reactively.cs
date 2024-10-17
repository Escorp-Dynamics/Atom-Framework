using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Atom.Architect.Reactive;

/// <summary>
/// Представляет базовую реализацию любой изменяемой модели данных.
/// </summary>
public abstract class Reactively : IReactively
{
    /// <summary>
    /// Происходит перед изменением свойства.
    /// </summary>
    public event PropertyChangingEventHandler? PropertyChanging;

    /// <summary>
    /// Происходит после изменения свойства.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Изменяет значение свойства.
    /// </summary>
    /// <typeparam name="T">Тип свойства.</typeparam>
    /// <param name="storage">Ссылка на поле, хранящее значение свойства.</param>
    /// <param name="value">Значение свойства.</param>
    /// <param name="propertyName">Имя свойства.</param>
    /// <returns>True, если свойство было изменено, иначе false.</returns>
    protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = default)
    {
        if (Equals(storage, value)) return false;

        OnPropertyChanging(storage, value, propertyName);
        storage = value;
        OnPropertyChanged(propertyName);

        return true;
    }

    /// <summary>
    /// Происходит перед изменением свойства.
    /// </summary>
    /// <param name="oldValue">Исходное значение свойства.</param>
    /// <param name="newValue">Назначаемое значение свойства.</param>
    /// <param name="propertyName">Имя свойства.</param>
    protected virtual void OnPropertyChanging(object? oldValue, object? newValue, [CallerMemberName] string? propertyName = default)
        => PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(propertyName));

    /// <summary>
    /// Происходит после изменения свойства.
    /// </summary>
    /// <param name="propertyName">Имя свойства.</param>
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = default) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}