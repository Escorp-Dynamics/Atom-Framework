namespace Atom.Media.ColorSpaces.Tests;

/// <summary>
/// Тесты конвертации Gray8 ↔ Bgra32.
/// Lossy конвертация: BGRA → Grayscale использует ITU-R BT.601 коэффициенты.
/// Gray8 → Bgra32: B = G = R = Value, A = 255 (lossless).
/// </summary>
[TestFixture]
public sealed class Gray8Bgra32Tests : ColorSpaceTestBase<Gray8, Bgra32>
{
    #region Properties

    /// <inheritdoc/>
    protected override string PairName => "Gray8 ↔ Bgra32";

    /// <summary>
    /// Forward (дублирование) vs Backward (BT.601 математика) — асимметричная операция.
    /// </summary>
    protected override double ForwardBackwardRatioLimit => 2.0;

    /// <inheritdoc/>
    protected override (Gray8 Source, Bgra32 Target, int Tolerance)[] ReferenceValues =>
    [
        // Gray8(Value) → Bgra32(V, V, V, 255)
        (new Gray8(0), new Bgra32(0, 0, 0, 255), 0),           // Чёрный
        (new Gray8(255), new Bgra32(255, 255, 255, 255), 0),   // Белый
        (new Gray8(128), new Bgra32(128, 128, 128, 255), 0),   // Средний серый
        (new Gray8(1), new Bgra32(1, 1, 1, 255), 0),           // Почти чёрный
        (new Gray8(254), new Bgra32(254, 254, 254, 255), 0),   // Почти белый
        (new Gray8(64), new Bgra32(64, 64, 64, 255), 0),       // Тёмный серый
        (new Gray8(192), new Bgra32(192, 192, 192, 255), 0),   // Светлый серый
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
    protected override bool EqualsSource(Gray8 a, Gray8 b, int tolerance)
        => ComponentEquals(a.Value, b.Value, tolerance);

    /// <inheritdoc/>
    protected override int GetMaxComponentError(Gray8 a, Gray8 b)
        => Math.Abs(a.Value - b.Value);

    #endregion
}
