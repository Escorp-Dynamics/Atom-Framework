using System.Runtime.InteropServices;

namespace Atom.Media.Audio.Effects;

/// <summary>
/// Полоса параметрического эквалайзера (peaking EQ).
/// </summary>
/// <param name="Frequency">Центральная частота полосы в герцах.</param>
/// <param name="GainDb">Усиление/ослабление в dB.</param>
/// <param name="Q">Добротность (ширина полосы). По умолчанию 0.707 (Butterworth).</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct EqBand(float Frequency, float GainDb, float Q = 0.707f);
