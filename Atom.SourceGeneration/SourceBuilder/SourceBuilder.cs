using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Atom.Buffers;
using Atom.Collections;

namespace Atom.SourceGeneration;

/// <summary>
/// Представляет строителя исходного кода
/// </summary>
public class SourceBuilder : ISourceBuilder
{
    private readonly SparseArray<string> usings = new(128);
    private readonly SparseArray<IEntity> entities = new(1024);

    /// <inheritdoc/>
    public IEnumerable<string> Usings => usings;

    /// <inheritdoc/>
    public string? Namespace { get; protected set; }

    /// <inheritdoc/>
    public IEnumerable<IEntity> Entities => entities;

    /// <inheritdoc/>
    public virtual ISourceBuilder AddUsings([NotNull] params string[] ns)
    {
        usings.AddRange(ns);
        return this;
    }

    /// <inheritdoc/>
    public ISourceBuilder AddUsing(string ns) => AddUsings(ns);

    /// <inheritdoc/>
    public virtual ISourceBuilder WithNamespace(string? ns)
    {
        Namespace = ns;
        return this;
    }

    /// <inheritdoc/>
    public virtual ISourceBuilder AddEntities([NotNull] params IEntity[] entities)
    {
        this.entities.AddRange(entities);
        return this;
    }

    /// <inheritdoc/>
    public ISourceBuilder AddEntity(IEntity entity) => AddEntities(entity);

    /// <inheritdoc/>
    public ISourceBuilder AddEnum(EnumEntity entity) => AddEntity(entity);

    /// <inheritdoc/>
    public ISourceBuilder AddInterface(InterfaceEntity entity) => AddEntity(entity);

    /// <inheritdoc/>
    public ISourceBuilder AddClass(ClassEntity entity) => AddEntity(entity);

    /// <inheritdoc/>
    public string? Build(bool release)
    {
        if (!Entities.Any())
        {
            if (release) Release();
            return default;
        }

        var sb = ObjectPool<StringBuilder>.Shared.Rent();

        sb.AppendLine("#nullable enable\n");

        if (Usings.Any())
        {
            foreach (var ns in usings) sb.AppendLine(CultureInfo.InvariantCulture, $"using {ns};");
            sb.AppendLine();
        }

        if (!string.IsNullOrEmpty(Namespace)) sb.AppendLine(CultureInfo.InvariantCulture, $"namespace {Namespace};").AppendLine();

        foreach (var entity in entities)
        {
            var v = entity.Build();
            if (string.IsNullOrEmpty(v)) continue;

            sb.Append(v);
        }

        var result = sb.ToString();
        ObjectPool<StringBuilder>.Shared.Return(sb, x => x.Clear());
        if (release) Release();

        return result;
    }

    /// <inheritdoc/>
    public string? Build() => Build(default);

    /// <inheritdoc/>
    public void Release()
    {
        ObjectPool<SourceBuilder>.Shared.Return(this, x =>
        {
            foreach (var value in x.entities) value.Release();

            x.usings.Reset();
            x.entities.Reset();

            x.Namespace = string.Empty;
        });
    }

    /// <summary>
    /// Инициализирует нового строителя исходного кода.
    /// </summary>
    public static ISourceBuilder Create() => ObjectPool<SourceBuilder>.Shared.Rent();
}