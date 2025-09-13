namespace Atom.Architect.Builders;

/// <summary>
/// Представляет базовый интерфейс для реализации строителя.
/// </summary>
public interface IBuilder
{
    /// <summary>
    /// Создаёт новый экземпляр строителя.
    /// </summary>
    static abstract IBuilder Create();
}

/// <summary>
/// Представляет базовый интерфейс для реализации строителя.
/// </summary>
/// <typeparam name="TResult">Тип результата построения.</typeparam>
public interface IBuilder<out TResult> : IBuilder where TResult : allows ref struct
{
    /// <summary>
    /// Строит результирующее значение.
    /// </summary>
    TResult Build();

    /// <summary>
    /// Создаёт новый экземпляр строителя.
    /// </summary>
    static new abstract IBuilder<TResult> Create();
}

/// <summary>
/// Представляет базовый интерфейс для реализации строителя.
/// </summary>
/// <typeparam name="TResult">Тип результата построения.</typeparam>
/// <typeparam name="TBuilder">Тип строителя.</typeparam>
public interface IBuilder<out TResult, out TBuilder> : IBuilder<TResult> where TResult : allows ref struct
{
    /// <summary>
    /// Создаёт новый экземпляр строителя.
    /// </summary>
    static new abstract TBuilder Create();
}