using System.Diagnostics.CodeAnalysis;

namespace Atom.Buffers;

/// <summary>
/// Представляет базовый интерфейс для реализации объектов, поддерживающих работу с пулом.
/// </summary>
public interface IPooled
{
    /// <summary>
    /// Происходит в момент возврата экземпляра в пул.
    /// </summary>
    void Reset();

    /// <summary>
    /// Арендует экземпляр в пуле объектов.
    /// </summary>
    /// <typeparam name="T">Тип экземпляра.</typeparam>
    static abstract T Rent<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor | DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)] T>() where T : IPooled;

    /// <summary>
    /// Возвращает экземпляр в пул объектов.
    /// </summary>
    /// <param name="value">Экземпляр.</param>
    /// <typeparam name="T">Тип экземпляра.</typeparam>
    static abstract void Return<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor | DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)] T>(T value) where T : IPooled;
}