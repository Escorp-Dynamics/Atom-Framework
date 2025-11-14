using System.Runtime.CompilerServices;
using BenchmarkDotNet.Loggers;

namespace Atom.Testing;

/// <summary>
/// Представляет базовый интерфейс для реализации тестовых модулей с поддержкой замеров производительности.
/// </summary>
public interface IBenchmarkTests
{
    /// <summary>
    /// Определяет, включены ли замеры производительности.
    /// </summary>
    bool IsBenchmarkEnabled { get; }

    /// <summary>
    /// Журнал событий для бенчмарка.
    /// </summary>
    ILogger Logger { get; }

    /// <summary>
    /// Устанавливает настройки для всех тестов.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void GlobalSetUp();

    /// <summary>
    /// Устанавливает настройки для каждого теста.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void OneTimeSetUp();

    /// <summary>
    /// Вызывается при завершении всех тестов.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void GlobalTearDown();

    /// <summary>
    /// Вызывается при завершении каждого теста.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void OneTimeTearDown();
}