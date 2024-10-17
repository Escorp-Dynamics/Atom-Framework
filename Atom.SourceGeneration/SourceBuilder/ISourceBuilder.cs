namespace Atom.SourceGeneration;

/// <summary>
/// Представляет строителя исходного кода
/// </summary>
public interface ISourceBuilder
{
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
    /// Добавляет используемые пространства имён.
    /// </summary>
    /// <param name="ns">Используемые пространства имён.</param>
    ISourceBuilder AddUsings(params string[] ns);

    /// <summary>
    /// Добавляет используемое пространство имён.
    /// </summary>
    /// <param name="ns">Используемое пространство имён.</param>
    ISourceBuilder AddUsing(string ns) => AddUsings(ns);

    /// <summary>
    /// Добавляет <c>namespace</c>.
    /// </summary>
    /// <param name="ns">Пространство имён.</param>
    ISourceBuilder WithNamespace(string? ns);

    /// <summary>
    /// Добавляет сущности в исходный код.
    /// </summary>
    /// <param name="entities">Сущности.</param>
    ISourceBuilder AddEntities(params IEntity[] entities);

    /// <summary>
    /// Добавляет сущность в исходный код.
    /// </summary>
    /// <param name="entity">Сущность.</param>
    ISourceBuilder AddEntity(IEntity entity) => AddEntities(entity);

    /// <summary>
    /// Добавляет перечисление в исходный код.
    /// </summary>
    /// <param name="entity">Строитель для <c>enum</c>.</param>
    ISourceBuilder AddEnum(EnumEntity entity) => AddEntity(entity);

    /// <summary>
    /// Добавляет интерфейс в исходный код.
    /// </summary>
    /// <param name="entity">Строитель для <c>interface</c>.</param>
    ISourceBuilder AddInterface(InterfaceEntity entity) => AddEntity(entity);

    /// <summary>
    /// Добавляет класс в исходный код.
    /// </summary>
    /// <param name="entity">Строитель для <c>interface</c>.</param>
    ISourceBuilder AddClass(ClassEntity entity) => AddEntity(entity);

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