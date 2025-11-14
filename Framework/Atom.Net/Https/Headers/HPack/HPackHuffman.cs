using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Net.Https.Headers.HPack;

/// <summary>
/// Huffman-кодек (RFC 7541 Appx.B).
/// Поддерживает: MSB-first запись кодов, паддинг единицами (префикс EOS), строгую валидацию на декодировании.
/// </summary>
[SkipLocalsInit]
internal static class HPackHuffman
{
    /// <summary>Максимальная длина кода в битах по RFC (HPACK = 30).</summary>
    private const int MaxCodeBits = 30;

    /// <summary>
    /// Таблица кодов (MSB-first) для символов 0..255 и EOS=256.
    /// Каждый элемент — 32-битный код, используемые старшие <see cref="Lengths"/>[i] бит.
    /// </summary>
    private static readonly uint[] Codes = [
        0x1ff8, 0x7fffd8, 0xfffffe2, 0xfffffe3, 0xfffffe4, 0xfffffe5, 0xfffffe6, 0xfffffe7,
        0xfffffe8, 0xffffea, 0x3ffffffc, 0xfffffe9, 0xfffffea, 0x3ffffffd, 0xfffffeb, 0xfffffec,
        0xfffffed, 0xfffffee, 0xfffffef, 0xffffff0, 0xffffff1, 0xffffff2, 0x3ffffffe, 0xffffff3,
        0xffffff4, 0xffffff5, 0xffffff6, 0xffffff7, 0xffffff8, 0xffffff9, 0xffffffa, 0xffffffb,
        0x14, 0x3f8, 0x3f9, 0xffa, 0x1ff9, 0x15, 0xf8, 0x7fa,
        0x3fa, 0x3fb, 0xf9, 0x7fb, 0xfa, 0x16, 0x17, 0x18,
        0x0, 0x1, 0x2, 0x19, 0x1a, 0x1b, 0x1c, 0x1d,
        0x1e, 0x1f, 0x5c, 0xfb, 0x7ffc, 0x20, 0xffb, 0x3fc,
        0x1ffa, 0x21, 0x5d, 0x5e, 0x5f, 0x60, 0x61, 0x62,
        0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69, 0x6a,
        0x6b, 0x6c, 0x6d, 0x6e, 0x6f, 0x70, 0x71, 0x72,
        0xfc, 0x73, 0xfd, 0x1ffb, 0x7fff0, 0x1ffc, 0x3ffc, 0x22,
        0x7ffd, 0x3, 0x23, 0x4, 0x24, 0x5, 0x25, 0x26,
        0x27, 0x6, 0x74, 0x75, 0x28, 0x29, 0x2a, 0x7,
        0x2b, 0x76, 0x2c, 0x8, 0x9, 0x2d, 0x77, 0x78,
        0x79, 0x7a, 0x7b, 0x7ffe, 0x7fc, 0x3ffd, 0x1ffd, 0xffffffc,
        0xfffe6, 0x3fffd2, 0xfffe7, 0xfffe8, 0x3fffd3, 0x3fffd4, 0x3fffd5, 0x7fffd9,
        0x3fffd6, 0x7fffda, 0x7fffdb, 0x7fffdc, 0x7fffdd, 0x7fffde, 0xffffeb, 0x7fffdf,
        0xffffec, 0xffffed, 0x3fffd7, 0x7fffe0, 0xffffee, 0x7fffe1, 0x7fffe2, 0x7fffe3,
        0x7fffe4, 0x1fffdc, 0x3fffd8, 0x7fffe5, 0x3fffd9, 0x7fffe6, 0x7fffe7, 0xffffef,
        0x3fffda, 0x1fffdd, 0xfffe9, 0x3fffdb, 0x3fffdc, 0x7fffe8, 0x7fffe9, 0x1fffde,
        0x7fffea, 0x3fffdd, 0x3fffde, 0xfffff0, 0x1fffdf, 0x3fffdf, 0x7fffeb, 0x7fffec,
        0x1fffe0, 0x1fffe1, 0x3fffe0, 0x1fffe2, 0x7fffed, 0x3fffe1, 0x7fffee, 0x7fffef,
        0xfffea, 0x3fffe2, 0x3fffe3, 0x3fffe4, 0x7ffff0, 0x3fffe5, 0x3fffe6, 0x7ffff1,
        0x3ffffe0, 0x3ffffe1, 0xfffeb, 0x7fff1, 0x3fffe7, 0x7ffff2, 0x3fffe8, 0x1ffffec,
        0x3ffffe2, 0x3ffffe3, 0x3ffffe4, 0x7ffffde, 0x7ffffdf, 0x3ffffe5, 0xfffff1, 0x1ffffed,
        0x7fff2, 0x1fffe3, 0x3ffffe6, 0x7ffffe0, 0x7ffffe1, 0x3ffffe7, 0x7ffffe2, 0xfffff2,
        0x1fffe4, 0x1fffe5, 0x3ffffe8, 0x3ffffe9, 0xffffffd, 0x7ffffe3, 0x7ffffe4, 0x7ffffe5,
        0xfffec, 0xfffff3, 0xfffed, 0x1fffe6, 0x3fffe9, 0x1fffe7, 0x1fffe8, 0x7ffff3,
        0x3fffea, 0x3fffeb, 0x1ffffee, 0x1ffffef, 0xfffff4, 0xfffff5, 0x3ffffea, 0x7ffff4,
        0x3ffffeb, 0x7ffffe6, 0x3ffffec, 0x3ffffed, 0x7ffffe7, 0x7ffffe8, 0x7ffffe9, 0x7ffffea,
        0x7ffffeb, 0xffffffe, 0x7ffffec, 0x7ffffed, 0x7ffffee, 0x7ffffef, 0x7fffff0, 0x3ffffee,

        // EOS (index 256):
        0x3fffffff,
    ];

