namespace Atom.Media.ColorSpaces.Tests;

/// <summary>
/// Тесты конвертации Gray8 ↔ Hsv.
/// Gray8 → Hsv: H = 0, S = 0, V = Value (ахроматический).
/// Hsv → Gray8: берём V-компоненту напрямую.
/// </summary>
[TestFixture]
public sealed class Gray8HsvTests : ColorSpaceTestBase<Gray8, Hsv>
{
    #region Properties

    /// <inheritdoc/>
    protected override string PairName => "Gray8 ↔ Hsv";

    /// <inheritdoc/>
    protected override (Gray8 Source, Hsv Target, int Tolerance)[] ReferenceValues =>
    [
        // Gray8(Value) → Hsv(0, 0, V)
        (new Gray8(0), new Hsv(0, 0, 0), 0),         // Чёрный
        (new Gray8(255), new Hsv(0, 0, 255), 0),     // Белый
        (new Gray8(128), new Hsv(0, 0, 128), 0),     // Средний серый
        (new Gray8(1), new Hsv(0, 0, 1), 0),         // Почти чёрный
        (new Gray8(254), new Hsv(0, 0, 254), 0),     // Почти белый
        (new Gray8(64), new Hsv(0, 0, 64), 0),       // Тёмный серый
        (new Gray8(192), new Hsv(0, 0, 192), 0),     // Светлый серый
    ];

    #endregion

    #region Comparison Methods

    /// <inheritdoc/>
    protected override bool EqualsTarget(Hsv a, Hsv b, int tolerance)
        => ComponentEquals((byte)(a.H >> 8), (byte)(b.H >> 8), tolerance) &&
           ComponentEquals(a.S, b.S, tolerance) &&
           ComponentEquals(a.V, b.V, tolerance);

    /// <inheritdoc/>
    protected override bool EqualsSource(Gray8 a, Gray8 b, int tolerance)
        => ComponentEquals(a.Value, b.Value, tolerance);

    /// <inheritdoc/>
    protected override int GetMaxComponentError(Gray8 a, Gray8 b)
        => Math.Abs(a.Value - b.Value);

    #endregion
}
