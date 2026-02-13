namespace Atom.Media.ColorSpaces.Tests;

/// <summary>
/// Тесты конвертации Gray8 ↔ Cmyk.
/// Gray8 → Cmyk: C = M = Y = 0, K = 255 - Value.
/// Cmyk → Gray8: Value = 255 - K (инвертированная логика).
/// </summary>
[TestFixture]
public sealed class Gray8CmykTests : ColorSpaceTestBase<Gray8, Cmyk>
{
    #region Properties

    /// <inheritdoc/>
    protected override string PairName => "Gray8 ↔ Cmyk";

    /// <inheritdoc/>
    protected override (Gray8 Source, Cmyk Target, int Tolerance)[] ReferenceValues =>
    [
        // Gray8(Value) → Cmyk(0, 0, 0, 255 - Value)
        (new Gray8(0), new Cmyk(0, 0, 0, 255), 0),       // Чёрный → K=255
        (new Gray8(255), new Cmyk(0, 0, 0, 0), 0),       // Белый → K=0
        (new Gray8(128), new Cmyk(0, 0, 0, 127), 0),     // Средний серый
        (new Gray8(1), new Cmyk(0, 0, 0, 254), 0),       // Почти чёрный
        (new Gray8(254), new Cmyk(0, 0, 0, 1), 0),       // Почти белый
        (new Gray8(64), new Cmyk(0, 0, 0, 191), 0),      // Тёмный серый
        (new Gray8(192), new Cmyk(0, 0, 0, 63), 0),      // Светлый серый
    ];

    #endregion

    #region Comparison Methods

    /// <inheritdoc/>
    protected override bool EqualsTarget(Cmyk a, Cmyk b, int tolerance)
        => ComponentEquals(a.C, b.C, tolerance) &&
           ComponentEquals(a.M, b.M, tolerance) &&
           ComponentEquals(a.Y, b.Y, tolerance) &&
           ComponentEquals(a.K, b.K, tolerance);

    /// <inheritdoc/>
    protected override bool EqualsSource(Gray8 a, Gray8 b, int tolerance)
        => ComponentEquals(a.Value, b.Value, tolerance);

    /// <inheritdoc/>
    protected override int GetMaxComponentError(Gray8 a, Gray8 b)
        => Math.Abs(a.Value - b.Value);

    #endregion
}