    /// <summary>
    /// Длина кода в битах для символов 0..255 и EOS=256.
    /// </summary>
    private static readonly byte[] Lengths = [
        13, 23, 28, 28, 28, 28, 28, 28, 28, 24, 30, 28, 28, 30, 28, 28,
        28, 28, 28, 28, 28, 28, 30, 28, 28, 28, 28, 28, 28, 28, 28, 28,
        6, 10, 10, 12, 13, 6, 8, 11, 10, 10, 8, 11, 8, 6, 6, 6,
        5, 5, 5, 6, 6, 6, 6, 6, 6, 6, 7, 8, 15, 6, 12, 10,
        13, 6, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7,
        7, 7, 7, 7, 7, 7, 7, 7, 8, 7, 8, 13, 19, 13, 14, 6,
        15, 5, 6, 5, 6, 5, 6, 6, 6, 5, 7, 7, 6, 6, 6, 5,
        6, 7, 6, 5, 5, 6, 7, 7, 7, 7, 7, 15, 11, 14, 13, 28,
        20, 22, 20, 20, 22, 22, 22, 23, 22, 23, 23, 23, 23, 23, 24, 23,
        24, 24, 22, 23, 24, 23, 23, 23, 23, 21, 22, 23, 22, 23, 23, 24,
        22, 21, 20, 22, 22, 23, 23, 21, 23, 22, 22, 24, 21, 22, 23, 23,
        21, 21, 22, 21, 23, 22, 23, 23, 20, 22, 22, 22, 23, 22, 22, 23,
        26, 26, 20, 19, 22, 23, 22, 25, 26, 26, 26, 27, 27, 26, 24, 25,
        19, 21, 26, 27, 27, 26, 27, 24, 21, 21, 26, 26, 28, 27, 27, 27,
        20, 24, 20, 21, 22, 21, 21, 23, 22, 22, 25, 25, 24, 24, 26, 23,
        26, 27, 26, 26, 27, 27, 27, 27, 27, 28, 27, 27, 27, 27, 27, 26,
        // EOS
        30,
    ];

    // ---- Декодирующее дерево (узлы) ----
    // Узел хранит: индекс левого/правого ребёнка (0 => нет), либо терминальный символ (>=0).
    // Мы строим дерево один раз при первом обращении (без аллокаций после инициализации).
    [StructLayout(LayoutKind.Auto)]
    private struct Node
    {
        public int Left;    // 0 если отсутствует; иначе индекс узла (1.._nodes.Length-1)
        public int Right;   // 0 если отсутствует
        public int Symbol;  // -1 для не-терминального; 0..256 — терминальный символ
    }

    private static Node[] nodes = [];

    // Потоковый scratch-буфер для результата декодирования (не возвращаем в пул, повторно используем).
    [ThreadStatic] private static byte[]? decodeScratch;

    /// <summary>Флаг: таблица заполнена и декодер построен.</summary>
    public static bool IsReady { get; private set; }

