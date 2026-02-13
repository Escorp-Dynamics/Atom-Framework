namespace Atom.Media.ColorSpaces.Tests;

/// <summary>
/// Тесты конвертации Gray8 ↔ YCoCgR32.
/// Gray8 → YCoCgR32: Y = Value, CoHigh = 127, CgHigh = 127, Frac = 3 (нулевая хроматичность).
/// YCoCgR32 → Gray8: берём Y-компоненту напрямую.
/// </summary>
[TestFixture]
public sealed class Gray8YCoCgR32Tests : ColorSpaceTestBase<Gray8, YCoCgR32>
{
    #region Properties

    /// <inheritdoc/>
    protected override string PairName => "Gray8 ↔ YCoCgR32";

    /// <inheritdoc/>
    protected override (Gray8 Source, YCoCgR32 Target, int Tolerance)[] ReferenceValues =>
    [
        // Gray8(Value) → YCoCgR32(Y, 127, 127, 3) — нейтральные Co/Cg
        (new Gray8(0), new YCoCgR32(0, 127, 127, 3), 0),       // Чёрный
        (new Gray8(255), new YCoCgR32(255, 127, 127, 3), 0),   // Белый
        (new Gray8(128), new YCoCgR32(128, 127, 127, 3), 0),   // Средний серый
        (new Gray8(1), new YCoCgR32(1, 127, 127, 3), 0),       // Почти чёрный
        (new Gray8(254), new YCoCgR32(254, 127, 127, 3), 0),   // Почти белый
        (new Gray8(64), new YCoCgR32(64, 127, 127, 3), 0),     // Тёмный серый
        (new Gray8(192), new YCoCgR32(192, 127, 127, 3), 0),   // Светлый серый
    ];

    #endregion

    #region Comparison Methods

    /// <inheritdoc/>
    protected override bool EqualsTarget(YCoCgR32 a, YCoCgR32 b, int tolerance)
        => ComponentEquals(a.Y, b.Y, tolerance) &&
           ComponentEquals(a.CoHigh, b.CoHigh, tolerance) &&
           ComponentEquals(a.CgHigh, b.CgHigh, tolerance) &&
           ComponentEquals(a.Frac, b.Frac, tolerance);

    /// <inheritdoc/>
    protected override bool EqualsSource(Gray8 a, Gray8 b, int tolerance)
        => ComponentEquals(a.Value, b.Value, tolerance);

    /// <inheritdoc/>
    protected override int GetMaxComponentError(Gray8 a, Gray8 b)
        => Math.Abs(a.Value - b.Value);

    #endregion
}
