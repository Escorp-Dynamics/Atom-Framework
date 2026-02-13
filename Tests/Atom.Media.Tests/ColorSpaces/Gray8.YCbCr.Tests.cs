namespace Atom.Media.ColorSpaces.Tests;

/// <summary>
/// Тесты конвертации Gray8 ↔ YCbCr.
/// Gray8 → YCbCr: Y = Value, Cb = Cr = 128 (нейтральная хроматичность).
/// YCbCr → Gray8: берём Y-компоненту напрямую.
/// </summary>
[TestFixture]
public sealed class Gray8YCbCrTests : ColorSpaceTestBase<Gray8, YCbCr>
{
    #region Properties

    /// <inheritdoc/>
    protected override string PairName => "Gray8 ↔ YCbCr";

    /// <inheritdoc/>
    protected override (Gray8 Source, YCbCr Target, int Tolerance)[] ReferenceValues =>
    [
        // Gray8(Value) → YCbCr(Y, 128, 128)
        (new Gray8(0), new YCbCr(0, 128, 128), 0),       // Чёрный
        (new Gray8(255), new YCbCr(255, 128, 128), 0),   // Белый
        (new Gray8(128), new YCbCr(128, 128, 128), 0),   // Средний серый
        (new Gray8(16), new YCbCr(16, 128, 128), 0),     // TV black (studio)
        (new Gray8(235), new YCbCr(235, 128, 128), 0),   // TV white (studio)
        (new Gray8(1), new YCbCr(1, 128, 128), 0),       // Почти чёрный
        (new Gray8(254), new YCbCr(254, 128, 128), 0),   // Почти белый
    ];

    #endregion

    #region Comparison Methods

    /// <inheritdoc/>
    protected override bool EqualsTarget(YCbCr a, YCbCr b, int tolerance)
        => ComponentEquals(a.Y, b.Y, tolerance) &&
           ComponentEquals(a.Cb, b.Cb, tolerance) &&
           ComponentEquals(a.Cr, b.Cr, tolerance);

    /// <inheritdoc/>
    protected override bool EqualsSource(Gray8 a, Gray8 b, int tolerance)
        => ComponentEquals(a.Value, b.Value, tolerance);

    /// <inheritdoc/>
    protected override int GetMaxComponentError(Gray8 a, Gray8 b)
        => Math.Abs(a.Value - b.Value);

    #endregion
}