    /// <summary>
    /// Подсчёт длины Huffman-кодирования строки: сумма длин кодов + паддинг до байтовой границы (единицами).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetEncodedBitLength(ReadOnlySpan<char> s)
    {
        EnsureInitialized();

        var bits = 0L;

        for (var i = 0; i < s.Length; i++)
        {
            // ASCII-нормализация как в WriteAscii: не-ASCII -> '?'
            var c = s[i];
            var b = (byte)(c <= 0x7F ? c : 0x3F);
            bits += Lengths[b];
        }

        var pad = (int)(8 - (bits & 7)) & 7;
        return checked((int)bits + pad);
    }

    /// <summary>
    /// Кодирование строки в Huffman (H=1). Пишет байты непосредственно в <paramref name="w"/>.
    /// </summary>
    public static void Encode(ref BufferWriter w, ReadOnlySpan<char> s)
    {
        EnsureInitialized();

        // 64-битный аккумулятор — не теряем биты при длинных кодах
        ulong acc = 0;
        var nBits = 0;

        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            var sym = (byte)(c <= 0x7F ? c : 0x3F); // ASCII-нормализация
            var code = Codes[sym];
            int n = Lengths[sym];

            acc = (acc << n) | code;
            nBits += n;

            while (nBits >= 8)
            {
                nBits -= 8;
                var b = (byte)((acc >> nBits) & 0xFFu);
                w.WriteByte(b); // быстрее и чище, чем временный ReadOnlySpan<byte>
            }
        }

