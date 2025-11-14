using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Atom.Buffers;
using Atom.Collections;

namespace Atom.SourceGeneration;

/// <summary>
/// Представляет строителя исходного кода
/// </summary>
public class SourceBuilder : ISourceBuilder
{
    private readonly SparseArray<string> directives = new(128);
    private readonly SparseArray<string> usings = new(128);
    private readonly SparseArray<IEntity> entities = new(1024);

    /// <inheritdoc/>
    public IEnumerable<string> Directives => directives;

    /// <inheritdoc/>
    public IEnumerable<string> Usings => usings;

    /// <inheritdoc/>
    public string? Namespace { get; protected set; }

    /// <inheritdoc/>
    public IEnumerable<IEntity> Entities => entities;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ISourceBuilder WithDirective(params IEnumerable<string> directives)
    {
        this.directives.AddRange(directives);
        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual ISourceBuilder WithUsing(params IEnumerable<string> ns)
    {
        usings.AddRange(ns);
        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual ISourceBuilder WithNamespace(string? ns)
    {
        Namespace = ns;
        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual ISourceBuilder WithEntity(params IEnumerable<IEntity> entities)
    {
        this.entities.AddRange(entities);
        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ISourceBuilder WithEnum(params IEnumerable<EnumEntity> enums) => WithEntity(enums);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ISourceBuilder WithInterface(params IEnumerable<InterfaceEntity> interfaces) => WithEntity(interfaces);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ISourceBuilder WithClass(params IEnumerable<ClassEntity> classes) => WithEntity(classes);

    /// <inheritdoc/>
    public string? Build(bool release)
    {
        if (!Entities.Any())
        {
            if (release) Release();
            return default;
        }

        var sb = ObjectPool<StringBuilder>.Shared.Rent();

        sb.AppendLine("#nullable enable");

        if (Directives.Any())
        {
            foreach (var directive in directives) sb.AppendLine(directive);
        }

        sb.AppendLine();

        if (Usings.Any())
        {
            foreach (var ns in usings) sb.AppendLine(CultureInfo.InvariantCulture, $"using {ns};");
            sb.AppendLine();
        }

        if (!string.IsNullOrEmpty(Namespace)) sb.AppendLine(CultureInfo.InvariantCulture, $"namespace {Namespace};").AppendLine();

        var tmp = ObjectPool<List<string>>.Shared.Rent();
        tmp.AddRange(usings);

        if (!string.IsNullOrEmpty(Namespace)) tmp.Add(Namespace);

        string[] allUsings = [.. tmp.Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.Ordinal).OrderByDescending(x => x.Length)];
        ObjectPool<List<string>>.Shared.Return(tmp, x => x.Clear());

        foreach (var entity in entities)
        {
            var v = entity.Build(allUsings);
            if (string.IsNullOrEmpty(v)) continue;

            sb.AppendLine(v);
        }

        var result = sb.ToString().Trim();
        ObjectPool<StringBuilder>.Shared.Return(sb, x => x.Clear());
        if (release) Release();

        return result;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string? Build() => Build(default);

    /// <inheritdoc/>
    public void Release()
    {
        ObjectPool<SourceBuilder>.Shared.Return(this, x =>
        {
            foreach (var value in x.entities) value.Release();

            x.directives.Reset();
            x.usings.Reset();
            x.entities.Reset();

            x.Namespace = string.Empty;
        });
    }

    /// <summary>
    /// Инициализирует нового строителя исходного кода.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ISourceBuilder Create() => ObjectPool<SourceBuilder>.Shared.Rent()
        .WithUsing("System",
            "System.Collections",
            "System.Collections.Generic",
            "System.Collections.Concurrent",
            "System.Threading",
            "System.Threading.Tasks",
            "Atom");
}
