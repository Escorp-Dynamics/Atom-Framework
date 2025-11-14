namespace Atom;

/// <summary>
/// Представляет обработчик событий.
/// </summary>
/// <typeparam name="TSender">Тип источника события.</typeparam>
/// <typeparam name="TEventArgs">Тип аргументов события.</typeparam>
/// <param name="sender">Источник события.</param>
/// <param name="e">Аргументы события.</param>
public delegate void MutableEventHandler<in TSender, in TEventArgs>(TSender sender, TEventArgs e) where TEventArgs : EventArgs;