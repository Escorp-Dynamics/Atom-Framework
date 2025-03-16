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
    ISourceBuilder WithDirective(params string[] directives);

    /// <summary>
    /// Добавляет используемые пространства имён.
    /// </summary>
    /// <param name="ns">Используемые пространства имён.</param>
    ISourceBuilder WithUsing(params string[] ns);

    /// <summary>
    /// Добавляет <c>namespace</c>.
    /// </summary>
    /// <param name="ns">Пространство имён.</param>
    ISourceBuilder WithNamespace(string? ns);

    /// <summary>
    /// Добавляет сущности в исходный код.
    /// </summary>
    /// <param name="entities">Сущности.</param>
    ISourceBuilder WithEntity(params IEntity[] entities);

    /// <summary>
    /// Добавляет перечисления в исходный код.
    /// </summary>
    /// <param name="enums">Перечисления.</param>
    ISourceBuilder WithEnum(params EnumEntity[] enums) => WithEntity(enums);

    /// <summary>
    /// Добавляет интерфейс в исходный код.
    /// </summary>
    /// <param name="interfaces">Интерфейсы.</param>
    ISourceBuilder WithInterface(params InterfaceEntity[] interfaces) => WithEntity(interfaces);

    /// <summary>
    /// Добавляет класс в исходный код.
    /// </summary>
    /// <param name="classes">Классы.</param>
    ISourceBuilder WithClass(params ClassEntity[] classes) => WithEntity(classes);

    /// <summary>
    /// Собирает исходный код.
    /// </summary>
    /// <param name="release">Указывает, нужно ли освободить используемые ресурсы после сборки.</param>
    string? Build(bool release);

    /// <summary>
    /// Собирает исходный код.
    /// </summary>
    string? Build() => Build(default);

    /// <summary>
    /// Высвобождает ресурсы.
    /// </summary>
    void Release();
}