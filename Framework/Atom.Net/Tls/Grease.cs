using System.Runtime.CompilerServices;

namespace Atom.Net.Tls;

/// <summary>
/// Статический провайдер GREASE-значений (RFC 8701) для разных доменов ClientHello.
/// </summary>
public static class Grease
{
    /// <summary>
    /// Токен области действия GREASE-контекста (RAII-паттерн через using).
    /// </summary>
    private readonly struct Scope : IDisposable
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void UpdateDepth()
        {
            if (depth <= 0) return;
            --depth;
            if (depth is 0) seed = 0u;  // Обнуляем seed, когда последний уровень завершился.
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose() => UpdateDepth();
    }

    /// <summary>
    /// Таблица GREASE-значений (16 допустимых 0x?A?A) согласно RFC 8701.
    /// </summary>
    private static readonly ushort[] Table =
    [
        0x0A0A, 0x1A1A, 0x2A2A, 0x3A3A,
        0x4A4A, 0x5A5A, 0x6A6A, 0x7A7A,
        0x8A8A, 0x9A9A, 0xAAAA, 0xBABA,
        0xCACA, 0xDADA, 0xEAEA, 0xFAFA,
    ];

    // Вся "связка" к конкретному ClientHello хранится в thread-local,
    // чтобы не использовать блокировки и избежать гонок.
    [ThreadStatic] private static uint seed;   // Хеш от ClientHello.random
    [ThreadStatic] private static int depth;   // Глубина вложенных Enter(..)

    /// <summary>
    /// Возвращает GREASE-значение для домена "supported_versions".
    /// </summary>
    public static ushort Versions => Pick(scope: 0);

    /// <summary>
    /// Возвращает GREASE-значение для домена "supported_groups".
    /// </summary>
    public static ushort Groups => Pick(scope: 1);

    /// <summary>
    /// Возвращает GREASE-значение для домена "cipher_suites".
    /// </summary>
    public static ushort CipherSuites => Pick(scope: 2);

    /// <summary>
    /// Возвращает GREASE-значение для домена "extensions" (ExtensionType).
    /// </summary>
    public static ushort Extension => Pick(scope: 3);

    /// <summary>
    /// Выбор GREASE по текущему seed и заданному домену.
    /// Бросает InvalidOperationException, если Enter(..) не вызывался.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort Pick(byte scope)
    {
        if (depth is 0) throw new InvalidOperationException("Grease.Enter(random) не вызван для текущего потока");

        // Индекс 0..15 — детерминированно, но различается по scope,
        // чтобы домены имели разные значения внутри одного CH.
        var idx = (int)((seed ^ scope) & 0x0Fu);
        return Table[idx];
    }

    /// <summary>
    /// Устанавливает контекст GREASE для текущего потока на основании ClientHello.random.
    /// Возвращает IDisposable-токен; при Dispose() контекст снимается.
    /// Поддерживает вложенные вызовы (depth++).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IDisposable Enter(ReadOnlySpan<byte> clientHelloRandom)
    {
        try
        {
            // Вычисляем детерминированный seed из 32-байтового random.
            // Без аллокаций, простой быстрый свёрточный хеш (вариант FNV/xx).
            var s = 2166136261u; // FNV offset basis

            unchecked
            {
                for (var i = 0; i < clientHelloRandom.Length; ++i)
                {
                    s ^= clientHelloRandom[i];
                    s *= 16777619u; // FNV prime
                    s = (s << 5) | (s >> 27);
                }
            }

            if (depth is 0) seed = s;
            ++depth;

            return new Scope();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Grease.Enter failed for random length {clientHelloRandom.Length} and depth {depth}.", exception);
        }
    }
}