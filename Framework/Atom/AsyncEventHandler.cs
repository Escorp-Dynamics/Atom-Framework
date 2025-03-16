namespace Atom;

/// <summary>
/// Представляет асинхронный обработчик событий.
/// </summary>
/// <typeparam name="TSender">Тип источника события.</typeparam>
/// <typeparam name="TEventArgs">Тип аргументов события.</typeparam>
/// <param name="sender">Источник события.</param>
/// <param name="e">Аргументы события.</param>
public delegate ValueTask AsyncEventHandler<in TSender, in TEventArgs>(TSender sender, TEventArgs e) where TEventArgs : EventArgs;

/// <summary>
/// Представляет асинхронный обработчик событий.
/// </summary>
/// <typeparam name="TSender">Тип источника события.</typeparam>
/// <param name="sender">Источник события.</param>
public delegate ValueTask AsyncEventHandler<in TSender>(TSender sender);

/// <summary>
/// Представляет асинхронный обработчик событий.
/// </summary>
public delegate ValueTask AsyncEventHandler();