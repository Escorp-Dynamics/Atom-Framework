using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Atom.Net.Tls;

/// <summary>
/// Функция Диффи — Хеллмана на кривой Curve25519 (RFC 7748).
/// </summary>
/// <remarks>
/// Собственная реализация здесь вынужденная, а не предпочтительная: платформа .NET на этой ОС
/// отказывает в кривой <c>1.3.101.110</c> с <see cref="PlatformNotSupportedException"/>, а без
/// X25519 состав ключевых долей в ClientHello отличается от браузерного — то есть цель мимикрии
/// не достигается никаким подбором остальных параметров.
///
/// Арифметика выполнена на 64-битных ветвях с основанием 2^51: это стандартное представление для
/// Curve25519, оно избегает переполнений при умножении и не требует ветвлений по данным. Лестница
/// Монтгомери обрабатывает все биты скаляра одинаково, а выбор ветви делается маской, а не
/// условным переходом — время работы не зависит от секрета.
/// </remarks>
/// <remarks>
/// <see cref="SkipLocalsInitAttribute"/> снимает обнуление буферов на стеке: все они заполняются
/// перед чтением, а обнуление на каждом вызове — заметная доля времени для функции, которая
/// вызывается дважды на каждое соединение.
/// </remarks>
[SkipLocalsInit]
public static class X25519
{
    /// <summary>Размер скаляра и координаты в байтах.</summary>
    public const int KeySize = 32;

    /// <summary>Базовая точка кривой (u = 9).</summary>
    private static ReadOnlySpan<byte> BasePoint =>
    [
        9, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    ];

    /// <summary>
    /// Создаёт случайный приватный ключ с обязательным «подрезанием» битов (RFC 7748, §5).
    /// </summary>
    /// <returns>Приватный ключ длиной <see cref="KeySize"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] CreatePrivateKey()
    {
        var key = RandomNumberGenerator.GetBytes(KeySize);
        Clamp(key);
        return key;
    }

    /// <summary>
    /// Вычисляет публичный ключ как скалярное умножение на базовую точку.
    /// </summary>
    /// <param name="privateKey">Приватный ключ.</param>
    /// <returns>Публичный ключ длиной <see cref="KeySize"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] GetPublicKey(ReadOnlySpan<byte> privateKey) => ScalarMultiply(privateKey, BasePoint);

    /// <summary>
    /// Вычисляет общий секрет из своего приватного ключа и чужого публичного.
    /// </summary>
    /// <param name="privateKey">Свой приватный ключ.</param>
    /// <param name="peerPublicKey">Публичный ключ второй стороны.</param>
    /// <returns>Общий секрет длиной <see cref="KeySize"/>.</returns>
    /// <exception cref="CryptographicException">Результат оказался нулевым — точка низкого порядка.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] DeriveSharedSecret(ReadOnlySpan<byte> privateKey, ReadOnlySpan<byte> peerPublicKey)
    {
        var shared = ScalarMultiply(privateKey, peerPublicKey);

        // Нулевой результат означает точку малого порядка: секрет предсказуем, и продолжать нельзя.
        // Проверка выполняется за постоянное время, чтобы не давать побочного канала.
        var isZero = 0;
        foreach (var value in shared) isZero |= value;

        return isZero == 0 ? throw new CryptographicException("X25519: получен нулевой общий секрет") : shared;
    }

    /// <summary>
    /// Приводит скаляр к требуемому виду: сбрасывает три младших бита, старший бит и выставляет бит 254.
    /// </summary>
    /// <param name="scalar">Скаляр для приведения.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Clamp(Span<byte> scalar)
    {
        scalar[0] &= 248;
        scalar[31] &= 127;
        scalar[31] |= 64;
    }

