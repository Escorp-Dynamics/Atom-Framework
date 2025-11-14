using System.Runtime.CompilerServices;

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
    /// <param name="usings">Используемые пространства имён.</param>
    /// <returns>Строка представления строителя сущности.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    string? Build(int tabs, params IEnumerable<string> usings);

    /// <summary>
    /// Строит строку представления строителя сущности с отступом.
    /// </summary>
    /// <param name="tabs">Количество отступов.</param>
    /// <param name="release">Указывает, нужно ли освободить ресурсы сущности.</param>
    /// <param name="usings">Используемые пространства имён.</param>
    /// <returns>Строка представления строителя сущности.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    string? Build(int tabs, bool release, params IEnumerable<string> usings)
    {
        var result = Build(tabs, usings);
        if (release) Release();
        return result;
    }

    /// <summary>
    /// Строит строку представления строителя сущности с отступом.
    /// </summary>
    /// <param name="release">Указывает, нужно ли освободить ресурсы сущности.</param>
    /// <param name="usings">Используемые пространства имён.</param>
    /// <returns>Строка представления строителя сущности.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    string? Build(bool release, params IEnumerable<string> usings) => Build(default, release, usings);

    /// <summary>
    /// Строит строку представления строителя сущности с отступом.
    /// </summary>
    /// <param name="usings">Используемые пространства имён.</param>
    /// <returns>Строка представления строителя сущности.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    string? Build(params IEnumerable<string> usings) => Build(default, default, usings);

    /// <summary>
    /// Возвращает сущность обратно в пул для повторного использования.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    T WithComment(string? comment);

    /// <summary>
    /// Добавляет название.
    /// </summary>
    /// <param name="name">Название.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    T WithName(string name);

    /// <summary>
    /// Добавляет атрибуты к строителю.
    /// </summary>
    /// <param name="attributes">Массив атрибутов для добавления.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    T WithAttribute(params IEnumerable<string> attributes);
}