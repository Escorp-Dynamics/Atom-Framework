using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Atom.Buffers;
using Atom.Collections;
using Atom.Text;

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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override void OnBuildingComment(ref ValueStringBuilder sb, string spaces)
    {
        // Method intentionally left empty.
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override void OnBuildingAttributes(ref ValueStringBuilder sb, string spaces)
    {
        foreach (var attr in Attributes) sb.Append($"[{attr}]");
        if (sb.Length > 0) sb.Append(' ');
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override void OnBuildingDeclaration(ref ValueStringBuilder sb, string spaces, params IEnumerable<string> usings) => sb.Append(Name);

    /// <summary>
    /// Указывает, является ли инвариантным.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual GenericEntity AsIn(bool value)
    {
        IsIn = value;
        return this;
    }

    /// <summary>
    /// Указывает, является ли инвариантным.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public GenericEntity AsIn() => AsIn(value: true);

    /// <summary>
    /// Указывает, является ли ковариантным.
    /// </summary>
    /// <param name="value">Значение свойства.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual GenericEntity AsOut(bool value)
    {
        IsOut = value;
        return this;
    }

    /// <summary>
    /// Указывает, является ли ковариантным.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public GenericEntity AsOut() => AsOut(value: true);

    /// <summary>
    /// Добавляет ограничители.
    /// </summary>
    /// <param name="limitations">Ограничители.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual GenericEntity WithLimitation([NotNull] params IEnumerable<string> limitations)
    {
        this.limitations.AddRange(limitations);
        return this;
    }

    /// <summary>
    /// Строит ограничители шаблонного типа.
    /// </summary>
    /// <param name="tabs">Число отступов.</param>
    /// <param name="release">Указывает, требуется ли высвобождать ресурсы после сборки.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual string? BuildLimitations(int tabs, bool release)
    {
        if (!IsValid || limitations.IsEmpty)
        {
            if (release) Release();
            return default;
        }

        var spaces = GetSpaces(tabs);
        using var sb = new ValueStringBuilder();

        sb.Append($"{spaces}where {Name} : ");
        foreach (var limitation in limitations) sb.Append($"{limitation}, ");
        sb.Remove(sb.Length - 2, 2);

        var result = sb.ToString();
        if (release) Release();

        return result;
    }

    /// <summary>
    /// Строит ограничители шаблонного типа.
    /// </summary>
    /// <param name="tabs">Число отступов.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string? BuildLimitations(int tabs) => BuildLimitations(tabs, default);

    /// <summary>
    /// Строит ограничители шаблонного типа.
    /// </summary>
    /// <param name="release">Указывает, требуется ли высвобождать ресурсы после сборки.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string? BuildLimitations(bool release) => BuildLimitations(default, release);

    /// <summary>
    /// Строит ограничители шаблонного типа.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string? BuildLimitations() => BuildLimitations(default, default);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override GenericEntity WithName(string name)
    {
        Name = name;
        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override GenericEntity WithComment(string? comment)
    {
        Comment = comment;
        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override GenericEntity WithAttribute(params IEnumerable<string> attributes)
    {
        AddAttribute(attributes);
        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static GenericEntity Create() => ObjectPool<GenericEntity>.Shared.Rent();

    /// <summary>
    /// Создаёт новый экземпляр строителя шаблонных типов.
    /// </summary>
    /// <param name="name">Имя типа.</param>
    /// <param name="limitations">Ограничения типа.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static GenericEntity Create(string name, params IEnumerable<string> limitations) => Create().WithName(name).WithLimitation(limitations);
}