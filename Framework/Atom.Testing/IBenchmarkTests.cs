using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Loggers;
using NUnit.Framework;

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
    [GlobalSetup]
    void GlobalSetUp();

    /// <summary>
    /// Устанавливает настройки для каждого теста.
    /// </summary>
    [OneTimeSetUp]
    void OneTimeSetUp();

    /// <summary>
    /// Вызывается при завершении всех тестов.
    /// </summary>
    [TearDown]
    void GlobalTearDown();

    /// <summary>
    /// Вызывается при завершении каждого теста.
    /// </summary>
    [OneTimeTearDown]
    void OneTimeTearDown();
}