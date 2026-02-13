namespace Atom.Media.ColorSpaces.Tests;

/// <summary>
/// Тесты конвертации Bgr24 ↔ Rgba32.
/// Lossless конвертация: swap R↔B + добавление/удаление альфа-канала.
/// </summary>
[TestFixture]
public sealed class Bgr24Rgba32Tests : ColorSpaceTestBase<Bgr24, Rgba32>
{
    #region Properties

    /// <inheritdoc/>
    protected override string PairName => "Bgr24 ↔ Rgba32";

    /// <inheritdoc/>
    protected override (Bgr24 Source, Rgba32 Target, int Tolerance)[] ReferenceValues =>
    [
        // Bgr24(B, G, R) → Rgba32(R, G, B, 255)
        (new Bgr24(0, 0, 0), new Rgba32(0, 0, 0, 255), 0),             // Чёрный
        (new Bgr24(255, 255, 255), new Rgba32(255, 255, 255, 255), 0), // Белый
        (new Bgr24(0, 0, 255), new Rgba32(255, 0, 0, 255), 0),         // Синий BGR → Красный RGBA
        (new Bgr24(0, 255, 0), new Rgba32(0, 255, 0, 255), 0),         // Зелёный (без изменений)
        (new Bgr24(255, 0, 0), new Rgba32(0, 0, 255, 255), 0),         // Красный BGR → Синий RGBA
        (new Bgr24(192, 64, 128), new Rgba32(128, 64, 192, 255), 0),   // Произвольный: B=192,G=64,R=128 → R=128,G=64,B=192
        (new Bgr24(3, 2, 1), new Rgba32(1, 2, 3, 255), 0),             // Малые значения
        (new Bgr24(252, 253, 254), new Rgba32(254, 253, 252, 255), 0), // Большие значения
    ];

    #endregion

    #region Comparison Methods

    /// <inheritdoc/>
    protected override bool EqualsTarget(Rgba32 a, Rgba32 b, int tolerance)
        => ComponentEquals(a.R, b.R, tolerance) &&
           ComponentEquals(a.G, b.G, tolerance) &&
           ComponentEquals(a.B, b.B, tolerance) &&
           ComponentEquals(a.A, b.A, tolerance);

    /// <inheritdoc/>
    protected override bool EqualsSource(Bgr24 a, Bgr24 b, int tolerance)
        => ComponentEquals(a.B, b.B, tolerance) &&
           ComponentEquals(a.G, b.G, tolerance) &&
           ComponentEquals(a.R, b.R, tolerance);

    /// <inheritdoc/>
    protected override int GetMaxComponentError(Bgr24 a, Bgr24 b)
        => Math.Max(Math.Max(Math.Abs(a.B - b.B), Math.Abs(a.G - b.G)), Math.Abs(a.R - b.R));

    #endregion
}
