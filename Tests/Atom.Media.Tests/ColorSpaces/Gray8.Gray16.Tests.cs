namespace Atom.Media.ColorSpaces.Tests;

/// <summary>
/// Тесты конвертации Gray8 ↔ Gray16.
/// Gray8 → Gray16: Value16 = Value8 * 257 (точное масштабирование 0-255 → 0-65535).
/// Gray16 → Gray8: Value8 = Value16 >> 8 (потеря младших 8 бит).
/// </summary>
[TestFixture]
public sealed class Gray8Gray16Tests : ColorSpaceTestBase<Gray8, Gray16>
{
    #region Properties

    /// <inheritdoc/>
    protected override string PairName => "Gray8 ↔ Gray16";

    /// <inheritdoc/>
    protected override (Gray8 Source, Gray16 Target, int Tolerance)[] ReferenceValues =>
    [
        // Gray8(Value) → Gray16(Value * 257)
        (new Gray8(0), new Gray16(0), 0),                     // Чёрный
        (new Gray8(255), new Gray16(65535), 0),               // Белый (255 * 257 = 65535)
        (new Gray8(128), new Gray16(32896), 0),               // Средний серый (128 * 257 = 32896)
        (new Gray8(1), new Gray16(257), 0),                   // Почти чёрный
        (new Gray8(254), new Gray16(65278), 0),               // Почти белый (254 * 257 = 65278)
        (new Gray8(127), new Gray16(32639), 0),               // 127 * 257 = 32639
        (new Gray8(64), new Gray16(16448), 0),                // 64 * 257 = 16448
    ];

    #endregion

    #region Comparison Methods

    /// <inheritdoc/>
    protected override bool EqualsTarget(Gray16 a, Gray16 b, int tolerance)
        => Math.Abs(a.Value - b.Value) <= tolerance;

    /// <inheritdoc/>
    protected override bool EqualsSource(Gray8 a, Gray8 b, int tolerance)
        => ComponentEquals(a.Value, b.Value, tolerance);

    /// <inheritdoc/>
    protected override int GetMaxComponentError(Gray8 a, Gray8 b)
        => Math.Abs(a.Value - b.Value);

    #endregion
}
