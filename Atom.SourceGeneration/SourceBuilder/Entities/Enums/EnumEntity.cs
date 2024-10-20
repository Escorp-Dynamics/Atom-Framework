using System.Diagnostics.CodeAnalysis;
using System.Text;
using Atom.Buffers;
using Atom.Collections;

namespace Atom.SourceGeneration;

/// <summary>
/// Представляет строителя для <c>enum</c>.
/// </summary>
public class EnumEntity : Entity<EnumEntity>, IEnumEntity<EnumEntity>
{
    private readonly SparseArray<EnumMember> values = new(1024);

    /// <inheritdoc/>
    public AccessModifier AccessModifier { get; set; }

    /// <inheritdoc/>
    public string Type { get; protected set; } = string.Empty;

    /// <inheritdoc/>
    public IEnumerable<EnumMember> Values => values;

    /// <inheritdoc/>
    public bool IsFlags => Attributes.Contains("Flags");

    /// <inheritdoc/>
    public override bool IsValid => !string.IsNullOrEmpty(Name) && values.CurrentIndex >= 0;

    /// <inheritdoc/>
    protected override void OnBuildingDeclaration([NotNull] StringBuilder sb, [NotNull] string spaces)
    {
        var type = string.IsNullOrEmpty(Type) || Type is "int" or "Int32" ? string.Empty : $" : {Type}";

        sb.AppendLine($"{spaces}{AccessModifier.AsString()} enum {Name}{type}")
          .AppendLine($"{spaces}{{");

        var subTabs = spaces.Length / 4 + 1;

        foreach (var value in Values)
        {
            var v = value.Build(subTabs);
            if (string.IsNullOrEmpty(v)) continue;

            sb.Append(v);
        }

        sb.AppendLine($"{spaces}}}");
    }

    /// <inheritdoc/>
    public override EnumEntity WithComment(string? comment)
    {
        Comment = comment;
        return this;
    }

    /// <inheritdoc/>
    public override EnumEntity WithAttributes([NotNull] params string[] attributes)
    {
        AddAttributes(attributes);
        return this;
    }

    /// <inheritdoc/>
    public EnumEntity WithAccessModifier(AccessModifier modifier)
    {
        AccessModifier = modifier;
        return this;
    }

    /// <inheritdoc/>
    public override EnumEntity WithName(string name)
    {
        Name = name;
        return this;
    }

    /// <inheritdoc/>
    public virtual EnumEntity WithType(string type)
    {
        Type = type;
        return this;
    }

    /// <inheritdoc/>
    public virtual EnumEntity WithType<TType>()
    {
        Type = typeof(TType).Name;
        return this;
    }

    /// <inheritdoc/>
    public virtual EnumEntity AsFlags()
    {
        AddAttribute("Flags");
        return this;
    }

    /// <inheritdoc/>
    public virtual EnumEntity AddValues([NotNull] params EnumMember[] values)
    {
        this.values.AddRange(values);
        return this;
    }

    /// <inheritdoc/>
    public EnumEntity AddValue(EnumMember value) => AddValues(value);

    /// <inheritdoc/>
    public EnumEntity AddValue(string name, long value, string comment, params string[] attributes) => AddValue(EnumMember.Create()
        .WithName(name)
        .WithValue(value)
        .WithComment(comment)
        .WithAttributes(attributes)
    );

    /// <inheritdoc/>
    public EnumEntity AddValue(string name, long value) => AddValue(name, value, string.Empty);

    /// <inheritdoc/>
    public EnumEntity AddValue(string name) => AddValue(name, -1);

    /// <inheritdoc/>
    public EnumEntity AddValue(string name, string comment, params string[] attributes) => AddValue(name, -1, comment, attributes);

    /// <inheritdoc/>
    public override void Release()
    {
        base.Release();

        ObjectPool<EnumEntity>.Shared.Return(this, x =>
        {
            foreach (var value in x.values) value.Release();

            x.values.Reset();

            x.AccessModifier = default;
            x.Type = string.Empty;
        });
    }

    /// <summary>
    /// Инициализирует нового строителя для <c>enum</c>.
    /// </summary>
    public static EnumEntity Create() => ObjectPool<EnumEntity>.Shared.Rent();
}