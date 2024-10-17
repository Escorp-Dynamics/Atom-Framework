using System.Diagnostics.CodeAnalysis;
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
    public string Comment { get; protected set; } = string.Empty;

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
    protected virtual void AddAttributes([NotNull] params string[] attributes) => this.attributes.AddRange(attributes);

    /// <summary>
    /// Добавляет атрибут.
    /// </summary>
    /// <param name="attr">Атрибут.</param>
    protected virtual void AddAttribute(string attr) => AddAttributes(attr);

    /// <summary>
    /// Очищает установленные атрибуты.
    /// </summary>
    protected void ClearAttributes() => attributes.Reset();

    /// <summary>
    /// Происходит при сборке комментария.
    /// </summary>
    /// <param name="sb">Сборщик строки.</param>
    /// <param name="spaces">Отступ.</param>
    protected virtual void OnBuildingComment([NotNull] StringBuilder sb, string spaces)
    {
        if (string.IsNullOrEmpty(Comment)) return;

        sb.AppendLine($"{spaces}/// <summary>")
          .AppendLine($"{spaces}/// {Comment}")
          .AppendLine($"{spaces}/// </summary>");
    }

    /// <summary>
    /// Происходит при сборке атрибутов.
    /// </summary>
    /// <param name="sb">Сборщик строки.</param>
    /// <param name="spaces">Отступ.</param>
    protected virtual void OnBuildingAttributes([NotNull] StringBuilder sb, string spaces)
    {
        foreach (var attr in Attributes) sb.AppendLine($"{spaces}[{attr}]");
    }

    /// <summary>
    /// Происходит при сборке объявления.
    /// </summary>
    /// <param name="sb">Сборщик строки.</param>
    /// <param name="spaces">Отступ.</param>
    protected abstract void OnBuildingDeclaration([NotNull] StringBuilder sb, string spaces);

    /// <inheritdoc/>
    public virtual string? Build(int tabs, bool release)
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
        OnBuildingDeclaration(sb, spaces);

        var result = sb.ToString();
        ObjectPool<StringBuilder>.Shared.Return(sb, x => x.Clear());
        if (release) Release();

        return result;
    }

    /// <inheritdoc/>
    public string? Build(int tabs) => Build(tabs, default);

    /// <inheritdoc/>
    public string? Build(bool release) => Build(default, release);

    /// <inheritdoc/>
    public string? Build() => Build(default, default);

    /// <inheritdoc/>
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
    protected static string GetSpaces(int tabs) => new(' ', tabs * 4);
}

/// <summary>
/// Представляет член сущности.
/// </summary>
/// <typeparam name="T">Тип реализации члена сущности.</typeparam>
public abstract class Entity<T> : Entity, IEntity<T> where T : IEntity
{
    /// <inheritdoc/>
    public abstract T WithName(string name);

    /// <inheritdoc/>
    public abstract T WithComment(string comment);

    /// <inheritdoc/>
    public abstract T WithAttributes(params string[] attributes);

    /// <inheritdoc/>
    public T WithAttribute(string attribute) => WithAttributes(attribute);
}