    /// <summary>
    /// Выполняет скалярное умножение точки кривой (RFC 7748, §5).
    /// </summary>
    /// <param name="scalar">Скаляр.</param>
    /// <param name="uCoordinate">Координата u исходной точки.</param>
    /// <returns>Координата u результата.</returns>
    public static byte[] ScalarMultiply(ReadOnlySpan<byte> scalar, ReadOnlySpan<byte> uCoordinate)
    {
        // Явные броски вместо ThrowIf-хелперов: хелпер подставляет выражение вызывающей стороны
        // вместо имени параметра, чего требует анализатор.
        if (scalar.Length != KeySize) throw new ArgumentOutOfRangeException(nameof(scalar), "Скаляр X25519 обязан быть длиной 32 байта");
        if (uCoordinate.Length != KeySize) throw new ArgumentOutOfRangeException(nameof(uCoordinate), "Координата X25519 обязана быть длиной 32 байта");

        Span<byte> clamped = stackalloc byte[KeySize];
        scalar.CopyTo(clamped);
        Clamp(clamped);

        Span<ulong> x1 = stackalloc ulong[5];
        Decode(uCoordinate, x1);

        Span<ulong> x2 = stackalloc ulong[5];
        Span<ulong> z2 = stackalloc ulong[5];
        Span<ulong> x3 = stackalloc ulong[5];
        Span<ulong> z3 = stackalloc ulong[5];
        Span<ulong> tmp0 = stackalloc ulong[5];
        Span<ulong> tmp1 = stackalloc ulong[5];

        // Обнуляем ЯВНО. Класс помечен SkipLocalsInit, поэтому stackalloc больше не очищает память,
        // а лестница Монтгомери начинает с (x2,z2) = (1,0) и (x3,z3) = (u,1) — то есть полагается на
        // нули в остальных ветвях. Без явной очистки алгоритм молча считает мусор: тесты на эталоны
        // OpenSSL это поймали, но в бою такая ошибка выглядела бы как случайный сбой рукопожатия.
        // Очищаем только то, что действительно требует нулей: x1, x3, tmp0 и tmp1 полностью
        // перезаписываются перед первым чтением.
        x2.Clear();
        z2.Clear();
        z3.Clear();

        x2[0] = 1;
        x1.CopyTo(x3);
        z3[0] = 1;

        var swap = 0UL;

        for (var position = 254; position >= 0; position--)
        {
            var bit = (ulong)((clamped[position >> 3] >> (position & 7)) & 1);
            swap ^= bit;

            ConditionalSwap(x2, x3, swap);
            ConditionalSwap(z2, z3, swap);
            swap = bit;

            // Шаг лестницы Монтгомери: одновременно удвоение и сложение, одинаковые операции на
            // любом значении бита — отсюда независимость времени работы от секрета.
            Subtract(tmp0, x3, z3);
            Subtract(tmp1, x2, z2);
            Add(x2, x2, z2);
            Add(z2, x3, z3);
            Multiply(z3, tmp0, x2);
            Multiply(z2, z2, tmp1);
            Square(tmp0, tmp1);
            Square(tmp1, x2);
            Add(x3, z3, z2);
            Subtract(z2, z3, z2);
            Multiply(x2, tmp1, tmp0);
            Subtract(tmp1, tmp1, tmp0);
            Square(z2, z2);
            MultiplyBy121666(z3, tmp1);
            Square(x3, x3);
            Add(tmp0, tmp0, z3);
            Multiply(z3, x1, z2);
            Multiply(z2, tmp1, tmp0);
        }

        ConditionalSwap(x2, x3, swap);
        ConditionalSwap(z2, z3, swap);

        Invert(z2, z2);
        Multiply(x2, x2, z2);

        var result = new byte[KeySize];
        Encode(x2, result);
        return result;
    }

    /// <summary>Раскладывает 32 байта в пять 51-битных ветвей.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Decode(ReadOnlySpan<byte> source, Span<ulong> destination)
    {
        var low = BitConverter.ToUInt64(source[..8]);
        var second = BitConverter.ToUInt64(source.Slice(8, 8));
        var third = BitConverter.ToUInt64(source.Slice(16, 8));
        var high = BitConverter.ToUInt64(source.Slice(24, 8));

        destination[0] = low & 0x7FFFFFFFFFFFF;
        destination[1] = ((low >> 51) | (second << 13)) & 0x7FFFFFFFFFFFF;
        destination[2] = ((second >> 38) | (third << 26)) & 0x7FFFFFFFFFFFF;
        destination[3] = ((third >> 25) | (high << 39)) & 0x7FFFFFFFFFFFF;

        // Старший бит координаты игнорируется спецификацией.
        destination[4] = (high >> 12) & 0x7FFFFFFFFFFFF;
    }

