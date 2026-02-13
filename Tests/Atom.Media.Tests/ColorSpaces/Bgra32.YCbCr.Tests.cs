namespace Atom.Media.ColorSpaces.Tests;

/// <summary>
/// Тесты конвертации Bgra32 ↔ YCbCr.
/// Lossy конвертация: BT.601 Q16 fixed-point.
/// Направление теста: Bgra32 → YCbCr → Bgra32 (Max Err = 1).
/// </summary>
[TestFixture]
public sealed class Bgra32YCbCrTests : ColorSpaceTestBase<Bgra32, YCbCr>
{
    #region Properties

    /// <inheritdoc/>
    protected override string PairName => "Bgra32 ↔ YCbCr";

    /// <inheritdoc/>
    protected override int RoundTripTolerance => 1; // BT.601 Q16 имеет погрешности округления ±1

    /// <inheritdoc/>
    protected override HardwareAcceleration ImplementedAccelerations =>
        HardwareAcceleration.None |
        HardwareAcceleration.Sse41 |
        HardwareAcceleration.Avx2;

    /// <inheritdoc/>
    protected override (Bgra32 Source, YCbCr Target, int Tolerance)[] ReferenceValues =>
    [
        // Bgra32 (B,G,R,A) → YCbCr (Y,Cb,Cr)
        (new Bgra32(0, 0, 0, 255), new YCbCr(0, 128, 128), 1),           // Чёрный
        (new Bgra32(255, 255, 255, 255), new YCbCr(255, 128, 128), 1),   // Белый
        (new Bgra32(0, 0, 255, 255), new YCbCr(76, 85, 255), 2),         // Красный (R=255)
        (new Bgra32(0, 255, 0, 255), new YCbCr(150, 44, 21), 2),         // Зелёный (G=255)
        (new Bgra32(255, 0, 0, 255), new YCbCr(29, 255, 107), 2),        // Синий (B=255)
        (new Bgra32(128, 128, 128, 255), new YCbCr(128, 128, 128), 1),   // Серый 50%
    ];

    #endregion

    #region Conversion Methods

    /// <inheritdoc/>
    protected override void ConvertForward(ReadOnlySpan<Bgra32> source, Span<YCbCr> destination, HardwareAcceleration acceleration)
        => Bgra32.ToYCbCr(source, destination, acceleration);

    /// <inheritdoc/>
    protected override void ConvertBackward(ReadOnlySpan<YCbCr> source, Span<Bgra32> destination, HardwareAcceleration acceleration)
        => Bgra32.FromYCbCr(source, destination, acceleration);

    /// <inheritdoc/>
    protected override YCbCr ConvertSingle(Bgra32 source) => source.ToYCbCr();

    /// <inheritdoc/>
    protected override Bgra32 ConvertSingleBack(YCbCr target) => Bgra32.FromYCbCr(target);

    #endregion

    #region Comparison Methods

    /// <inheritdoc/>
    protected override bool EqualsTarget(YCbCr a, YCbCr b, int tolerance)
        => ComponentEquals(a.Y, b.Y, tolerance) &&
           ComponentEquals(a.Cb, b.Cb, tolerance) &&
           ComponentEquals(a.Cr, b.Cr, tolerance);

    /// <inheritdoc/>
    protected override bool EqualsSource(Bgra32 a, Bgra32 b, int tolerance)
        => ComponentEquals(a.B, b.B, tolerance) &&
           ComponentEquals(a.G, b.G, tolerance) &&
           ComponentEquals(a.R, b.R, tolerance) &&
           a.A == b.A; // Альфа должна сохраняться точно (255)

    /// <inheritdoc/>
    protected override int GetMaxComponentError(Bgra32 a, Bgra32 b)
        => Math.Max(Math.Max(Math.Abs(a.B - b.B), Math.Abs(a.G - b.G)), Math.Abs(a.R - b.R));

    #endregion
}
