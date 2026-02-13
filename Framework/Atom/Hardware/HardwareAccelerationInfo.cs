using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.X86;

namespace Atom.Hardware;

/// <summary>
/// Статические утилиты для работы с аппаратным ускорением.
/// </summary>
public static class HardwareAccelerationInfo
{
    /// <summary>
    /// Флаги ускорителей, поддерживаемых текущим CPU.
    /// Кешируется при первом обращении.
    /// </summary>
    public static HardwareAcceleration Supported { get; } = DetectSupported();

    /// <summary>
    /// Выбирает лучший доступный ускоритель из запрошенных.
    /// </summary>
    /// <param name="requested">Запрошенные флаги ускорения.</param>
    /// <returns>Лучший доступный ускоритель (один флаг) или Scalar.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HardwareAcceleration SelectBest(HardwareAcceleration requested)
    {
        // Пересекаем запрошенные с поддерживаемыми
        var available = requested & Supported;

        // Возвращаем самый мощный (наивысший бит)
        if (available.HasFlag(HardwareAcceleration.Avx512Vbmi))
            return HardwareAcceleration.Avx512Vbmi;

        if (available.HasFlag(HardwareAcceleration.Avx512BW))
            return HardwareAcceleration.Avx512BW;

        if (available.HasFlag(HardwareAcceleration.Avx512F))
            return HardwareAcceleration.Avx512F;

        if (available.HasFlag(HardwareAcceleration.F16c))
            return HardwareAcceleration.F16c;

        if (available.HasFlag(HardwareAcceleration.Fma))
            return HardwareAcceleration.Fma;

        if (available.HasFlag(HardwareAcceleration.Avx2))
            return HardwareAcceleration.Avx2;

        if (available.HasFlag(HardwareAcceleration.Avx))
            return HardwareAcceleration.Avx;

        if (available.HasFlag(HardwareAcceleration.Sse42))
            return HardwareAcceleration.Sse42;

        if (available.HasFlag(HardwareAcceleration.Sse41))
            return HardwareAcceleration.Sse41;

        if (available.HasFlag(HardwareAcceleration.Ssse3))
            return HardwareAcceleration.Ssse3;

        if (available.HasFlag(HardwareAcceleration.Sse2))
            return HardwareAcceleration.Sse2;

        if (available.HasFlag(HardwareAcceleration.Sse))
            return HardwareAcceleration.Sse;

        if (available.HasFlag(HardwareAcceleration.Mmx))
            return HardwareAcceleration.Mmx;

        return HardwareAcceleration.None;
    }

    /// <summary>
    /// Проверяет, доступен ли указанный ускоритель на текущем CPU.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsSupported(HardwareAcceleration acceleration) =>
        (Supported & acceleration) == acceleration;

    /// <summary>
    /// Возвращает минимальный размер буфера для эффективного использования ускорителя.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetMinBatchSize(HardwareAcceleration acceleration) => acceleration switch
    {
        HardwareAcceleration.Avx512Vbmi or HardwareAcceleration.Avx512BW or HardwareAcceleration.Avx512F => 64,
        HardwareAcceleration.Avx2 or HardwareAcceleration.Avx or HardwareAcceleration.Fma or HardwareAcceleration.F16c => 32,
        HardwareAcceleration.Sse42 or HardwareAcceleration.Sse41 or HardwareAcceleration.Ssse3 or HardwareAcceleration.Sse2 or HardwareAcceleration.Sse => 16,
        HardwareAcceleration.Mmx => 8,
        _ => 1,
    };

    /// <summary>
    /// Определяет поддерживаемые ускорители CPU.
    /// </summary>
    private static HardwareAcceleration DetectSupported()
    {
        var result = HardwareAcceleration.None;

        // MMX не доступен через System.Runtime.Intrinsics напрямую,
        // но практически все x86-64 процессоры его поддерживают
        // Оставляем без проверки — на x64 он всегда есть

        if (Sse.IsSupported)
            result |= HardwareAcceleration.Sse;

        if (Sse2.IsSupported)
            result |= HardwareAcceleration.Sse2;

        if (Ssse3.IsSupported)
            result |= HardwareAcceleration.Ssse3;

        if (Sse41.IsSupported)
            result |= HardwareAcceleration.Sse41;

        if (Sse42.IsSupported)
            result |= HardwareAcceleration.Sse42;

        if (Avx.IsSupported)
            result |= HardwareAcceleration.Avx;

        if (Avx2.IsSupported)
            result |= HardwareAcceleration.Avx2;

        if (Fma.IsSupported)
            result |= HardwareAcceleration.Fma;

        if (X86Base.IsSupported && Avx.IsSupported)
        {
            // F16C проверяем через CPUID feature bit — доступен если AVX есть
            // В .NET нет прямого Fma.IsSupported для F16C, но он обычно идёт вместе с AVX
            // Для точной проверки нужен CPUID, но большинство AVX процессоров поддерживают F16C
        }

        if (Avx512F.IsSupported)
            result |= HardwareAcceleration.Avx512F;

        if (Avx512BW.IsSupported)
            result |= HardwareAcceleration.Avx512BW;

        if (Avx512Vbmi.IsSupported)
            result |= HardwareAcceleration.Avx512Vbmi;

        return result;
    }
}