    /// <summary>Собирает пять ветвей обратно в 32 байта с полной редукцией.</summary>
    private static void Encode(Span<ulong> value, Span<byte> destination)
    {
        Carry(value);
        Carry(value);
        Carry(value);

        const ulong Mask = 0x7FFFFFFFFFFFF;

        // Приведение к КАНОНИЧЕСКОМУ виду. После переносов значение лежит в [0, 2^255), то есть
        // может оказаться в диапазоне [p, 2^255) и представлять то же число, что и value − p.
        // Ровно на этом ломалась прежняя версия: она заворачивала перенос из старшей ветви обратно
        // в младшую (как это делает Carry), поэтому признак «значение ≥ p» терялся, и p кодировался
        // сам собой вместо нуля.
        //
        // Считаем частное q = (value + 19) / 2^255 БЕЗ заворачивания: единица означает value ≥ p.
        // Затем безусловно прибавляем 19·q и отбрасываем старший бит — так вычитание p происходит
        // тогда и только тогда, когда оно нужно, и без ветвления по данным.
        var q = (value[0] + 19) >> 51;
        q = (value[1] + q) >> 51;
        q = (value[2] + q) >> 51;
        q = (value[3] + q) >> 51;
        q = (value[4] + q) >> 51;

        value[0] += 19 * q;

        var carry = value[0] >> 51;
        value[0] &= Mask;
        value[1] += carry;

        carry = value[1] >> 51;
        value[1] &= Mask;
        value[2] += carry;

        carry = value[2] >> 51;
        value[2] &= Mask;
        value[3] += carry;

        carry = value[3] >> 51;
        value[3] &= Mask;
        value[4] += carry;

        // Старший бит отбрасываем: он и есть вычтенное 2^255.
        value[4] &= Mask;

        var low = value[0] | (value[1] << 51);
        var second = (value[1] >> 13) | (value[2] << 38);
        var third = (value[2] >> 26) | (value[3] << 25);
        var high = (value[3] >> 39) | (value[4] << 12);

        BitConverter.TryWriteBytes(destination[..8], low);
        BitConverter.TryWriteBytes(destination.Slice(8, 8), second);
        BitConverter.TryWriteBytes(destination.Slice(16, 8), third);
        BitConverter.TryWriteBytes(destination.Slice(24, 8), high);
    }

