using System.Diagnostics.CodeAnalysis;
using System.Text;
using Atom.Buffers;
using Atom.Collections;

namespace Atom.SourceGeneration;

/// <summary>
/// Представляет строителя шаблонных типов.
/// </summary>
public class GenericEntity : Entity<GenericEntity>
{
    private readonly SparseArray<string> limitations = new(128);

    /// <summary>
    /// Является ли инвариантным.
    /// </summary>
    public bool IsIn { get; protected set; }

    /// <summary>
    /// Является ли ковариантным.
    /// </summary>
    public bool IsOut { get; protected set; }

    /// <summary>
    /// Ограничения типа.
    /// </summary>
    public IEnumerable<string> Limitations => limitations;

    /// <inheritdoc/>
    public override bool IsValid => !string.IsNullOrEmpty(Name);

    /// <inheritdoc/>
    protected override void OnBuildingComment([NotNull] StringBuilder sb, string spaces)
    {
        // Method intentionally left empty.
    }

    /// <inheritdoc/>
    protected override void OnBuildingAttributes([NotNull] StringBuilder sb, string spaces)
    {
        foreach (var attr in Attributes) sb.Append($"[{attr}]");
        if (sb.Length > 0) sb.Append(' ');
    }

    /// <inheritdoc/>
    protected override void OnBuildingDeclaration([NotNull] StringBuilder sb, string spaces) => sb.Append(Name);

    /// <summary>
    /// Указывает, является ли инвариантным.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    public virtual GenericEntity AsIn(bool value)
    {
        IsIn = value;
        return this;
    }

    /// <summary>
    /// Указывает, является ли инвариантным.
    /// </summary>
    public GenericEntity AsIn() => AsIn(true);

    /// <summary>
    /// Указывает, является ли ковариантным.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    public virtual GenericEntity AsOut(bool value)
    {
        IsOut = value;
        return this;
    }

    /// <summary>
    /// Указывает, является ли ковариантным.
    /// </summary>
    public GenericEntity AsOut() => AsOut(true);

    /// <summary>
    /// Добавляет ограничители.
    /// </summary>
    /// <param name="limitations">Ограничители.</param>
    public virtual GenericEntity AddLimitations([NotNull] params string[] limitations)
    {
        this.limitations.AddRange(limitations);
        return this;
    }

    /// <summary>
    /// Добавляет ограничитель.
    /// </summary>
    /// <param name="limitation">Ограничитель.</param>
    public GenericEntity AddLimitation(string limitation) => AddLimitations(limitation);

    /// <summary>
    /// Строит ограничители шаблонного типа.
    /// </summary>
    /// <param name="tabs">Число отступов.</param>
    /// <param name="release">Указывает, требуется ли высвобождать ресурсы после сборки.</param>
    public virtual string? BuildLimitations(int tabs, bool release)
    {
        if (!IsValid || limitations.IsEmpty)
        {
            if (release) Release();
            return default;
        }

        var spaces = GetSpaces(tabs);
        var sb = ObjectPool<StringBuilder>.Shared.Rent();

        sb.Append($"{spaces}where {Name} : ");
        foreach (var limitation in limitations) sb.Append($"{limitation}, ");
        sb.Remove(sb.Length - 2, 2);

        var result = sb.ToString();
        ObjectPool<StringBuilder>.Shared.Return(sb, x => x.Clear());
        if (release) Release();

        return result;
    }

    /// <summary>
    /// Строит ограничители шаблонного типа.
    /// </summary>
    /// <param name="tabs">Число отступов.</param>
    public string? BuildLimitations(int tabs) => BuildLimitations(tabs, default);

    /// <summary>
    /// Строит ограничители шаблонного типа.
    /// </summary>
    /// <param name="release">Указывает, требуется ли высвобождать ресурсы после сборки.</param>
    public string? BuildLimitations(bool release) => BuildLimitations(default, release);

    /// <summary>
    /// Строит ограничители шаблонного типа.
    /// </summary>
    public string? BuildLimitations() => BuildLimitations(default, default);

    /// <inheritdoc/>
    public override GenericEntity WithName(string name)
    {
        Name = name;
        return this;
    }

    /// <inheritdoc/>
    public override GenericEntity WithComment(string comment)
    {
        Comment = comment;
        return this;
    }

    /// <inheritdoc/>
    public override GenericEntity WithAttributes(params string[] attributes)
    {
        AddAttributes(attributes);
        return this;
    }

    /// <inheritdoc/>
    public override void Release()
    {
        base.Release();

        ObjectPool<GenericEntity>.Shared.Return(this, x =>
        {
            x.limitations.Reset();
            x.IsIn = default;
            x.IsOut = default;
        });
    }

    /// <summary>
    /// Создаёт новый экземпляр строителя шаблонных типов.
    /// </summary>
    public static GenericEntity Create() => ObjectPool<GenericEntity>.Shared.Rent();

    /// <summary>
    /// Создаёт новый экземпляр строителя шаблонных типов.
    /// </summary>
    /// <param name="name">Имя типа.</param>
    /// <param name="limitations">Ограничения типа.</param>
    public static GenericEntity Create(string name, params string[] limitations) => Create().WithName(name).AddLimitations(limitations);
}