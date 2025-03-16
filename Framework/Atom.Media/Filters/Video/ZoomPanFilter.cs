using System.Globalization;
using System.Text;
using Atom.Buffers;

namespace Atom.Media.Filters.Video;

/// <summary>
/// Представляет фильтр масштабирования видео.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="ZoomPanFilter"/>.
/// </remarks>
/// <param name="from">Начальное значение.</param>
/// <param name="to">Конечное значение.</param>
/// <param name="duration">Длительность эффекта.</param>
public class ZoomPanFilter(float from, float to, TimeSpan duration) : VideoFilter(duration)
{
    /// <inheritdoc/>
    public override string Name { get; } = "zoompan";

    /// <summary>
    /// Начальное значение.
    /// </summary>
    public float From { get; set; } = from;

    /// <summary>
    /// Конечное значение.
    /// </summary>
    public float To { get; set; } = to;

    /// <summary>
    /// Смещение по оси X.
    /// </summary>
    public string? X { get; set; }

    /// <summary>
    /// Смещение по оси Y.
    /// </summary>
    public string? Y { get; set; }

    /// <inheritdoc/>
    public override string Calculate()
    {
        var sb = ObjectPool<StringBuilder>.Shared.Rent();

        var x = X ?? "iw/2-(iw/zoom/2)";
        var y = Y ?? "ih/2-(ih/zoom/2)";
        var d = Duration.TotalSeconds.ToString(CultureInfo.InvariantCulture);
        var sign = To > From ? "+" : "-";
        var func = To > From ? "min" : "max";
        var zoom = From.ToString(CultureInfo.InvariantCulture);
        var value = To.ToString(CultureInfo.InvariantCulture);

        if (!From.Equals(To)) zoom = $"if(lte(time,{d}),{func}({zoom}{sign}(time/{d}),{value}),{value})";

        sb.Append(base.Calculate())
          .Append($"z='{zoom}':")
          .Append("d=1:")
          .Append($"x='{x}':")
          .Append($"y='{y}':")
          .Append($"fps={FrameRate}:")
          .Append($"s={Resolution.Width}x{Resolution.Height}");

        var spec = sb.ToString();
        ObjectPool<StringBuilder>.Shared.Return(sb, x => x.Clear());

        return spec;
    }
}