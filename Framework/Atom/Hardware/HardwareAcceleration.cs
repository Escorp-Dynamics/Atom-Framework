namespace Atom.Hardware;

/// <summary>
/// Флаги аппаратного ускорения SIMD для конвертации цветовых пространств.
/// Позволяет явно указать, какие инструкции использовать.
/// </summary>
[Flags]
public enum HardwareAcceleration
{
    /// <summary>Без аппаратного ускорения, только скалярный код.</summary>
    None = 0,

    /// <summary>MMX — 64-bit SIMD (устаревший, для совместимости).</summary>
    Mmx = 1 << 0,

    /// <summary>SSE — 128-bit floating point SIMD.</summary>
    Sse = 1 << 1,

    /// <summary>SSE2 — 128-bit SIMD, базовые операции.</summary>
    Sse2 = 1 << 2,

    /// <summary>SSSE3 — 128-bit SIMD, shuffle и sign инструкции.</summary>
    Ssse3 = 1 << 3,

    /// <summary>SSE4.1 — 128-bit SIMD, blend, extract, insert инструкции.</summary>
    Sse41 = 1 << 4,

    /// <summary>SSE4.2 — 128-bit SIMD, строковые и CRC инструкции.</summary>
    Sse42 = 1 << 5,

    /// <summary>AVX — 256-bit floating point SIMD.</summary>
    Avx = 1 << 6,

    /// <summary>AVX2 — 256-bit integer SIMD, gather, permute.</summary>
    Avx2 = 1 << 7,

    /// <summary>FMA — Fused Multiply-Add инструкции (3 операнда).</summary>
    Fma = 1 << 8,

    /// <summary>F16C — Half-precision floating point конвертация.</summary>
    F16c = 1 << 9,

    /// <summary>AVX-512F — 512-bit SIMD foundation.</summary>
    Avx512F = 1 << 10,

    /// <summary>AVX-512BW — 512-bit byte/word операции.</summary>
    Avx512BW = 1 << 11,

    /// <summary>AVX-512VBMI — 512-bit vector byte manipulation.</summary>
    Avx512Vbmi = 1 << 12,

    /// <summary>Все доступные ускорители (выбор лучшего автоматически).</summary>
    Auto = Mmx | Sse | Sse2 | Ssse3 | Sse41 | Sse42 | Avx | Avx2 | Fma | F16c | Avx512F | Avx512BW | Avx512Vbmi,
}