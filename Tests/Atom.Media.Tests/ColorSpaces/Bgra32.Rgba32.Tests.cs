namespace Atom.Media.ColorSpaces.Tests;

/// <summary>
/// Тесты конвертации Bgra32 ↔ Rgba32.
/// Lossless конвертация: swap R↔B, альфа-канал сохраняется.
/// 32-bit формат — идеально для параллелизации (нет overlapping).
/// </summary>
[TestFixture]
public sealed class Bgra32Rgba32Tests : ColorSpaceTestBase<Bgra32, Rgba32>
{
    #region Properties

    /// <inheritdoc/>
    protected override string PairName => "Bgra32 ↔ Rgba32";

    /// <inheritdoc/>
    protected override (Bgra32 Source, Rgba32 Target, int Tolerance)[] ReferenceValues =>
    [
        // Bgra32(B, G, R, A) → Rgba32(R, G, B, A)
        (new Bgra32(0, 0, 0, 255), new Rgba32(0, 0, 0, 255), 0),             // Чёрный непрозрачный
        (new Bgra32(255, 255, 255, 255), new Rgba32(255, 255, 255, 255), 0), // Белый непрозрачный
        (new Bgra32(0, 0, 0, 0), new Rgba32(0, 0, 0, 0), 0),                 // Чёрный прозрачный
        (new Bgra32(0, 0, 255, 128), new Rgba32(255, 0, 0, 128), 0),         // Синий BGRA → Красный RGBA, полупрозрачный
        (new Bgra32(0, 255, 0, 255), new Rgba32(0, 255, 0, 255), 0),         // Зелёный (без изменений)
        (new Bgra32(255, 0, 0, 64), new Rgba32(0, 0, 255, 64), 0),           // Красный BGRA → Синий RGBA
        (new Bgra32(192, 64, 128, 200), new Rgba32(128, 64, 192, 200), 0),   // Произвольный с альфой
        (new Bgra32(3, 2, 1, 4), new Rgba32(1, 2, 3, 4), 0),                 // Малые значения
        (new Bgra32(252, 253, 254, 251), new Rgba32(254, 253, 252, 251), 0), // Большие значения
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
    protected override bool EqualsSource(Bgra32 a, Bgra32 b, int tolerance)
        => ComponentEquals(a.B, b.B, tolerance) &&
           ComponentEquals(a.G, b.G, tolerance) &&
           ComponentEquals(a.R, b.R, tolerance) &&
           ComponentEquals(a.A, b.A, tolerance);

    /// <inheritdoc/>
    protected override int GetMaxComponentError(Bgra32 a, Bgra32 b)
        => Math.Max(
            Math.Max(Math.Abs(a.B - b.B), Math.Abs(a.G - b.G)),
            Math.Max(Math.Abs(a.R - b.R), Math.Abs(a.A - b.A)));

    #endregion
}
