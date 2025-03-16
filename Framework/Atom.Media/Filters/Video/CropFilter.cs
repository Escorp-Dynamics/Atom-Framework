using System.Text;
using Atom.Buffers;

namespace Atom.Media.Filters.Video;

/// <summary>
/// Эффект обрезки.
/// </summary>
public class CropFilter : VideoFilter
{
    /// <inheritdoc/>
    public override string Name { get; } = "crop";

    /// <summary>
    /// Начало обрезки по оси X.
    /// </summary>
    public string? X { get; set; }

    /// <summary>
    /// Начало обрезки по оси Y.
    /// </summary>
    public string? Y { get; set; }

    /// <summary>
    /// Конец обрезки по оси X.
    /// </summary>
    public string? Width { get; set; }

    /// <summary>
    /// Конец обрезки по оси Y.
    /// </summary>
    public string? Height { get; set; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="CropFilter"/>.
    /// </summary>
    /// <param name="duration">Длительность эффекта.</param>
    protected CropFilter(TimeSpan duration) : base(duration) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="CropFilter"/>.
    /// </summary>
    public CropFilter() : this(default) { }

    /// <inheritdoc/>
    public override string Calculate()
    {
        var sb = ObjectPool<StringBuilder>.Shared.Rent();

        var w = Width ?? "iw-100";
        var h = Height ?? "ih-100";
        var x = X ?? "50 + 4*sin(2*PI*0.1*t + PI/2) + 2*sin(2*PI*0.5*t + PI/4)";
        var y = Y ?? "50 + 4*sin(2*PI*0.2*t + PI/3) + 2*sin(2*PI*0.7*t + PI/6)";

        sb.Append(base.Calculate())
          .Append($"w={w}:")
          .Append($"h={h}:")
          .Append($"x={x}:")
          .Append($"y={y}");

        var spec = sb.ToString();
        ObjectPool<StringBuilder>.Shared.Return(sb, x => x.Clear());

        return spec;
    }
}