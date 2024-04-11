using System.ComponentModel;

namespace Atom.Reactive;

/// <summary>
/// Представляет базовый интерфейс для реализации изменяемых моделей данных.
/// </summary>
public interface IReactively : INotifyPropertyChanging, INotifyPropertyChanged;