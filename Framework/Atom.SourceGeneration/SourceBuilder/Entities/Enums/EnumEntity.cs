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
    protected override void OnBuildingDeclaration([NotNull] StringBuilder sb, [NotNull] string spaces, params IEnumerable<string> usings)
    {
        var type = string.IsNullOrEmpty(Type) || Type is "int" or "Int32" ? string.Empty : $" : {Type}";

        sb.AppendLine($"{spaces}{AccessModifier.AsString()} enum {Name}{type}")
          .AppendLine($"{spaces}{{");

        var subTabs = (spaces.Length / 4) + 1;

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
    public override EnumEntity WithAttribute([NotNull] params IEnumerable<string> attributes)
    {
        AddAttribute(attributes);
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
    public virtual EnumEntity WithType<TType>(bool withNamespaces = true, bool withNullable = true, bool withGenericNullable = true) => WithType(typeof(TType).GetFriendlyName(withNamespaces, withNullable, withGenericNullable));

    /// <inheritdoc/>
    public virtual EnumEntity AsFlags()
    {
        AddAttribute("Flags");
        return this;
    }

    /// <inheritdoc/>
    public virtual EnumEntity WithValue([NotNull] params IEnumerable<EnumMember> values)
    {
        this.values.AddRange(values);
        return this;
    }

    /// <inheritdoc/>
    public EnumEntity WithValue(string name, long value, string comment, params IEnumerable<string> attributes) => WithValue(EnumMember.Create()
        .WithName(name)
        .WithValue(value)
        .WithComment(comment)
        .WithAttribute(attributes)
    );

    /// <inheritdoc/>
    public EnumEntity WithValue(string name, long value) => WithValue(name, value, string.Empty);

    /// <inheritdoc/>
    public EnumEntity WithValue(string name) => WithValue(name, -1);

    /// <inheritdoc/>
    public EnumEntity WithValue(string name, string comment, params IEnumerable<string> attributes) => WithValue(name, -1, comment, attributes);

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