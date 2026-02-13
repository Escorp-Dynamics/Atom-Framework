namespace Atom.Media.ColorSpaces.Tests;

/// <summary>
/// Тесты конвертации Rgb24 ↔ Bgra32.
/// Lossless конвертация: swap R↔B + добавление альфа-канала.
/// </summary>
[TestFixture]
public sealed class Rgb24Bgra32Tests : ColorSpaceTestBase<Rgb24, Bgra32>
{
    #region Properties

    /// <inheritdoc/>
    protected override string PairName => "Rgb24 ↔ Bgra32";

    /// <inheritdoc/>
    protected override (Rgb24 Source, Bgra32 Target, int Tolerance)[] ReferenceValues =>
    [
        // Rgb24(R, G, B) → Bgra32(B, G, R, 255)
        (new Rgb24(0, 0, 0), new Bgra32(0, 0, 0, 255), 0),             // Чёрный
        (new Rgb24(255, 255, 255), new Bgra32(255, 255, 255, 255), 0), // Белый
        (new Rgb24(255, 0, 0), new Bgra32(0, 0, 255, 255), 0),         // Красный RGB → R в BGRA
        (new Rgb24(0, 255, 0), new Bgra32(0, 255, 0, 255), 0),         // Зелёный
        (new Rgb24(0, 0, 255), new Bgra32(255, 0, 0, 255), 0),         // Синий RGB → B в BGRA
        (new Rgb24(128, 64, 192), new Bgra32(192, 64, 128, 255), 0),   // Произвольный
    ];

    #endregion

    #region Comparison Methods

    /// <inheritdoc/>
    protected override bool EqualsTarget(Bgra32 a, Bgra32 b, int tolerance)
        => ComponentEquals(a.B, b.B, tolerance) &&
           ComponentEquals(a.G, b.G, tolerance) &&
           ComponentEquals(a.R, b.R, tolerance) &&
           ComponentEquals(a.A, b.A, tolerance);

    /// <inheritdoc/>
    protected override bool EqualsSource(Rgb24 a, Rgb24 b, int tolerance)
        => ComponentEquals(a.R, b.R, tolerance) &&
           ComponentEquals(a.G, b.G, tolerance) &&
           ComponentEquals(a.B, b.B, tolerance);

    /// <inheritdoc/>
    protected override int GetMaxComponentError(Rgb24 a, Rgb24 b)
        => Math.Max(Math.Max(Math.Abs(a.R - b.R), Math.Abs(a.G - b.G)), Math.Abs(a.B - b.B));

    #endregion
}
