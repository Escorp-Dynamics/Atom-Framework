namespace Atom;

/// <summary>
/// Представляет асинхронный обработчик событий.
/// </summary>
/// <typeparam name="TSender">Тип источника события.</typeparam>
/// <typeparam name="TEventArgs">Тип аргументов события.</typeparam>
/// <param name="sender">Источник события.</param>
/// <param name="e">Аргументы события.</param>
/// <returns></returns>
public delegate ValueTask AsyncEventHandler<TSender, TEventArgs>(TSender sender, TEventArgs e) where TEventArgs : EventArgs;

/// <summary>
/// Представляет асинхронный обработчик событий.
/// </summary>
/// <typeparam name="TSender">Тип источника события.</typeparam>
/// <param name="sender">Источник события.</param>
/// <returns></returns>
public delegate ValueTask AsyncEventHandler<TSender>(TSender sender);