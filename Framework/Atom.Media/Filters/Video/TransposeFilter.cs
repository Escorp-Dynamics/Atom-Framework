using System.Text;
using Atom.Buffers;

namespace Atom.Media.Filters.Video;

/// <summary>
/// Эффект поворота.
/// </summary>
public class TransposeFilter : VideoFilter
{
    /// <inheritdoc/>
    public override string Name { get; } = "transpose";

    /// <summary>
    /// Значение поворота.
    /// </summary>
    public int Value { get; set; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="TransposeFilter"/>.
    /// </summary>
    /// <param name="duration">Длительность эффекта.</param>
    protected TransposeFilter(TimeSpan duration) : base(duration) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="TransposeFilter"/>.
    /// </summary>
    /// <param name="value">Значение поворота.</param>
    public TransposeFilter(int value) : this(TimeSpan.Zero) => Value = value;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="TransposeFilter"/>.
    /// </summary>
    public TransposeFilter() : this(0) { }

    /// <inheritdoc/>
    public override string Calculate()
    {
        var sb = ObjectPool<StringBuilder>.Shared.Rent();

        sb.Append(base.Calculate())
          .Append($"{Value}");

        var spec = sb.ToString();
        ObjectPool<StringBuilder>.Shared.Return(sb, x => x.Clear());

        return spec;
    }
}