    /// <summary>Меняет местами два значения, если маска установлена; время работы постоянно.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ConditionalSwap(Span<ulong> left, Span<ulong> right, ulong condition)
    {
        // Маска строится вычитанием из нуля и обязана «переполняться»: при condition = 1 получаем
        // все единицы, при 0 — все нули. Проверяемая арифметика включена для всего фреймворка, но
        // здесь переполнение — не ошибка, а сам механизм выбора ветви без условного перехода.
        var mask = unchecked(0UL - condition);

        // Цикл развёрнут по той же причине, что и в умножении: пять итераций с индексацией спана
        // стоят дороже, чем пять пар обращений напрямую, а вызывается это дважды на каждый из 255
        // шагов лестницы.
        Swap(left, right, mask, 0);
        Swap(left, right, mask, 1);
        Swap(left, right, mask, 2);
        Swap(left, right, mask, 3);
        Swap(left, right, mask, 4);

        static void Swap(Span<ulong> left, Span<ulong> right, ulong mask, int index)
        {
            var difference = mask & (left[index] ^ right[index]);
            left[index] ^= difference;
            right[index] ^= difference;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Add(Span<ulong> destination, ReadOnlySpan<ulong> left, ReadOnlySpan<ulong> right)
    {
        unchecked
        {
            for (var index = 0; index < 5; index++) destination[index] = left[index] + right[index];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Subtract(Span<ulong> destination, ReadOnlySpan<ulong> left, ReadOnlySpan<ulong> right)
    {
        unchecked
        {
            // Прибавляем кратное p, чтобы разность гарантированно осталась неотрицательной.
            destination[0] = left[0] + 0xFFFFFFFFFFFDA - right[0];
            for (var index = 1; index < 5; index++) destination[index] = left[index] + 0xFFFFFFFFFFFFE - right[index];
        }
    }

    private static void Multiply(Span<ulong> destination, ReadOnlySpan<ulong> left, ReadOnlySpan<ulong> right)
    {
        // Ветви загружаем в локальные переменные ОДИН раз. Прямая индексация спана дала бы здесь
        // полсотни обращений с проверкой границ на каждое умножение, и JIT их не устраняет —
        // замер показал 2.96 мс на операцию против ожидаемых десятков микросекунд. С локальными
        // переменными значения живут в регистрах, а проверок границ остаётся десять.
        var l0 = left[0];
        var l1 = left[1];
        var l2 = left[2];
        var l3 = left[3];
        var l4 = left[4];

        var r0 = right[0];
        var r1 = right[1];
        var r2 = right[2];
        var r3 = right[3];
        var r4 = right[4];

        unchecked
        {
            // Слагаемые выше пятой позиции сворачиваются множителем 19 — это и есть редукция по
            // модулю 2^255 − 19. Множители 19·l вычисляем один раз: каждый используется четырежды.
            var l1x19 = l1 * 19;
            var l2x19 = l2 * 19;
            var l3x19 = l3 * 19;
            var l4x19 = l4 * 19;

            var t0 = ((UInt128)l0 * r0) + ((UInt128)l1x19 * r4) + ((UInt128)l2x19 * r3) + ((UInt128)l3x19 * r2) + ((UInt128)l4x19 * r1);
            var t1 = ((UInt128)l0 * r1) + ((UInt128)l1 * r0) + ((UInt128)l2x19 * r4) + ((UInt128)l3x19 * r3) + ((UInt128)l4x19 * r2);
            var t2 = ((UInt128)l0 * r2) + ((UInt128)l1 * r1) + ((UInt128)l2 * r0) + ((UInt128)l3x19 * r4) + ((UInt128)l4x19 * r3);
            var t3 = ((UInt128)l0 * r3) + ((UInt128)l1 * r2) + ((UInt128)l2 * r1) + ((UInt128)l3 * r0) + ((UInt128)l4x19 * r4);
            var t4 = ((UInt128)l0 * r4) + ((UInt128)l1 * r3) + ((UInt128)l2 * r2) + ((UInt128)l3 * r1) + ((UInt128)l4 * r0);

            Reduce(destination, t0, t1, t2, t3, t4);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Square(Span<ulong> destination, ReadOnlySpan<ulong> value) => Multiply(destination, value, value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void MultiplyBy121666(Span<ulong> destination, ReadOnlySpan<ulong> value)
    {
        const ulong Factor = 121666;

        unchecked
        {
            var r0 = (UInt128)value[0] * Factor;
            var r1 = (UInt128)value[1] * Factor;
            var r2 = (UInt128)value[2] * Factor;
            var r3 = (UInt128)value[3] * Factor;
            var r4 = (UInt128)value[4] * Factor;

            Reduce(destination, r0, r1, r2, r3, r4);
        }
    }

    /// <summary>Сворачивает 128-битные накопители обратно в пять 51-битных ветвей.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Reduce(Span<ulong> destination, UInt128 r0, UInt128 r1, UInt128 r2, UInt128 r3, UInt128 r4)
    {
        const ulong Mask = 0x7FFFFFFFFFFFF;

        // Усечение 128-битного накопителя до 64 бит — не потеря данных, а часть редукции: старшие
        // биты уже сняты переносом строкой выше. Проверяемая арифметика включена для всего
        // фреймворка (Framework/Directory.Build.props), поэтому здесь снимаем её точечно.
        unchecked
        {
            var carry = (ulong)(r0 >> 51);
            destination[0] = (ulong)r0 & Mask;
            r1 += carry;

            carry = (ulong)(r1 >> 51);
            destination[1] = (ulong)r1 & Mask;
            r2 += carry;

            carry = (ulong)(r2 >> 51);
            destination[2] = (ulong)r2 & Mask;
            r3 += carry;

            carry = (ulong)(r3 >> 51);
            destination[3] = (ulong)r3 & Mask;
            r4 += carry;

            carry = (ulong)(r4 >> 51);
            destination[4] = (ulong)r4 & Mask;

            // Перенос из старшей ветви возвращается в младшую с множителем 19 — это и есть редукция.
            destination[0] += carry * 19;
            carry = destination[0] >> 51;
            destination[0] &= Mask;
            destination[1] += carry;
        }
    }

    /// <summary>Выполняет один проход переносов между ветвями.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Carry(Span<ulong> value)
    {
        const ulong Mask = 0x7FFFFFFFFFFFF;

        var carry = value[0] >> 51;
        value[0] &= Mask;
        value[1] += carry;

        carry = value[1] >> 51;
        value[1] &= Mask;
        value[2] += carry;

        carry = value[2] >> 51;
        value[2] &= Mask;
        value[3] += carry;

        carry = value[3] >> 51;
        value[3] &= Mask;
        value[4] += carry;

        carry = value[4] >> 51;
        value[4] &= Mask;
        value[0] += carry * 19;
    }

    /// <summary>
    /// Вычисляет обратный элемент возведением в степень p−2 (малая теорема Ферма).
    /// </summary>
    private static void Invert(Span<ulong> destination, ReadOnlySpan<ulong> value)
    {
        Span<ulong> z2 = stackalloc ulong[5];
        Span<ulong> z9 = stackalloc ulong[5];
        Span<ulong> z11 = stackalloc ulong[5];
        Span<ulong> z2To5Minus1 = stackalloc ulong[5];
        Span<ulong> z2To10Minus1 = stackalloc ulong[5];
        Span<ulong> z2To20Minus1 = stackalloc ulong[5];
        Span<ulong> z2To50Minus1 = stackalloc ulong[5];
        Span<ulong> z2To100Minus1 = stackalloc ulong[5];
        Span<ulong> temporary = stackalloc ulong[5];

        Square(z2, value);
        Square(temporary, z2);
        Square(temporary, temporary);
        Multiply(z9, temporary, value);
        Multiply(z11, z9, z2);
        Square(temporary, z11);
        Multiply(z2To5Minus1, temporary, z9);

        Square(temporary, z2To5Minus1);
        for (var index = 1; index < 5; index++) Square(temporary, temporary);
        Multiply(z2To10Minus1, temporary, z2To5Minus1);

        Square(temporary, z2To10Minus1);
        for (var index = 1; index < 10; index++) Square(temporary, temporary);
        Multiply(z2To20Minus1, temporary, z2To10Minus1);

        Square(temporary, z2To20Minus1);
        for (var index = 1; index < 20; index++) Square(temporary, temporary);
        Multiply(temporary, temporary, z2To20Minus1);

        for (var index = 0; index < 10; index++) Square(temporary, temporary);
        Multiply(z2To50Minus1, temporary, z2To10Minus1);

        Square(temporary, z2To50Minus1);
        for (var index = 1; index < 50; index++) Square(temporary, temporary);
        Multiply(z2To100Minus1, temporary, z2To50Minus1);

        Square(temporary, z2To100Minus1);
        for (var index = 1; index < 100; index++) Square(temporary, temporary);
        Multiply(temporary, temporary, z2To100Minus1);

        for (var index = 0; index < 50; index++) Square(temporary, temporary);
        Multiply(temporary, temporary, z2To50Minus1);

        for (var index = 0; index < 5; index++) Square(temporary, temporary);
        Multiply(destination, temporary, z11);
    }
}
