namespace Atom.Media.ColorSpaces.Tests;

/// <summary>
/// Тесты конвертации Bgr24 ↔ Bgra32.
/// Lossless конвертация: добавление/удаление альфа-канала.
/// </summary>
[TestFixture]
public sealed class Bgr24Bgra32Tests : ColorSpaceTestBase<Bgr24, Bgra32>
{
    #region Properties

    /// <inheritdoc/>
    protected override string PairName => "Bgr24 ↔ Bgra32";

    /// <inheritdoc/>
    protected override (Bgr24 Source, Bgra32 Target, int Tolerance)[] ReferenceValues =>
    [
        (new Bgr24(0, 0, 0), new Bgra32(0, 0, 0, 255), 0),             // Чёрный
        (new Bgr24(255, 255, 255), new Bgra32(255, 255, 255, 255), 0), // Белый
        (new Bgr24(255, 0, 0), new Bgra32(255, 0, 0, 255), 0),         // Синий (B=255)
        (new Bgr24(0, 255, 0), new Bgra32(0, 255, 0, 255), 0),         // Зелёный
        (new Bgr24(0, 0, 255), new Bgra32(0, 0, 255, 255), 0),         // Красный (R=255)
        (new Bgr24(128, 64, 192), new Bgra32(128, 64, 192, 255), 0),   // Произвольный
        (new Bgr24(1, 2, 3), new Bgra32(1, 2, 3, 255), 0),             // Малые значения
        (new Bgr24(254, 253, 252), new Bgra32(254, 253, 252, 255), 0), // Большие значения
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
    protected override bool EqualsSource(Bgr24 a, Bgr24 b, int tolerance)
        => ComponentEquals(a.B, b.B, tolerance) &&
           ComponentEquals(a.G, b.G, tolerance) &&
           ComponentEquals(a.R, b.R, tolerance);

    /// <inheritdoc/>
    protected override int GetMaxComponentError(Bgr24 a, Bgr24 b)
        => Math.Max(Math.Max(Math.Abs(a.B - b.B), Math.Abs(a.G - b.G)), Math.Abs(a.R - b.R));

    #endregion
}
