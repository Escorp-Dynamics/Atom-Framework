using System.ComponentModel;

namespace Atom.Architect.Reactive;

/// <summary>
/// Представляет базовый интерфейс для реализации изменяемых моделей данных.
/// </summary>
public interface IReactively : INotifyPropertyChanging, INotifyPropertyChanged;