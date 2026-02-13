namespace Atom.Media.ColorSpaces.Tests;

/// <summary>
/// Тесты конвертации Gray8 ↔ Bgr24.
/// Lossy конвертация: BGR → Grayscale использует ITU-R BT.601 коэффициенты.
/// Gray8 → Bgr24: B = G = R = Value (lossless).
/// </summary>
[TestFixture]
public sealed class Gray8Bgr24Tests : ColorSpaceTestBase<Gray8, Bgr24>
{
    #region Properties

    /// <inheritdoc/>
    protected override string PairName => "Gray8 ↔ Bgr24";

    /// <summary>
    /// Forward (дублирование) vs Backward (BT.601 математика) — асимметричная операция.
    /// </summary>
    protected override double ForwardBackwardRatioLimit => 2.0;

    /// <inheritdoc/>
    protected override (Gray8 Source, Bgr24 Target, int Tolerance)[] ReferenceValues =>
    [
        // Gray8(Value) → Bgr24(V, V, V)
        (new Gray8(0), new Bgr24(0, 0, 0), 0),           // Чёрный
        (new Gray8(255), new Bgr24(255, 255, 255), 0),   // Белый
        (new Gray8(128), new Bgr24(128, 128, 128), 0),   // Средний серый
        (new Gray8(1), new Bgr24(1, 1, 1), 0),           // Почти чёрный
        (new Gray8(254), new Bgr24(254, 254, 254), 0),   // Почти белый
        (new Gray8(64), new Bgr24(64, 64, 64), 0),       // Тёмный серый
        (new Gray8(192), new Bgr24(192, 192, 192), 0),   // Светлый серый
    ];

    #endregion

    #region Comparison Methods

    /// <inheritdoc/>
    protected override bool EqualsTarget(Bgr24 a, Bgr24 b, int tolerance)
        => ComponentEquals(a.B, b.B, tolerance) &&
           ComponentEquals(a.G, b.G, tolerance) &&
           ComponentEquals(a.R, b.R, tolerance);

    /// <inheritdoc/>
    protected override bool EqualsSource(Gray8 a, Gray8 b, int tolerance)
        => ComponentEquals(a.Value, b.Value, tolerance);

    /// <inheritdoc/>
    protected override int GetMaxComponentError(Gray8 a, Gray8 b)
        => Math.Abs(a.Value - b.Value);

    #endregion
}
