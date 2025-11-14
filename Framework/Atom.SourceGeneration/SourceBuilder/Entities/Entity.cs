using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using Atom.Buffers;
using Atom.Collections;

namespace Atom.SourceGeneration;

/// <summary>
/// Представляет член сущности.
/// </summary>
public abstract class Entity : IEntity
{
    private readonly SparseArray<string> attributes = new(128);

    /// <inheritdoc/>
    public string? Comment { get; protected set; }

    /// <inheritdoc/>
    public IEnumerable<string> Attributes => attributes;

    /// <inheritdoc/>
    public string Name { get; protected set; } = string.Empty;

    /// <inheritdoc/>
    public abstract bool IsValid { get; }

    /// <summary>
    /// Добавляет атрибуты.
    /// </summary>
    /// <param name="attributes">Коллекция атрибутов.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual void AddAttribute([NotNull] params IEnumerable<string> attributes) => this.attributes.AddRange(attributes);

    /// <summary>
    /// Очищает установленные атрибуты.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void ClearAttributes() => attributes.Reset();

    /// <summary>
    /// Происходит при сборке комментария.
    /// </summary>
    /// <param name="sb">Сборщик строки.</param>
    /// <param name="spaces">Отступ.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual void OnBuildingComment([NotNull] StringBuilder sb, string spaces)
    {
        if (string.IsNullOrEmpty(Comment)) return;

        if (Comment is "<inheritdoc/>")
        {
            sb.AppendLine($"{spaces}/// {Comment}");
            return;
        }

        sb.AppendLine($"{spaces}/// <summary>")
          .AppendLine($"{spaces}/// {Comment}")
          .AppendLine($"{spaces}/// </summary>");
    }

    /// <summary>
    /// Происходит при сборке атрибутов.
    /// </summary>
    /// <param name="sb">Сборщик строки.</param>
    /// <param name="spaces">Отступ.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual void OnBuildingAttributes([NotNull] StringBuilder sb, string spaces)
    {
        foreach (var attr in Attributes) sb.AppendLine($"{spaces}[{attr}]");
    }

    /// <summary>
    /// Происходит при сборке объявления.
    /// </summary>
    /// <param name="sb">Сборщик строки.</param>
    /// <param name="spaces">Отступ.</param>
    /// <param name="usings">Используемые пространства имён.</param>
    protected abstract void OnBuildingDeclaration([NotNull] StringBuilder sb, string spaces, params IEnumerable<string> usings);

    /// <inheritdoc/>
    public virtual string? Build(int tabs, bool release, params IEnumerable<string> usings)
    {
        if (!IsValid)
        {
            if (release) Release();
            return default;
        }

        var spaces = GetSpaces(tabs);
        var sb = ObjectPool<StringBuilder>.Shared.Rent();

        OnBuildingComment(sb, spaces);
        OnBuildingAttributes(sb, spaces);
        OnBuildingDeclaration(sb, spaces, usings);

        var result = sb.ToString();
        ObjectPool<StringBuilder>.Shared.Return(sb, x => x.Clear());
        if (release) Release();

        return result;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string? Build(int tabs, params IEnumerable<string> usings) => Build(tabs, default, usings);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string? Build(bool release, params IEnumerable<string> usings) => Build(default, release, usings);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string? Build(params IEnumerable<string> usings) => Build(default, default, usings);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void Release()
    {
        attributes.Reset();
        Comment = string.Empty;
        Name = string.Empty;
    }

    /// <summary>
    /// Возвращает строку, состоящую из заданного количества табуляций.
    /// </summary>
    /// <param name="tabs">Количество табуляций.</param>
    /// <returns>Строка, состоящая из заданного количества табуляций.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static string GetSpaces(int tabs) => new(' ', tabs * 4);
}

/// <summary>
/// Представляет член сущности.
/// </summary>
/// <typeparam name="T">Тип реализации члена сущности.</typeparam>
public abstract class Entity<T> : Entity, IEntity<T> where T : IEntity
{
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public abstract T WithName(string name);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public abstract T WithComment(string? comment);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public abstract T WithAttribute(params IEnumerable<string> attributes);
}