        // Паддинг единицами (префикс EOS)
        if (nBits > 0)
        {
            var last = (acc << (8 - nBits)) | ((1u << (8 - nBits)) - 1u);
            w.WriteByte((byte)(last & 0xFFu));
        }
    }

    /// <summary>
    /// Декодирование Huffman-блока. Возвращает срез в потоковом scratch-буфере (действителен до следующего вызова Decode в этом потоке).
    /// </summary>
    public static ReadOnlySpan<byte> Decode(ReadOnlySpan<byte> data)
    {
        EnsureInitialized();

        uint acc = 0;
        var nBits = 0;
        var outBuf = EnsureScratch(Math.Max(64, data.Length * 2));
        var outPos = 0;
        var node = 1;
        for (var i = 0; i < data.Length; i++)
        {
            acc = (acc << 8) | data[i];
            nBits += 8;
            (node, nBits, outPos, outBuf) = DecodeBits(acc, nBits, node, outPos, outBuf);
        }
        ValidateDecodeFinalState(node, nBits, acc);
        return new ReadOnlySpan<byte>(outBuf, 0, outPos);
    }

    /// <summary>
    /// Обрабатывает побитовый обход дерева Хаффмана и эмит символы в буфер.
    /// </summary>
    private static (int node, int nBits, int outPos, byte[] outBuf) DecodeBits(uint acc, int nBits, int node, int outPos, byte[] outBuf)
    {
        var step = Math.Min(nBits, MaxCodeBits);
        var consumed = 0;
        while (nBits > 0 && consumed < step)
        {
            var bitIndex = nBits - 1;
            var bit = (int)((acc >> bitIndex) & 1);
            nBits--;
            consumed++;
            node = (bit == 0) ? nodes[node].Left : nodes[node].Right;
            if (node is 0)
                throw new InvalidOperationException("HPACK Huffman: недопустимая последовательность бит (пустая ветка).");
            var sym = nodes[node].Symbol;
            if (sym >= 0)
            {
                if (sym is 256)
                    throw new InvalidOperationException("HPACK Huffman: недопустимый EOS в середине потока.");
                if (outPos == outBuf.Length)
                    outBuf = EnsureScratch(outBuf.Length << 1);
                outBuf[outPos++] = (byte)sym;
                node = 1;
                break;
            }
        }
        return (node, nBits, outPos, outBuf);
    }

    /// <summary>
    /// Проверяет финальное состояние декодирования Huffman-блока.
    /// </summary>
    private static void ValidateDecodeFinalState(int node, int nBits, uint acc)
    {
        if (node is not 1) throw new InvalidOperationException("HPACK Huffman: незавершённый код символа в конце потока.");
        if (nBits > 0)
        {
            var mask = (uint)((1 << nBits) - 1);
            var tail = acc & mask;
            if (tail != mask) throw new InvalidOperationException("HPACK Huffman: некорректный паддинг (ожидались единицы).");
        }
    }

    /// <summary>
    /// Гарантирует инициализацию декодирующего дерева и корректность таблиц.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EnsureInitialized()
    {
        if (IsReady) return;

        // Базовая валидация таблиц (чтобы случайно не поехать MSB/len)
        if (Codes.Length is not 257 || Lengths.Length is not 257)
            throw new InvalidOperationException("HPACK Huffman: ожидаются 257 кодов и 257 длин (0..255 + EOS).");

        // Проверим максимумы длин
        for (var i = 0; i < 257; i++)
        {
            if (Lengths[i] is 0) throw new InvalidOperationException("HPACK Huffman: длина кода не задана (см. TODO-таблицу).");
            if (Lengths[i] > MaxCodeBits) throw new InvalidOperationException("HPACK Huffman: недопустимая длина кода > 30.");
        }

        BuildDecoder();
        IsReady = true;
    }

    /// <summary>
    /// Построение бинарного дерева декодирования из (code,len) для 0..256.
    /// Узел 1 — корень. Узел 0 — «пусто».
    /// </summary>
    private static void BuildDecoder()
    {
        var localNodes = AllocateDecoderNodes();
        BuildDecoderTree(localNodes);
        nodes = localNodes;
    }

    /// <summary>
    /// Выделяет массив узлов для декодирующего дерева.
    /// </summary>
    private static Node[] AllocateDecoderNodes()
    {
        var arr = new Node[8192];
        arr[1].Left = 0; arr[1].Right = 0; arr[1].Symbol = -1;
        return arr;
    }

    /// <summary>
    /// Строит бинарное дерево декодирования Huffman по таблицам кодов и длин.
    /// </summary>
    private static void BuildDecoderTree(Node[] localNodes)
    {
        var next = 2;
        for (var sym = 0; sym <= 256; sym++)
        {
            var code = Codes[sym];
            int len = Lengths[sym];
            var node = 1;
            for (var k = len - 1; k >= 0; k--)
            {
                var bit = (int)((code >> k) & 1);
                if (k is 0)
                {
                    next = HandleTerminalStep(localNodes, node, bit, sym, next);
                }
                else
                {
                    (node, next) = HandleIntermediateStep(localNodes, node, bit, next);
                }
            }
        }
    }

    /// <summary>
    /// Обрабатывает терминальный шаг построения дерева Хаффмана.
    /// </summary>
    private static int HandleTerminalStep(Node[] localNodes, int node, int bit, int sym, int next)
    {
        ref var n = ref localNodes[node];
        if (bit is 0)
        {
            if (n.Left is 0)
            {
                n.Left = next;
                localNodes[next] = new Node { Symbol = sym };
                return next + 1;
            }
            if (localNodes[n.Left].Symbol is not -1)
                throw new InvalidOperationException("HPACK Huffman: конфликт кодов (терминал слева).");
            throw new InvalidOperationException("HPACK Huffman: код пересекается с существующим префиксом.");
        }
        if (n.Right is 0)
        {
            n.Right = next;
            localNodes[next] = new Node { Symbol = sym };
            return next + 1;
        }
        if (localNodes[n.Right].Symbol is not -1)
            throw new InvalidOperationException("HPACK Huffman: конфликт кодов (терминал справа).");
        throw new InvalidOperationException("HPACK Huffman: код пересекается с существующим префиксом.");
    }

    /// <summary>
    /// Обрабатывает промежуточный шаг построения дерева Хаффмана.
    /// </summary>
    private static (int node, int next) HandleIntermediateStep(Node[] localNodes, int node, int bit, int next)
    {
        ref var n = ref localNodes[node];
        if (bit is 0)
        {
            if (n.Left is 0)
            {
                n.Left = next;
                localNodes[next] = new Node { Symbol = -1 };
                return (n.Left, next + 1);
            }
            return (n.Left, next);
        }
        if (n.Right is 0)
        {
            n.Right = next;
            localNodes[next] = new Node { Symbol = -1 };
            return (n.Right, next + 1);
        }
        return (n.Right, next);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte[] EnsureScratch(int minCapacity)
    {
        var a = decodeScratch;
        if (a is null || a.Length < minCapacity)
        {
            var cap = a == null ? 1024 : a.Length;
            while (cap < minCapacity) cap <<= 1;
            decodeScratch = a = new byte[cap];
        }
        return a;
    }
}