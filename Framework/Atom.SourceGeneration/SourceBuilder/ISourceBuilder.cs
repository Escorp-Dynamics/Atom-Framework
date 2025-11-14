using System.Runtime.CompilerServices;

namespace Atom.SourceGeneration;

/// <summary>
/// Представляет строителя исходного кода
/// </summary>
public interface ISourceBuilder
{
    /// <summary>
    /// Используемые директивы.
    /// </summary>
    IEnumerable<string> Directives { get; }

    /// <summary>
    /// Используемые пространства имён.
    /// </summary>
    IEnumerable<string> Usings { get; }

    /// <summary>
    /// Пространство имён.
    /// </summary>
    string? Namespace { get; }

    /// <summary>
    /// Сущности.
    /// </summary>
    IEnumerable<IEntity> Entities { get; }

    /// <summary>
    /// Добавляет используемые директивы.
    /// </summary>
    /// <param name="directives">Используемые директивы.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ISourceBuilder WithDirective(params IEnumerable<string> directives);

    /// <summary>
    /// Добавляет используемые пространства имён.
    /// </summary>
    /// <param name="ns">Используемые пространства имён.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ISourceBuilder WithUsing(params IEnumerable<string> ns);

    /// <summary>
    /// Добавляет <see langword="namespace"/>.
    /// </summary>
    /// <param name="ns">Пространство имён.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ISourceBuilder WithNamespace(string? ns);

    /// <summary>
    /// Добавляет сущности в исходный код.
    /// </summary>
    /// <param name="entities">Сущности.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ISourceBuilder WithEntity(params IEnumerable<IEntity> entities);

    /// <summary>
    /// Добавляет перечисления в исходный код.
    /// </summary>
    /// <param name="enums">Перечисления.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ISourceBuilder WithEnum(params IEnumerable<EnumEntity> enums) => WithEntity(enums);

    /// <summary>
    /// Добавляет интерфейс в исходный код.
    /// </summary>
    /// <param name="interfaces">Интерфейсы.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ISourceBuilder WithInterface(params IEnumerable<InterfaceEntity> interfaces) => WithEntity(interfaces);

    /// <summary>
    /// Добавляет класс в исходный код.
    /// </summary>
    /// <param name="classes">Классы.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ISourceBuilder WithClass(params IEnumerable<ClassEntity> classes) => WithEntity(classes);

    /// <summary>
    /// Собирает исходный код.
    /// </summary>
    /// <param name="release">Указывает, нужно ли освободить используемые ресурсы после сборки.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    string? Build(bool release);

    /// <summary>
    /// Собирает исходный код.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    string? Build() => Build(default);

    /// <summary>
    /// Высвобождает ресурсы.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void Release();
}