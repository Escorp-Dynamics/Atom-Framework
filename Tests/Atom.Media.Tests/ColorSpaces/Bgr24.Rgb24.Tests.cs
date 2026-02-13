namespace Atom.Media.ColorSpaces.Tests;

/// <summary>
/// Тесты конвертации Bgr24 ↔ Rgb24.
/// Lossless конвертация: чистый swap B↔R (G остаётся на месте).
/// 24-bit формат с параллелизацией.
/// </summary>
[TestFixture]
public sealed class Bgr24Rgb24Tests : ColorSpaceTestBase<Bgr24, Rgb24>
{
    #region Properties

    /// <inheritdoc/>
    protected override string PairName => "Bgr24 ↔ Rgb24";

    /// <inheritdoc/>
    protected override (Bgr24 Source, Rgb24 Target, int Tolerance)[] ReferenceValues =>
    [
        // Bgr24(B, G, R) → Rgb24(R, G, B)
        (new Bgr24(0, 0, 0), new Rgb24(0, 0, 0), 0),                   // Чёрный
        (new Bgr24(255, 255, 255), new Rgb24(255, 255, 255), 0),       // Белый
        (new Bgr24(0, 0, 255), new Rgb24(255, 0, 0), 0),               // R=255 → красный
        (new Bgr24(0, 255, 0), new Rgb24(0, 255, 0), 0),               // Зелёный (без изменений)
        (new Bgr24(255, 0, 0), new Rgb24(0, 0, 255), 0),               // B=255 → синий
        (new Bgr24(192, 64, 128), new Rgb24(128, 64, 192), 0),         // Произвольный
        (new Bgr24(3, 2, 1), new Rgb24(1, 2, 3), 0),                   // Малые значения
        (new Bgr24(252, 253, 254), new Rgb24(254, 253, 252), 0),       // Большие значения
    ];

    #endregion

    #region Comparison Methods

    /// <inheritdoc/>
    protected override bool EqualsTarget(Rgb24 a, Rgb24 b, int tolerance)
        => ComponentEquals(a.R, b.R, tolerance) &&
           ComponentEquals(a.G, b.G, tolerance) &&
           ComponentEquals(a.B, b.B, tolerance);

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
