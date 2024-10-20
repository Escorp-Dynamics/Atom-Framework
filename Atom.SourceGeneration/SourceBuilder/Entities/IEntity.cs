namespace Atom.SourceGeneration;

/// <summary>
/// Представляет строителя сущности.
/// </summary>
public interface IEntity
{
    /// <summary>
    /// Комментарий.
    /// </summary>
    string? Comment { get; }

    /// <summary>
    /// Аттрибуты.
    /// </summary>
    IEnumerable<string> Attributes { get; }

    /// <summary>
    /// Название.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Определяет, может ли сущность быть собрана.
    /// </summary>
    bool IsValid { get; }

    /// <summary>
    /// Строит строку представления строителя сущности с отступом.
    /// </summary>
    /// <param name="tabs">Количество отступов.</param>
    /// <returns>Строка представления строителя сущности.</returns>
    string? Build(int tabs);

    /// <summary>
    /// Строит строку представления строителя сущности с отступом.
    /// </summary>
    /// <param name="tabs">Количество отступов.</param>
    /// <param name="release">Указывает, нужно ли освободить ресурсы сущности.</param>
    /// <returns>Строка представления строителя сущности.</returns>
    public string? Build(int tabs, bool release)
    {
        var result = Build(tabs);
        if (release) Release();
        return result;
    }

    /// <summary>
    /// Строит строку представления строителя сущности с отступом.
    /// </summary>
    /// <param name="release">Указывает, нужно ли освободить ресурсы сущности.</param>
    /// <returns>Строка представления строителя сущности.</returns>
    public string? Build(bool release) => Build(default, release);

    /// <summary>
    /// Строит строку представления строителя сущности с отступом.
    /// </summary>
    /// <returns>Строка представления строителя сущности.</returns>
    public string? Build() => Build(default, default);

    /// <summary>
    /// Возвращает сущность обратно в пул для повторного использования.
    /// </summary>
    void Release();
}

/// <summary>
/// Представляет строителя сущности.
/// </summary>
/// <typeparam name="T">Тип реализации строителя сущности.</typeparam>
public interface IEntity<out T> : IEntity where T : IEntity
{
    /// <summary>
    /// Добавляет комментарий.
    /// </summary>
    /// <param name="comment">Комментарий.</param>
    T WithComment(string? comment);

    /// <summary>
    /// Добавляет название.
    /// </summary>
    /// <param name="name">Название.</param>
    T WithName(string name);

    /// <summary>
    /// Добавляет атрибуты к строителю.
    /// </summary>
    /// <param name="attributes">Массив атрибутов для добавления.</param>
    T WithAttributes(params string[] attributes);

    /// <summary>
    /// Добавляет атрибут к строителю.
    /// </summary>
    /// <param name="attribute">Атрибут.</param>
    T WithAttribute(string attribute) => WithAttributes(attribute);
}