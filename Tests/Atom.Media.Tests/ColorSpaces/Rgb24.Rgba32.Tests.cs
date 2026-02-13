namespace Atom.Media.ColorSpaces.Tests;

/// <summary>
/// Тесты конвертации Rgb24 ↔ Rgba32.
/// Lossless конвертация: добавление/удаление альфа-канала.
/// </summary>
[TestFixture]
public sealed class Rgb24Rgba32Tests : ColorSpaceTestBase<Rgb24, Rgba32>
{
    #region Properties

    /// <inheritdoc/>
    protected override string PairName => "Rgb24 ↔ Rgba32";

    /// <inheritdoc/>
    protected override (Rgb24 Source, Rgba32 Target, int Tolerance)[] ReferenceValues =>
    [
        (new Rgb24(0, 0, 0), new Rgba32(0, 0, 0, 255), 0),             // Чёрный
        (new Rgb24(255, 255, 255), new Rgba32(255, 255, 255, 255), 0), // Белый
        (new Rgb24(255, 0, 0), new Rgba32(255, 0, 0, 255), 0),         // Красный
        (new Rgb24(0, 255, 0), new Rgba32(0, 255, 0, 255), 0),         // Зелёный
        (new Rgb24(0, 0, 255), new Rgba32(0, 0, 255, 255), 0),         // Синий
        (new Rgb24(128, 64, 192), new Rgba32(128, 64, 192, 255), 0),   // Произвольный
        (new Rgb24(1, 2, 3), new Rgba32(1, 2, 3, 255), 0),             // Малые значения
        (new Rgb24(254, 253, 252), new Rgba32(254, 253, 252, 255), 0), // Большие значения
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
    protected override bool EqualsSource(Rgb24 a, Rgb24 b, int tolerance)
        => ComponentEquals(a.R, b.R, tolerance) &&
           ComponentEquals(a.G, b.G, tolerance) &&
           ComponentEquals(a.B, b.B, tolerance);

    /// <inheritdoc/>
    protected override int GetMaxComponentError(Rgb24 a, Rgb24 b)
        => Math.Max(Math.Max(Math.Abs(a.R - b.R), Math.Abs(a.G - b.G)), Math.Abs(a.B - b.B));

    #endregion
}
