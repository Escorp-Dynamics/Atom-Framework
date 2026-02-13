#pragma warning disable MA0051, MA0102, IDE0251, S1066, S1199, S138, S3776, CS1591, CA1823, S4136

using System.Buffers;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

#if !DEBUG
using System.Numerics;
using System.Runtime.Intrinsics;
#endif

namespace Atom.Text;

/// <summary>
/// Высокопроизводительный построитель строк без промежуточных аллокаций.
/// </summary>
[StructLayout(LayoutKind.Auto)]
[SkipLocalsInit]
public ref struct ValueStringBuilder
{
    private const int DefaultCapacity = 256;
    private const int UnsafeCopyThreshold = 8;

#if !DEBUG
    // SIMD пороги активации (breakeven points для char replacement)
    private const int SimdThreshold256 = 16;
    private const int SimdThreshold128 = 8;
#endif

    private Span<char> chars;
    private char[]? buffer;
    private int position;
#pragma warning disable IDE0032 // readonly field, не может быть auto property
    private readonly int maxCapacity;
    private readonly bool isClearOnDispose;
#pragma warning restore IDE0032

    /// <summary>
    /// Инициализирует экземпляр с емкостью по умолчанию.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueStringBuilder() : this(DefaultCapacity) { }

    /// <summary>
    /// Инициализирует экземпляр с указанной емкостью.
    /// </summary>
    /// <param name="capacity">Начальная емкость буфера.</param>
    /// <param name="clearOnDispose">Нужно ли очищать буфер при возврате.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueStringBuilder(int capacity, bool clearOnDispose = default) : this(capacity, int.MaxValue, clearOnDispose) { }

    /// <summary>
    /// Инициализирует экземпляр поверх внешнего буфера.
    /// </summary>
    /// <param name="initialBuffer">Исходный буфер.</param>
    /// <param name="clearOnDispose">Нужно ли очищать буфер.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueStringBuilder(Span<char> initialBuffer, bool clearOnDispose = default)
    {
        // Внешний буфер: buffer остаётся null, chars указывает на внешний span
        chars = initialBuffer;
        buffer = null; // Внешний буфер не принадлежит нам
        position = 0;
        maxCapacity = initialBuffer.Length; // Не можем расти за пределы внешнего буфера
        isClearOnDispose = clearOnDispose;
    }

    /// <summary>
    /// Инициализирует экземпляр с указанными параметрами.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueStringBuilder(int capacity, int maxCapacity, bool clearOnDispose)
    {
#if DEBUG
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, maxCapacity);
#endif

        this.maxCapacity = maxCapacity;
        isClearOnDispose = clearOnDispose;

        // Упрощённый конструктор: всегда используем ArrayPool для предсказуемости
        var rentLength = Math.Max(capacity, 1);
        var array = ArrayPool<char>.Shared.Rent(rentLength);
        chars = array;
        buffer = array;
        position = 0;
    }

    /// <summary>
    /// Инициализирует экземпляр и заполняет его строкой.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueStringBuilder(string? value) : this(Math.Max((value?.Length ?? 0) * 2, DefaultCapacity)) => Append(value);

    /// <summary>
    /// Инициализирует экземпляр из <see cref="StringBuilder"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueStringBuilder(StringBuilder? builder) : this(builder?.Length ?? DefaultCapacity)
    {
        if (builder is null) return;
        builder.CopyTo(0, chars, builder.Length);
        position = builder.Length;
    }

    /// <summary>
    /// Инициализирует экземпляр из диапазона символов.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public ValueStringBuilder(ReadOnlySpan<char> value) : this(Math.Max(value.Length * 2, DefaultCapacity)) => Append(value);

    /// <summary>
    /// Инициализирует экземпляр с указанной строкой и емкостью.
    /// </summary>
    /// <param name="value">Начальное значение.</param>
    /// <param name="capacity">Начальная емкость.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueStringBuilder(string? value, int capacity) : this(Math.Max(capacity, value?.Length ?? 0)) => Append(value);

    /// <summary>
    /// Инициализирует экземпляр с подстрокой и емкостью.
    /// </summary>
    /// <param name="value">Исходная строка.</param>
    /// <param name="startIndex">Начальный индекс.</param>
    /// <param name="length">Длина подстроки.</param>
    /// <param name="capacity">Начальная емкость.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueStringBuilder(string value, int startIndex, int length, int capacity) : this(Math.Max(capacity, length))
    {
#if DEBUG
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfNegative(startIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (startIndex + length > value.Length)
            throw new ArgumentOutOfRangeException(nameof(length));
#endif
        Append(value.AsSpan(startIndex, length));
    }

    /// <summary>
    /// Получает или задает емкость буфера.
    /// </summary>
    public int Capacity
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        readonly get => chars.Length;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
#if DEBUG
            ArgumentOutOfRangeException.ThrowIfLessThan(value, position);
#endif
            if (value == chars.Length) return;
            if (value > MaxCapacity) ThrowCapacityExceeded();

            var array = ArrayPool<char>.Shared.Rent(value);
            if (position > 0)
                chars[..position].CopyTo(array);

            if (buffer is not null)
                ArrayPool<char>.Shared.Return(buffer, clearArray: isClearOnDispose);

            buffer = array;
            chars = array;
        }
    }

    /// <summary>
    /// Максимально допустимая длина.
    /// </summary>
    public readonly int MaxCapacity
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => maxCapacity is 0 ? int.MaxValue : maxCapacity;
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // INTERNAL ACCESSORS — для handler'а
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>Возвращает внутренний Span chars (для handler).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal readonly Span<char> GetCharsSpan() => chars;

    /// <summary>Возвращает внутренний buffer (для handler).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal readonly char[]? GetBuffer() => buffer;

    /// <summary>Возвращает флаг очистки при dispose (для handler).</summary>
    internal readonly bool IsClearOnDispose
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => isClearOnDispose;
    }

    /// <summary>
    /// Количество записанных символов.
    /// </summary>
    public int Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        readonly get => position;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
#if DEBUG
            ArgumentOutOfRangeException.ThrowIfNegative(value);
#endif
            // Оптимизация: проверяем capacity перед EnsureCapacity
            if (value > Capacity) EnsureCapacity(value);
            if (value > position) chars[position..value].Clear();
            position = value;
        }
    }

    /// <summary>
    /// Предоставляет span для прямой записи указанного количества символов.
    /// </summary>
    /// <param name="sizeHint">Минимальное количество символов.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<char> GetSpan(int sizeHint = 0)
    {
        if ((uint)sizeHint > 0 ? (position > chars.Length - sizeHint) : (position >= chars.Length)) Grow(Math.Max(sizeHint, 1));
        return chars[position..];
    }

    /// <summary>
    /// Увеличивает текущую длину после записи в буфер через <see cref="GetSpan"/>.
    /// </summary>
    /// <param name="count">Количество записанных символов.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int count)
    {
        // Минимальная проверка безопасности (быстрая unsigned проверка)
        if ((uint)count > (uint)(chars.Length - position)) ThrowOutOfRange();
        position += count;
    }

    /// <summary>
    /// Получает или задает символ по указанному индексу.
    /// </summary>
    public readonly char this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
#if DEBUG
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, position);
#endif
            return chars[index];
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
#if DEBUG
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, position);
#endif
            chars[index] = value;
        }
    }

    /// <summary>
    /// Возвращает ссылку на символ по индексу для прямой модификации без копирования.
    /// </summary>
    /// <param name="index">Индекс символа.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public readonly ref char GetReference(int index)
    {
#if DEBUG
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, position);
#endif
        return ref chars[index];
    }

    /// <summary>
    /// Возвращает записанное содержимое как <see cref="ReadOnlySpan{T}"/> без аллокаций.
    /// </summary>
    /// <returns>Span записанных символов.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ReadOnlySpan<char> AsSpan() => chars[..position];

    /// <summary>
    /// Возвращает записанное содержимое указанного диапазона как <see cref="ReadOnlySpan{T}"/> без аллокаций.
    /// </summary>
    /// <param name="start">Начальный индекс.</param>
    /// <param name="length">Длина.</param>
    /// <returns>Span записанных символов.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ReadOnlySpan<char> AsSpan(int start, int length)
    {
#if DEBUG
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (start + length > position) throw new ArgumentOutOfRangeException(nameof(length));
#endif
        return chars.Slice(start, length);
    }

    /// <summary>
    /// Очищает буфер.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ValueStringBuilder Clear()
    {
        if (isClearOnDispose) chars[..position].Clear();
        position = 0;
        return ref Unsafe.AsRef(ref this);
    }

    /// <summary>Добавляет строковое представление логического значения.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ValueStringBuilder Append(bool value)
    {
        Append(value ? bool.TrueString : bool.FalseString);
        return ref Unsafe.AsRef(ref this);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // HOT-PATH PRIMITIVES: int, uint, long, ulong, double - оптимизированы вручную
    // Остальные типы используют generic Append<T> через AppendSpanFormattable<T>
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>Добавляет строковое представление целого числа.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ValueStringBuilder Append(int value)
    {
        // int: max 11 chars (-2147483648)
        var pos = position;
        if (pos > chars.Length - 11) Grow(11);
        value.TryFormat(chars[pos..], out var written, default, CultureInfo.InvariantCulture);
        position = pos + written;
        return ref Unsafe.AsRef(ref this);
    }

    /// <summary>Добавляет строковое представление беззнакового целого.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ValueStringBuilder Append(uint value)
    {
        // uint: max 10 chars (4294967295)
        var pos = position;
        if (pos > chars.Length - 10) Grow(10);
        value.TryFormat(chars[pos..], out var written, default, CultureInfo.InvariantCulture);
        position = pos + written;
        return ref Unsafe.AsRef(ref this);
    }

    /// <summary>Добавляет строковое представление целого числа (64-bit).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ValueStringBuilder Append(long value)
    {
        // long: max 20 chars (-9223372036854775808)
        var pos = position;
        if (pos > chars.Length - 20) Grow(20);
        value.TryFormat(chars[pos..], out var written, default, CultureInfo.InvariantCulture);
        position = pos + written;
        return ref Unsafe.AsRef(ref this);
    }

    /// <summary>Добавляет строковое представление длинного беззнакового целого.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ValueStringBuilder Append(ulong value)
    {
        // ulong: max 20 chars (18446744073709551615)
        var pos = position;
        if (pos > chars.Length - 20) Grow(20);
        value.TryFormat(chars[pos..], out var written, default, CultureInfo.InvariantCulture);
        position = pos + written;
        return ref Unsafe.AsRef(ref this);
    }

    /// <summary>Добавляет строковое представление числа с плавающей точкой.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ValueStringBuilder Append(double value)
    {
        // double: max ~24 chars, use 32 for safety
        var pos = position;
        if (pos > chars.Length - 32) Grow(32);
        value.TryFormat(chars[pos..], out var written, default, CultureInfo.InvariantCulture);
        position = pos + written;
        return ref Unsafe.AsRef(ref this);
    }

    /// <summary>Добавляет строковое представление значения ISpanFormattable с форматом.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ValueStringBuilder Append<T>(T value, string? format) where T : ISpanFormattable
    {
        AppendSpanFormattable(value, format.AsSpan(), CultureInfo.InvariantCulture, alignment: 0);
        return ref Unsafe.AsRef(ref this);
    }

    /// <summary>Добавляет строковое представление объекта.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ValueStringBuilder Append(object? value)
    {
        // Приоритет: ISpanFormattable (без промежуточных строк), затем IFormattable, затем ToString()
        if (value is ISpanFormattable spanFormattable)
        {
            AppendSpanFormattable(spanFormattable, default, CultureInfo.InvariantCulture, alignment: 0);
            return ref Unsafe.AsRef(ref this);
        }

        if (value is IFormattable formattable)
        {
            var formatted = formattable.ToString(format: null, formatProvider: CultureInfo.InvariantCulture);
            if (formatted is not null)
                Append(formatted);
            return ref Unsafe.AsRef(ref this);
        }

        Append(value?.ToString());
        return ref Unsafe.AsRef(ref this);
    }

    /// <summary>Добавляет значение, поддерживающее форматирование без выделений.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ValueStringBuilder Append<T>(T value) where T : ISpanFormattable
    {
        AppendSpanFormattable(value, default, CultureInfo.InvariantCulture, alignment: 0);
        return ref Unsafe.AsRef(ref this);
    }

    /// <summary>Добавляет строку.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public ref ValueStringBuilder Append(string? value)
    {
        if (value is null) return ref Unsafe.AsRef(ref this);

        var len = value.Length;
        if (len == 0) return ref Unsafe.AsRef(ref this);

        var pos = position;
        var required = pos + len;

        // Phase 2: Unified capacity check - один branch вместо двух
        if ((uint)required > (uint)chars.Length)
        {
            if ((uint)required > (uint)maxCapacity) ThrowCapacityExceeded();

            var currentLength = chars.Length;
            var newCapacity = (int)Math.Min(
                Math.Max((uint)required, (uint)currentLength << 1),
                (uint)maxCapacity);

            var array = ArrayPool<char>.Shared.Rent(newCapacity);
            chars[..pos].CopyTo(array);

            if (buffer is { } oldBuffer)
                ArrayPool<char>.Shared.Return(oldBuffer, clearArray: isClearOnDispose);

            buffer = array;
            chars = array;
        }

        // Unified path: один вызов Unsafe.CopyBlockUnaligned для всех размеров
        Unsafe.CopyBlockUnaligned(
            ref Unsafe.As<char, byte>(ref Unsafe.Add(ref MemoryMarshal.GetReference(chars), pos)),
            ref Unsafe.As<char, byte>(ref Unsafe.AsRef(in value.GetPinnableReference())),
            (uint)(len * sizeof(char)));

        position = required;
        return ref Unsafe.AsRef(ref this);
    }

    /// <summary>Добавляет диапазон символов.</summary>
    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ValueStringBuilder Append(scoped ReadOnlySpan<char> value)
    {
        var len = value.Length;
        if (len == 0) return ref Unsafe.AsRef(ref this);

        var pos = position;
        var required = pos + len;

        // Phase 2: Unified check - вычисляем required один раз
        if ((uint)required > (uint)chars.Length)
        {
            Grow(len);
        }

        value.CopyTo(chars[pos..]);
        position = required;
        return ref Unsafe.AsRef(ref this);
    }

    /// <summary>Добавляет значение, поддерживающее форматирование без выделений.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ValueStringBuilder Append<T>(T value, ReadOnlySpan<char> format, IFormatProvider? provider = default)
        where T : ISpanFormattable
    {
        AppendSpanFormattable(value, format, provider, alignment: 0);
        return ref Unsafe.AsRef(ref this);
    }

    /// <summary>Добавляет значение другого <see cref="StringBuilder"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ValueStringBuilder Append(StringBuilder? builder)
    {
        if (builder is null || builder.Length == 0) return ref Unsafe.AsRef(ref this);

        var length = builder.Length;
        var pos = position;
        var required = pos + length;

        if ((uint)required > (uint)chars.Length) Grow(length);

        builder.CopyTo(0, chars[pos..], length);
        position = required;

        return ref Unsafe.AsRef(ref this);
    }

    /// <summary>Добавляет одиночный символ.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ValueStringBuilder Append(char value)
    {
        var pos = position;

        // Прямой доступ к chars без локальной переменной
        if ((uint)pos < (uint)chars.Length)
        {
            chars[pos] = value;
            position = pos + 1;
        }
        else
        {
            GrowAndAppend(value);
        }

        return ref Unsafe.AsRef(ref this);
    }

    /// <summary>Добавляет указанное количество повторений символа.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ValueStringBuilder Append(char value, int repeatCount)
    {
#if DEBUG
        ArgumentOutOfRangeException.ThrowIfNegative(repeatCount);
#endif
        if (repeatCount is 0) return ref Unsafe.AsRef(ref this);

        var pos = position;
        if (pos > chars.Length - repeatCount) Grow(repeatCount);

        chars.Slice(pos, repeatCount).Fill(value);
        position = pos + repeatCount;

        return ref Unsafe.AsRef(ref this);
    }

    /// <summary>Добавляет массив символов.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ValueStringBuilder Append(char[]? value)
    {
        if (value is null || value.Length is 0) return ref Unsafe.AsRef(ref this);
        return ref Append(value.AsSpan());
    }

    /// <summary>Добавляет часть массива символов.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ValueStringBuilder Append([NotNull] char[] value, int startIndex, int charCount)
    {
#if DEBUG
        ArgumentOutOfRangeException.ThrowIfNegative(startIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(charCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(startIndex, value.Length - charCount);
#endif
        return ref Append(value.AsSpan(startIndex, charCount));
    }

    /// <summary>Добавляет интерполированную строку с использованием оптимизированного обработчика.</summary>
    /// <param name="handler">Обработчик интерполированной строки.</param>
    /// <remarks>
    /// Fast path: данные записываются напрямую в буфер builder'а (zero allocation).
    /// Slow path: если буфера не хватило, данные накапливаются во временном буфере из ArrayPool,
    /// затем копируются в расширенный буфер builder'а и возвращаются в пул.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ValueStringBuilder Append([InterpolatedStringHandlerArgument("")] scoped AppendInterpolatedStringHandler handler)
    {
        // Fast path: всё записано напрямую в наш буфер
        if (handler.TryGetDirectResult(out var newPos))
        {
            position = newPos;
            return ref Unsafe.AsRef(ref this);
        }

        // Slow path: handler использовал собственный буфер
        // Получаем данные из overflow буфера
        var ownBuffer = handler.OwnBuffer!;
        var ownPos = handler.OwnPosition;

        // Сбрасываем позицию на начало (handler писал в direct буфер до переполнения,
        // но потом скопировал всё в own buffer)
        position = handler.StartPosition;

        // Теперь копируем всё из own buffer в наш (расширенный) буфер
        var data = ownBuffer.AsSpan(0, ownPos);
        Append(data);

        // Возвращаем буфер в пул
        ArrayPool<char>.Shared.Return(ownBuffer);

        return ref Unsafe.AsRef(ref this);
    }

    /// <summary>Добавляет перевод строки.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ValueStringBuilder AppendLine()
    {
        // Inline newline chars directly for better performance
        var pos = position;
        // Environment.NewLine is "\r\n" on Windows, "\n" on Unix
        if (OperatingSystem.IsWindows())
        {
            if (pos > chars.Length - 2) Grow(2);
            chars[pos] = '\r';
            chars[pos + 1] = '\n';
            position = pos + 2;
        }
        else
        {
            if ((uint)pos >= (uint)chars.Length) Grow(1);
            chars[pos] = '\n';
            position = pos + 1;
        }
        return ref Unsafe.AsRef(ref this);
    }

    /// <summary>Добавляет строку и перевод строки.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ValueStringBuilder AppendLine(string? value)
    {
        Append(value);
        return ref AppendLine();
    }

    /// <summary>Добавляет строку с форматированием.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ValueStringBuilder AppendFormat([NotNull] string format, [NotNull] params object?[] args) => ref AppendFormatted(CultureInfo.CurrentCulture, format, args);

    /// <summary>Добавляет строку с форматированием без выделения массива аргументов.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ValueStringBuilder AppendFormat([NotNull] string format, object? arg0) => ref AppendFormattedOne(CultureInfo.CurrentCulture, format, arg0);

    /// <summary>Добавляет строку с форматированием без выделения массива аргументов.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ValueStringBuilder AppendFormat([NotNull] string format, object? arg0, object? arg1) => ref AppendFormattedTwo(CultureInfo.CurrentCulture, format, arg0, arg1);

    /// <summary>Добавляет строку с форматированием без выделения массива аргументов.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ValueStringBuilder AppendFormat([NotNull] string format, object? arg0, object? arg1, object? arg2) => ref AppendFormattedThree(CultureInfo.CurrentCulture, format, arg0, arg1, arg2);

    /// <summary>Добавляет строку с форматированием и указанным провайдером.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ValueStringBuilder AppendFormat(IFormatProvider? provider, [NotNull] string format, [NotNull] params object?[] args) => ref AppendFormatted(provider ?? CultureInfo.CurrentCulture, format, args);

    /// <summary>Добавляет строку с форматированием и указанным провайдером без выделения массива аргументов.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ValueStringBuilder AppendFormat(IFormatProvider? provider, [NotNull] string format, object? arg0) => ref AppendFormattedOne(provider ?? CultureInfo.CurrentCulture, format, arg0);

    /// <summary>Добавляет строку с форматированием и указанным провайдером без выделения массива аргументов.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ValueStringBuilder AppendFormat(IFormatProvider? provider, [NotNull] string format, object? arg0, object? arg1) => ref AppendFormattedTwo(provider ?? CultureInfo.CurrentCulture, format, arg0, arg1);

    /// <summary>Добавляет строку с форматированием и указанным провайдером без выделения массива аргументов.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ValueStringBuilder AppendFormat(IFormatProvider? provider, [NotNull] string format, object? arg0, object? arg1, object? arg2) => ref AppendFormattedThree(provider ?? CultureInfo.CurrentCulture, format, arg0, arg1, arg2);

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private ref ValueStringBuilder AppendFormatted(IFormatProvider provider, string format, object?[] args)
    {
        AppendCompositeFormat(provider, format, args);
        return ref Unsafe.AsRef(ref this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private ref ValueStringBuilder AppendFormattedOne(IFormatProvider provider, string format, object? arg0)
    {
        // КРИТИЧЕСКАЯ ОПТИМИЗАЦИЯ: Inline fast-path только для ПРОСТЫХ форматов
        // Обходим парсинг и кеширование CompositeTemplate ТОЛЬКО когда это 100% безопасно

        // Fast-path 1: "{0}" - самый частый случай
        if (format.Length == 3 && format[0] == '{' && format[1] == '0' && format[2] == '}')
        {
            AppendFormattedArgument(arg0, default, 0, provider);
            return ref Unsafe.AsRef(ref this);
        }

        // Fast-path 2: "{0:XXX}" - ТОЛЬКО если это единственный placeholder и нет литералов
        // Проверяем: начинается с "{0:", заканчивается на "}", нет других '{' внутри
        if (format.Length > 4 && format.Length <= 20 && // Ограничиваем простыми форматами типа "{0:D4}"
            format[0] == '{' && format[1] == '0' && format[2] == ':' && format[^1] == '}')
        {
            // Убеждаемся что это единственный placeholder (нет других '{' после позиции 0)
            var hasOtherPlaceholders = false;
            for (var i = 3; i < format.Length - 1; i++)
            {
                if (format[i] == '{')
                {
                    hasOtherPlaceholders = true;
                    break;
                }
            }

            if (!hasOtherPlaceholders)
            {
                var formatSpec = format.AsSpan(3, format.Length - 4);
                AppendFormattedArgument(arg0, formatSpec, 0, provider);
                return ref Unsafe.AsRef(ref this);
            }
        }

        // Медленный путь: сложный формат с литералами или несколькими placeholders
        AppendSingleCompositeFormat(provider, format, arg0);
        return ref Unsafe.AsRef(ref this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private ref ValueStringBuilder AppendFormattedTwo(IFormatProvider provider, string format, object? arg0, object? arg1)
    {
        // Fast-path для "{0} {1}" и подобных простых случаев
        if (TryAppendSimpleTwoArgs(provider, format, arg0, arg1))
            return ref Unsafe.AsRef(ref this);

        AppendDoubleCompositeFormat(provider, format, arg0, arg1);
        return ref Unsafe.AsRef(ref this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private ref ValueStringBuilder AppendFormattedThree(IFormatProvider provider, string format, object? arg0, object? arg1, object? arg2)
    {
        AppendTripleCompositeFormat(provider, format, arg0, arg1, arg2);
        return ref Unsafe.AsRef(ref this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryAppendSimpleTwoArgs(IFormatProvider provider, string format, object? arg0, object? arg1)
    {
        // Простые паттерны: "{0} {1}", "{0},{1}", "{0}={1}"
        if (format.Length == 7 &&
            format[0] == '{' && format[1] == '0' && format[2] == '}' &&
            format[^3] == '{' && format[^2] == '1' && format[^1] == '}')
        {
            // Паттерн: "{0}X{1}" где X - один разделитель
            AppendFormattedArgument(arg0, default, 0, provider);
            Append(format[3]);
            AppendFormattedArgument(arg1, default, 0, provider);
            return true;
        }

        return false;
    }

    /// <summary>Вставляет строку по указанному индексу.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ValueStringBuilder Insert(int index, string? value)
    {
        if (string.IsNullOrEmpty(value)) return ref Unsafe.AsRef(ref this);
        return ref Insert(index, value.AsSpan());
    }

    /// <summary>Вставляет строковое представление логического значения.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ValueStringBuilder Insert(int index, bool value) => ref Insert(index, value ? bool.TrueString : bool.FalseString);

    /// <summary>Вставляет строковое представление ISpanFormattable значения.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ValueStringBuilder Insert<T>(int index, T value) where T : ISpanFormattable => ref InsertFormatted(index, value);

    /// <summary>Вставляет строковое представление объекта.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ValueStringBuilder Insert(int index, object? value)
    {
        if (value is null) return ref Unsafe.AsRef(ref this);
        var str = value.ToString();
        if (str is not null) Insert(index, str);
        return ref Unsafe.AsRef(ref this);
    }

    /// <summary>Вставляет символ по указанному индексу.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ValueStringBuilder Insert(int index, char value)
    {
        var span = MemoryMarshal.CreateReadOnlySpan(ref value, 1);
        return ref Insert(index, span);
    }

    /// <summary>Вставляет часть массива символов.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ValueStringBuilder Insert(int index, char[]? value, int startIndex, int charCount)
    {
        if (value is null) return ref Unsafe.AsRef(ref this);
#if DEBUG
        ArgumentOutOfRangeException.ThrowIfNegative(startIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(charCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(startIndex, value.Length - charCount);
#endif
        return ref Insert(index, value.AsSpan(startIndex, charCount));
    }

    /// <summary>Вставляет диапазон символов.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ValueStringBuilder Insert(int index, ReadOnlySpan<char> value)
    {
        var pos = position;
        if ((uint)index > (uint)pos) ThrowOutOfRange();
        if (value.IsEmpty) return ref Unsafe.AsRef(ref this);

        var len = value.Length;
        var newPos = pos + len;

        // Fast-path: достаточно места
        if ((uint)newPos <= (uint)chars.Length)
        {
            // Сдвигаем хвост вправо (Span.CopyTo корректно обрабатывает overlapping regions)
            var tailLen = pos - index;
            if (tailLen > 0)
            {
                chars.Slice(index, tailLen).CopyTo(chars.Slice(index + len, tailLen));
            }

            value.CopyTo(chars.Slice(index, len));
            position = newPos;
            return ref Unsafe.AsRef(ref this);
        }

        // Slow-path: нужно расширение буфера
        return ref InsertSlow(index, value, pos, newPos);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private ref ValueStringBuilder InsertSlow(int index, ReadOnlySpan<char> value, int pos, int newPos)
    {
        // Grow при необходимости
        GrowAndInsert(index, value, pos, newPos);
        return ref Unsafe.AsRef(ref this);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SkipLocalsInit]
    private ref ValueStringBuilder InsertFormatted<T>(int index, T value) where T : ISpanFormattable
    {
        // Stackalloc instead of ArrayPool for better performance (no rent/return overhead)
        Span<char> tempBuffer = stackalloc char[64];
        if (value.TryFormat(tempBuffer, out var written, default, CultureInfo.InvariantCulture))
        {
            InsertSpanDirect(index, tempBuffer, written);
            return ref Unsafe.AsRef(ref this);
        }
        // Fallback for large values - use heap buffer
        var heapBuffer = ArrayPool<char>.Shared.Rent(256);
        try
        {
            if (value.TryFormat(heapBuffer, out written, default, CultureInfo.InvariantCulture))
            {
                Insert(index, heapBuffer.AsSpan(0, written));
                return ref Unsafe.AsRef(ref this);
            }
            var str = value.ToString();
            if (str is not null) Insert(index, str);
            return ref Unsafe.AsRef(ref this);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(heapBuffer);
        }
    }

    // Helper to insert stackalloc span without escaping issues
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void InsertSpanDirect(int index, scoped Span<char> source, int length)
    {
        if ((uint)index > (uint)position) ThrowOutOfRange();
        if (length == 0) return;
        if (index == position) { AppendSpanDirect(source, length); return; }

        var pos = position;
        var newPos = pos + length;

        if ((uint)newPos > (uint)chars.Length) Grow(length);

        var tailLen = pos - index;
        if (tailLen > 0)
            chars.Slice(index, tailLen).CopyTo(chars.Slice(index + length, tailLen));

        source[..length].CopyTo(chars.Slice(index, length));
        position = newPos;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendSpanDirect(scoped Span<char> source, int length)
    {
        var pos = position;
        if (pos > chars.Length - length) Grow(length);
        source[..length].CopyTo(chars[pos..]);
        position = pos + length;
    }



    [MethodImpl(MethodImplOptions.NoInlining)]
    private void GrowAndInsert(int index, ReadOnlySpan<char> value, int pos, int newPos)
    {
        if ((uint)newPos > (uint)maxCapacity) ThrowCapacityExceeded();

        var len = value.Length;
        var currentLength = chars.Length;
        var growth = currentLength < 1024 ? (uint)currentLength : ((uint)currentLength >> 1);
        var newCapacity = (int)Math.Max((uint)newPos,
            Math.Min((uint)currentLength + growth, (uint)maxCapacity));

        var array = ArrayPool<char>.Shared.Rent(newCapacity);

        // Копируем с учётом вставки сразу
        chars[..index].CopyTo(array);
        value.CopyTo(array.AsSpan(index, len));
        chars[index..pos].CopyTo(array.AsSpan(index + len));

        if (buffer is { } oldBuffer)
            ArrayPool<char>.Shared.Return(oldBuffer, clearArray: isClearOnDispose);

        buffer = array;
        chars = array;
        position = newPos;
    }

    /// <summary>Удаляет заданное количество символов.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ValueStringBuilder Remove(int startIndex, int count)
    {
#if DEBUG
        if (startIndex < 0 || count < 0 || startIndex > position || count > position - startIndex) ThrowOutOfRange();
#endif
        if (count is 0) return ref Unsafe.AsRef(ref this);

        chars[(startIndex + count)..position].CopyTo(chars[startIndex..]);
        position -= count;

        return ref Unsafe.AsRef(ref this);
    }

    /// <summary>Заменяет все вхождения указанной строки.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ValueStringBuilder Replace(string oldValue, string? newValue)
    {
        if (string.IsNullOrEmpty(oldValue)) return ref Unsafe.AsRef(ref this);

        if (oldValue.Length == 1 && newValue is { Length: 1 })
        {
            ReplaceCharInternal(oldValue[0], newValue[0]);
        }
        else
        {
            ReplaceSpanInternal(oldValue.AsSpan(), (newValue ?? string.Empty).AsSpan());
        }

        return ref Unsafe.AsRef(ref this);
    }

    /// <summary>Заменяет все вхождения указанной строки в заданном диапазоне.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ValueStringBuilder Replace(string oldValue, string? newValue, int startIndex, int count)
    {
        if (string.IsNullOrEmpty(oldValue) || count == 0) return ref Unsafe.AsRef(ref this);

        if (oldValue.Length == 1 && newValue is { Length: 1 })
        {
            ReplaceCharInternalRange(oldValue[0], newValue[0], startIndex, count);
        }
        else
        {
            ReplaceStringInternalRange(oldValue, newValue, startIndex, count);
        }

        return ref Unsafe.AsRef(ref this);
    }

    /// <summary>Заменяет все вхождения символа на другой символ.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ValueStringBuilder Replace(char oldValue, char newValue)
    {
        ReplaceCharInternal(oldValue, newValue);
        return ref Unsafe.AsRef(ref this);
    }

    /// <summary>Заменяет все вхождения символа на другой символ в заданном диапазоне.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ValueStringBuilder Replace(char oldChar, char newChar, int startIndex, int count)
    {
#if DEBUG
        ArgumentOutOfRangeException.ThrowIfNegative(startIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (startIndex + count > position)
            throw new ArgumentOutOfRangeException(nameof(count));
#endif
        if (count == 0 || oldChar == newChar) return ref Unsafe.AsRef(ref this);

        ReplaceCharInternalRange(oldChar, newChar, startIndex, count);
        return ref Unsafe.AsRef(ref this);
    }

    /// <summary>Заменяет все вхождения указанного диапазона символов на другой диапазон (zero-allocation).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref ValueStringBuilder Replace(ReadOnlySpan<char> oldValue, ReadOnlySpan<char> newValue)
    {
        if (oldValue.Length == 1 && newValue.Length == 1)
        {
            ReplaceCharInternal(oldValue[0], newValue[0]);
        }
        else
        {
            ReplaceSpanInternal(oldValue, newValue);
        }

        return ref Unsafe.AsRef(ref this);
    }

    /// <summary>
    /// Вставляет значение по индексу с одновременной заменой символов - batch операция для оптимизации.
    /// Обходит двойной проход памяти (Insert затем Replace).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public ref ValueStringBuilder InsertAndReplace(int index, ReadOnlySpan<char> value, char oldChar, char newChar)
    {
        if ((uint)index > (uint)position) ThrowOutOfRange();
        if (value.IsEmpty) return ref Unsafe.AsRef(ref this);
        if (oldChar == newChar) return ref Insert(index, value);

        var len = value.Length;
        var pos = position;
        var newPos = pos + len;

        // Проверка capacity
        if ((uint)newPos > (uint)chars.Length)
        {
            if ((uint)newPos > (uint)maxCapacity) ThrowCapacityExceeded();
            Grow(len);
        }

        var tailLen = pos - index;

        // Сдвигаем tail если нужно (Span.CopyTo корректно обрабатывает overlapping)
        if (tailLen > 0)
        {
            chars.Slice(index, tailLen).CopyTo(chars.Slice(index + len, tailLen));
        }

        // Копируем value с одновременной заменой символов
        ref var destRef = ref Unsafe.Add(ref MemoryMarshal.GetReference(chars), index);
        for (var i = 0; i < len; i++)
        {
            var ch = value[i];
            Unsafe.Add(ref destRef, i) = ch == oldChar ? newChar : ch;
        }

        position = newPos;

        // Заменяем в tail (если нужно)
        if (tailLen > 0)
        {
            ReplaceCharInternalRange(oldChar, newChar, index + len, tailLen);
        }

        return ref Unsafe.AsRef(ref this);
    }

    /// <summary>Копирует диапазон символов в целевой буфер.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public readonly void CopyTo(int sourceIndex, Span<char> destination, int count)
    {
#if DEBUG
        ArgumentOutOfRangeException.ThrowIfNegative(sourceIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, destination.Length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sourceIndex, position);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, position - sourceIndex);
#endif

        chars.Slice(sourceIndex, count).CopyTo(destination);
    }

    /// <summary>Копирует диапазон символов в массив.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void CopyTo(int sourceIndex, char[] destination, int destinationIndex, int count)
    {
#if DEBUG
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(destinationIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (sourceIndex + count > position)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (destinationIndex + count > destination.Length)
            throw new ArgumentOutOfRangeException(nameof(count));
#endif
        chars.Slice(sourceIndex, count).CopyTo(destination.AsSpan(destinationIndex));
    }

    /// <summary>Гарантирует указанную емкость буфера.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int EnsureCapacity(int capacity)
    {
#if DEBUG
        if (capacity < 0) ThrowOutOfRange();
#endif
        if (capacity <= Capacity) return Capacity;

        Grow(capacity - position);
        return Capacity;
    }

    /// <summary>Создаёт строку из указанного диапазона.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly string ToString(int startIndex, int count)
    {
#if DEBUG
        if (startIndex < 0 || count < 0 || startIndex > position || count > position - startIndex) ThrowOutOfRange();
#endif
        // Оптимизация: ранний выход для пустого диапазона
        if (count == 0) return string.Empty;
        return chars.Slice(startIndex, count).ToString();
    }

    /// <summary>Возвращает текущее содержимое в виде строки.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override readonly string ToString() => chars[..position].ToString();

    /// <summary>Высвобождает буфер и возвращает его в пул.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        // Упрощенная логика: buffer всегда есть, так как убран stack buffer
        if (buffer is { } array)
        {
            buffer = null;
            ArrayPool<char>.Shared.Return(array, clearArray: isClearOnDispose);
            chars = [];
            position = 0;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void GrowAndAppend(char value)
    {
        Grow(1);
        // Прямая запись вместо повторного вызова Append(char)
        chars[position++] = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Grow(int additionalCapacityBeyondPos)
    {
        var required = position + additionalCapacityBeyondPos;

        // Hot path: проверка лимита с вероятностью early return
        if ((uint)required > (uint)MaxCapacity)
            ThrowCapacityExceeded();

        GrowCore(required);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowCapacityExceeded() =>
        throw new InvalidOperationException("Превышен максимально допустимый размер ValueStringBuilder");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void GrowCore(int required)
    {
        var currentLength = chars.Length;
        var cap = MaxCapacity;

        // Phase 2: Агрессивный рост 2x для всех размеров
        var newCapacity = (int)Math.Min(
            Math.Max((uint)required, (uint)currentLength << 1),
            (uint)cap);

        var array = ArrayPool<char>.Shared.Rent(newCapacity);
        chars[..position].CopyTo(array);

        var toReturn = buffer;
        chars = array;
        buffer = array;

        if (toReturn is not null)
        {
            ArrayPool<char>.Shared.Return(toReturn, clearArray: isClearOnDispose);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ReplaceStringInternalRange(string oldValue, string? newValue, int startIndex, int count)
    {
        var originalPos = position;
        var tempPos = startIndex + count;

        // Временно ограничиваем диапазон поиска
        position = tempPos;

        var newSpan = newValue is null ? default : newValue.AsSpan();
        ReplaceSpanInternal(oldValue.AsSpan(), newSpan);

        // Восстанавливаем позицию с учетом изменений
        var delta = position - tempPos;
        position = delta != 0 ? originalPos + delta : originalPos;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly void ReplaceCharInternalRange(char oldChar, char newChar, int startIndex, int count)
    {
        if (oldChar != newChar)
        {
            var span = chars.Slice(startIndex, count);
            ReplaceCharScalar(span, oldChar, newChar);
        }
    }

    private void ReplaceSpanInternal(ReadOnlySpan<char> oldSpan, ReadOnlySpan<char> newSpan)
    {
        if (oldSpan.IsEmpty)
            throw new ArgumentException("Old value cannot be empty", nameof(oldSpan));

        var delta = newSpan.Length - oldSpan.Length;

        // Одно-проходная streaming замена без предварительного массива позиций
        if (delta == 0)
        {
            ReplaceInPlaceSameLength(oldSpan, newSpan);
            return;
        }

        if (delta > 0)
        {
            ReplaceWithExpansion(oldSpan, newSpan, delta);
        }
        else
        {
            ReplaceWithShrinking(oldSpan, newSpan);
        }
    }

    private readonly void ReplaceInPlaceSameLength(ReadOnlySpan<char> oldSpan, ReadOnlySpan<char> newSpan)
    {
        // Fast-path для single char replacement
        if (oldSpan.Length == 1 && newSpan.Length == 1)
        {
            var oldChar = oldSpan[0];
            var newChar = newSpan[0];
            var span = chars[..position];
            for (var i = 0; i < span.Length; i++)
            {
                if (span[i] == oldChar)
                    span[i] = newChar;
            }
            return;
        }

        var searchPos = 0;
        var oldLen = oldSpan.Length;

        while (searchPos <= position - oldLen)
        {
            var slice = chars[searchPos..position];
            var idx = slice.IndexOf(oldSpan, StringComparison.Ordinal);
            if (idx < 0) break;

            var pos = searchPos + idx;
            newSpan.CopyTo(chars.Slice(pos, newSpan.Length));
            searchPos = pos + oldLen;
        }
    }

    private void ReplaceWithExpansion(ReadOnlySpan<char> oldSpan, ReadOnlySpan<char> newSpan, int delta)
    {
        // ОПТИМИЗАЦИЯ: Ultra-fast path для single-char expansion (самый частый случай)
        if (oldSpan.Length == 1)
        {
            ReplaceSingleCharExpanding(oldSpan[0], newSpan, delta);
            return;
        }

        // Fast-path для коротких паттернов (2-4 символа)
        if ((uint)(oldSpan.Length - 1) < 4)
        {
            ReplaceShortPatternExpanding(oldSpan, newSpan, delta);
            return;
        }

        const int MaxStackPositions = 32;
        Span<int> stackPositions = stackalloc int[MaxStackPositions];
        int[]? rentedPositions = null;
        var positions = stackPositions;
        var occurrenceCount = 0;
        var searchPos = 0;
        var oldLen = oldSpan.Length;

        // Поиск всех вхождений (SIMD-ускоренный для single char)
        while (searchPos <= position - oldLen)
        {
#if !DEBUG
            // SIMD fast-path для single-char search
            var idx = oldLen == 1
                ? VectorizedIndexOf(chars[searchPos..position], oldSpan[0])
                : chars[searchPos..position].IndexOf(oldSpan, StringComparison.Ordinal);
#else
            var idx = chars[searchPos..position].IndexOf(oldSpan, StringComparison.Ordinal);
#endif
            if (idx < 0) break;

            if (occurrenceCount == positions.Length)
            {
                var newRented = ArrayPool<int>.Shared.Rent(positions.Length * 2);
                positions.CopyTo(newRented);
                if (rentedPositions is not null) ArrayPool<int>.Shared.Return(rentedPositions);
                rentedPositions = newRented;
                positions = newRented;
            }

            positions[occurrenceCount++] = searchPos + idx;
            searchPos += idx + oldLen;
        }

        if (occurrenceCount == 0)
        {
            if (rentedPositions is not null) ArrayPool<int>.Shared.Return(rentedPositions);
            return;
        }

        PerformBackwardReplacements(oldLen, newSpan, delta, positions, occurrenceCount);
        if (rentedPositions is not null) ArrayPool<int>.Shared.Return(rentedPositions);
    }

    private void PerformBackwardReplacements(int oldLen, ReadOnlySpan<char> newSpan, int delta, scoped Span<int> positions, int occurrenceCount)
    {
        var newLen = newSpan.Length;
        var newPosition = position + (delta * occurrenceCount);
        EnsureCapacity(newPosition);

        // Unsafe pointer-based backward replacement для минимизации overhead
        unsafe
        {
            fixed (char* charsPtr = chars)
            fixed (char* newPtr = newSpan)
            {
                var writePos = newPosition;
                var readPos = position;

                for (var i = occurrenceCount - 1; i >= 0; i--)
                {
                    var matchPos = positions[i];
                    var tailLen = readPos - matchPos - oldLen;

                    // Копируем хвост через Buffer.MemoryCopy (overlapping-safe при backward)
                    if (tailLen > 0)
                    {
                        writePos -= tailLen;
                        Buffer.MemoryCopy(
                            charsPtr + matchPos + oldLen,
                            charsPtr + writePos,
                            (nuint)tailLen * sizeof(char),
                            (nuint)tailLen * sizeof(char));
                    }

                    // Записываем замену
                    writePos -= newLen;
                    Buffer.MemoryCopy(
                        newPtr,
                        charsPtr + writePos,
                        (nuint)newLen * sizeof(char),
                        (nuint)newLen * sizeof(char));

                    readPos = matchPos;
                }
            }
        }

        position = newPosition;
    }

    /// <summary>
    /// Ultra-fast single-char expansion: single backward pass using SIMD LastIndexOf.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ReplaceSingleCharExpanding(char oldChar, ReadOnlySpan<char> newSpan, int delta)
    {
        // Быстрый подсчёт вхождений для определения нового размера
        var srcLen = position;
        var span = chars[..srcLen];
        var count = span.Count(oldChar);
        if (count == 0) return;

        var newLen = newSpan.Length;
        var newPosition = srcLen + (delta * count);
        EnsureCapacity(newPosition);

        // Однопроходный алгоритм с конца через Unsafe pointer arithmetic
        ref var charsRef = ref MemoryMarshal.GetReference(chars);
        ref var newRef = ref MemoryMarshal.GetReference(newSpan);
        var readEnd = srcLen;
        var writeEnd = newPosition;

        while (readEnd > 0)
        {
            // SIMD-ускоренный поиск с конца
            var matchPos = chars[..readEnd].LastIndexOf(oldChar);

            if (matchPos < 0)
            {
                // Нет больше вхождений - копируем оставшееся через Unsafe.CopyBlock
                writeEnd -= readEnd;
                Unsafe.CopyBlock(
                    ref Unsafe.As<char, byte>(ref Unsafe.Add(ref charsRef, writeEnd)),
                    ref Unsafe.As<char, byte>(ref charsRef),
                    (uint)(readEnd * sizeof(char)));
                break;
            }

            // Копируем сегмент ПОСЛЕ найденного символа (от matchPos+1 до readEnd)
            var segmentStart = matchPos + 1;
            var segmentLen = readEnd - segmentStart;
            if (segmentLen > 0)
            {
                writeEnd -= segmentLen;
                Unsafe.CopyBlock(
                    ref Unsafe.As<char, byte>(ref Unsafe.Add(ref charsRef, writeEnd)),
                    ref Unsafe.As<char, byte>(ref Unsafe.Add(ref charsRef, segmentStart)),
                    (uint)(segmentLen * sizeof(char)));
            }

            // Копируем замену
            writeEnd -= newLen;
            Unsafe.CopyBlock(
                ref Unsafe.As<char, byte>(ref Unsafe.Add(ref charsRef, writeEnd)),
                ref Unsafe.As<char, byte>(ref newRef),
                (uint)(newLen * sizeof(char)));

            readEnd = matchPos;
        }

        position = newPosition;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ReplaceShortPatternExpanding(ReadOnlySpan<char> oldSpan, ReadOnlySpan<char> newSpan, int delta)
    {
        // Single-pass: collect positions then replace backward (avoid O(n*m) double-scan)
        const int MaxStackPositions = 64;
        Span<int> stackPositions = stackalloc int[MaxStackPositions];
        int[]? rentedPositions = null;
        var positions = stackPositions;
        var occurrenceCount = 0;

        var firstChar = oldSpan[0];
        var oldLen = oldSpan.Length;
        var span = chars[..position];

        // Single-pass collection of all positions
        for (var i = 0; i <= span.Length - oldLen; i++)
        {
            if (span[i] == firstChar && span.Slice(i, oldLen).SequenceEqual(oldSpan))
            {
                if (occurrenceCount == positions.Length)
                {
                    var newRented = ArrayPool<int>.Shared.Rent(positions.Length * 2);
                    positions.CopyTo(newRented);
                    if (rentedPositions is not null) ArrayPool<int>.Shared.Return(rentedPositions);
                    rentedPositions = newRented;
                    positions = newRented;
                }
                positions[occurrenceCount++] = i;
#pragma warning disable S127 // Loop counter modification required for skip optimization
                i += oldLen - 1;
#pragma warning restore S127
            }
        }

        if (occurrenceCount == 0)
        {
            if (rentedPositions is not null) ArrayPool<int>.Shared.Return(rentedPositions);
            return;
        }

        PerformBackwardReplacements(oldLen, newSpan, delta, positions, occurrenceCount);
        if (rentedPositions is not null) ArrayPool<int>.Shared.Return(rentedPositions);
    }

    private void ReplaceWithShrinking(ReadOnlySpan<char> oldSpan, ReadOnlySpan<char> newSpan)
    {
        var oldLen = oldSpan.Length;
        var newLen = newSpan.Length;

        // Unsafe pointer-based forward replacement
        unsafe
        {
            fixed (char* charsPtr = chars)
            fixed (char* newPtr = newSpan)
            {
                var writePos = 0;
                var readPos = 0;

                while (readPos <= position - oldLen)
                {
                    var slice = chars[readPos..position];
                    var idx = slice.IndexOf(oldSpan, StringComparison.Ordinal);
                    if (idx < 0) break;

                    // Копируем часть до вхождения
                    if (idx > 0)
                    {
                        if (writePos != readPos)
                        {
                            Buffer.MemoryCopy(
                                charsPtr + readPos,
                                charsPtr + writePos,
                                (nuint)idx * sizeof(char),
                                (nuint)idx * sizeof(char));
                        }
                        writePos += idx;
                    }

                    // Записываем замену
                    Buffer.MemoryCopy(
                        newPtr,
                        charsPtr + writePos,
                        (nuint)newLen * sizeof(char),
                        (nuint)newLen * sizeof(char));
                    writePos += newLen;
                    readPos += idx + oldLen;
                }

                // Copy remaining tail
                if (readPos < position)
                {
                    var remaining = position - readPos;
                    if (writePos != readPos)
                    {
                        Buffer.MemoryCopy(
                            charsPtr + readPos,
                            charsPtr + writePos,
                            (nuint)remaining * sizeof(char),
                            (nuint)remaining * sizeof(char));
                    }
                    writePos += remaining;
                }

                position = writePos;
            }
        }
    }
#if !DEBUG
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int VectorizedIndexOf(ReadOnlySpan<char> span, char value)
    {
        if (Vector256.IsHardwareAccelerated && span.Length >= Vector256<ushort>.Count)
        {
            return IndexOfVector256(span, value);
        }

        if (Vector128.IsHardwareAccelerated && span.Length >= Vector128<ushort>.Count)
        {
            return IndexOfVector128(span, value);
        }

        return span.IndexOf(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int IndexOfVector256(ReadOnlySpan<char> span, char value)
    {
        var searchVector = Vector256.Create((ushort)value);
        var vecCount = Vector256<ushort>.Count;
        var len = span.Length;

        ref var baseRef = ref Unsafe.As<char, ushort>(ref MemoryMarshal.GetReference(span));
        var i = 0;

        for (; i <= len - vecCount; i += vecCount)
        {
            ref var ptr = ref Unsafe.Add(ref baseRef, i);
            var current = Vector256.LoadUnsafe(ref ptr);
            var mask = Vector256.Equals(current, searchVector);
            var maskBits = mask.ExtractMostSignificantBits();
            if (maskBits != 0)
            {
                return i + BitOperations.TrailingZeroCount(maskBits);
            }
        }

        // Обработка остатка скалярно
        for (; i < len; i++)
        {
            if (span[i] == value) return i;
        }

        return -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int IndexOfVector128(ReadOnlySpan<char> span, char value)
    {
        var searchVector = Vector128.Create((ushort)value);
        var vecCount = Vector128<ushort>.Count;
        var len = span.Length;

        ref var baseRef = ref Unsafe.As<char, ushort>(ref MemoryMarshal.GetReference(span));
        var i = 0;

        for (; i <= len - vecCount; i += vecCount)
        {
            ref var ptr = ref Unsafe.Add(ref baseRef, i);
            var current = Vector128.LoadUnsafe(ref ptr);
            var mask = Vector128.Equals(current, searchVector);
            var maskBits = mask.ExtractMostSignificantBits();
            if (maskBits != 0)
            {
                return i + BitOperations.TrailingZeroCount(maskBits);
            }
        }

        // Обработка остатка скалярно
        for (; i < len; i++)
        {
            if (span[i] == value) return i;
        }

        return -1;
    }
#endif
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#if DEBUG
    private static void CopyCharsOptimized(Span<char> buffer, int srcStart, int dstStart, int length)
        => buffer.Slice(srcStart, length).CopyTo(buffer.Slice(dstStart, length));
#else
    private static unsafe void CopyCharsOptimized(Span<char> buffer, int srcStart, int dstStart, int length)
    {
        fixed (char* basePtr = buffer)
        {
            if (length >= UnsafeCopyThreshold)
            {
                Buffer.MemoryCopy(basePtr + srcStart, basePtr + dstStart, length * sizeof(char), length * sizeof(char));
                return;
            }

            // Unrolled copy for small lengths (1-7 chars)
            var src = basePtr + srcStart;
            var dst = basePtr + dstStart;
            if (length >= 7) dst[6] = src[6];
            if (length >= 6) dst[5] = src[5];
            if (length >= 5) dst[4] = src[4];
            if (length >= 4) dst[3] = src[3];
            if (length >= 3) dst[2] = src[2];
            if (length >= 2) dst[1] = src[1];
            if (length >= 1) dst[0] = src[0];
        }
    }
#endif

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly void ReplaceCharInternal(char oldValue, char newValue)
    {
        if (oldValue == newValue) return;
        var span = chars[..position];

#if !DEBUG
        // SIMD только при профите (breakeven: 64/32 символа)
        if (span.Length >= SimdThreshold256 && Vector256.IsHardwareAccelerated)
        {
            ReplaceCharVectorized256(span, oldValue, newValue);
            return;
        }

        if (span.Length >= SimdThreshold128 && Vector128.IsHardwareAccelerated)
        {
            ReplaceCharVectorized128(span, oldValue, newValue);
            return;
        }
#endif

        // Scalar fast-path для малых строк (<32 символов)
        ReplaceCharScalar(span, oldValue, newValue);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ReplaceCharScalar(Span<char> span, char oldValue, char newValue)
    {
        var len = span.Length;
        var len4 = len - 3;
        var i = 0;

        // 4x loop unrolling for better throughput
        for (; i < len4; i += 4)
        {
            if (span[i] == oldValue) span[i] = newValue;
            if (span[i + 1] == oldValue) span[i + 1] = newValue;
            if (span[i + 2] == oldValue) span[i + 2] = newValue;
            if (span[i + 3] == oldValue) span[i + 3] = newValue;
        }

        // Handle remainder
        for (; i < len; i++)
        {
            if (span[i] == oldValue) span[i] = newValue;
        }
    }

#if !DEBUG
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ReplaceCharVectorized256(Span<char> span, char oldValue, char newValue)
    {
        var oldVector = Vector256.Create((ushort)oldValue);
        var newVector = Vector256.Create((ushort)newValue);
        var vecCount = Vector256<ushort>.Count;
        var len = span.Length;

        ref var baseRef = ref Unsafe.As<char, ushort>(ref MemoryMarshal.GetReference(span));
        var i = 0;

        for (; i <= len - vecCount; i += vecCount)
        {
            ref var ptr = ref Unsafe.Add(ref baseRef, i);
            var current = Vector256.LoadUnsafe(ref ptr);
            var mask = Vector256.Equals(current, oldVector);
            var replaced = Vector256.ConditionalSelect(mask, newVector, current);
            replaced.StoreUnsafe(ref ptr);
        }

        // Обработка остатка
        for (; i < len; i++)
        {
            if (span[i] == oldValue) span[i] = newValue;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ReplaceCharVectorized128(Span<char> span, char oldValue, char newValue)
    {
        var oldVector = Vector128.Create((ushort)oldValue);
        var newVector = Vector128.Create((ushort)newValue);
        var vecCount = Vector128<ushort>.Count;
        var len = span.Length;

        ref var baseRef = ref Unsafe.As<char, ushort>(ref MemoryMarshal.GetReference(span));
        var i = 0;

        for (; i <= len - vecCount; i += vecCount)
        {
            ref var ptr = ref Unsafe.Add(ref baseRef, i);
            var current = Vector128.LoadUnsafe(ref ptr);
            var mask = Vector128.Equals(current, oldVector);
            var replaced = Vector128.ConditionalSelect(mask, newVector, current);
            replaced.StoreUnsafe(ref ptr);
        }

        // Обработка остатка
        for (; i < len; i++)
        {
            if (span[i] == oldValue) span[i] = newValue;
        }
    }
#endif

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ThrowOutOfRange(string? paramName = default) => throw new ArgumentOutOfRangeException(paramName);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void CopyStateFrom(ValueStringBuilder source)
    {
        // Phase 3: Копируем состояние из временного обработчика обратно в оригинал
        // Важно: если handler вызвал EnsureCapacity и произошёл рост буфера,
        // то нужно скопировать новые chars, buffer и position
        chars = source.chars;
        buffer = source.buffer;
        position = source.position;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void AppendSpanFormattable<T>(T value, ReadOnlySpan<char> format, IFormatProvider? provider, int alignment)
        where T : ISpanFormattable
    {
        provider ??= CultureInfo.InvariantCulture;

        // Fast-path: no alignment - write directly to buffer (avoid stackalloc + copy)
        if (alignment == 0)
        {
            const int InitialCapacity = 64;
            EnsureCapacity(position + InitialCapacity);
            if (value.TryFormat(chars[position..], out var written, format, provider))
            {
                position += written;
                return;
            }
            // Grow and retry
            var newCapacity = InitialCapacity * 2;
            while (true)
            {
                EnsureCapacity(position + newCapacity);
                if (value.TryFormat(chars[position..], out written, format, provider))
                {
                    position += written;
                    return;
                }
                newCapacity = (int)(newCapacity * 1.5);
            }
        }

        // Slow path with alignment - use stackalloc
        var width = Math.Abs(alignment);
        const int StackBufferSize = 64;
        Span<char> stackDest = stackalloc char[StackBufferSize];
        if (value.TryFormat(stackDest, out var w, format, provider))
        {
            var requiredWidth = Math.Max(width, w);
            EnsureCapacity(position + requiredWidth);
            var dest = GetSpan(requiredWidth);

            var padding = requiredWidth - w;
            if (alignment < 0)
            {
                stackDest[..w].CopyTo(dest);
                dest.Slice(w, padding).Fill(' ');
            }
            else
            {
                dest[..padding].Fill(' ');
                stackDest[..w].CopyTo(dest[padding..]);
            }

            Advance(requiredWidth);
            return;
        }

        // Large value with alignment - heap buffer
        var newCap = Math.Max(width, StackBufferSize);
        var destination = GetSpan(newCap);

        while (!value.TryFormat(destination, out w, format, provider))
        {
            newCap = (int)(newCap * 1.5);
            EnsureCapacity(position + newCap);
            destination = GetSpan(newCap);
        }

        ApplyAlignment(destination, w, alignment);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // ULTRA-FAST primitives — кастомные форматтеры без TryFormat overhead
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>Ultra-fast int append - кастомный форматтер без TryFormat.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    internal void AppendIntFast(int value)
    {
        // Hot path: 11 chars max for int (-2147483648)
        const int MaxLen = 11;
        var pos = position;
        if (pos > chars.Length - MaxLen) Grow(MaxLen);

        var charsSpan = chars;
        if (value < 0)
        {
            if (value == int.MinValue)
            {
                // Special case: int.MinValue cannot be negated
                "-2147483648".CopyTo(charsSpan[pos..]);
                position = pos + 11;
                return;
            }
            charsSpan[pos++] = '-';
            value = -value;
        }

        // Fast path for small numbers (0-9999) - most common case
        if ((uint)value < 10)
        {
            charsSpan[pos] = (char)('0' + value);
            position = pos + 1;
            return;
        }
        if ((uint)value < 100)
        {
            charsSpan[pos] = (char)('0' + (value / 10));
            charsSpan[pos + 1] = (char)('0' + (value % 10));
            position = pos + 2;
            return;
        }
        if ((uint)value < 1000)
        {
            charsSpan[pos] = (char)('0' + (value / 100));
            charsSpan[pos + 1] = (char)('0' + (value / 10 % 10));
            charsSpan[pos + 2] = (char)('0' + (value % 10));
            position = pos + 3;
            return;
        }

        // General case: write digits in reverse, then reverse
        var startPos = pos;
        do
        {
            charsSpan[pos++] = (char)('0' + (value % 10));
            value /= 10;
        } while (value > 0);

        // Reverse the digits in-place
        var endPos = pos - 1;
        while (startPos < endPos)
        {
            (charsSpan[startPos], charsSpan[endPos]) = (charsSpan[endPos], charsSpan[startPos]);
            startPos++;
            endPos--;
        }
        position = pos;
    }

    /// <summary>Ultra-fast long append - кастомный форматтер.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    internal void AppendLongFast(long value)
    {
        const int MaxLen = 20;
        var pos = position;
        if (pos > chars.Length - MaxLen) Grow(MaxLen);

        var charsSpan = chars;
        if (value < 0)
        {
            if (value == long.MinValue)
            {
                "-9223372036854775808".CopyTo(charsSpan[pos..]);
                position = pos + 20;
                return;
            }
            charsSpan[pos++] = '-';
            value = -value;
        }

        // Fast path for small numbers
        if ((uint)value < 10)
        {
            charsSpan[pos] = (char)('0' + value);
            position = pos + 1;
            return;
        }

        // General case
        var startPos = pos;
        do
        {
            charsSpan[pos++] = (char)('0' + (value % 10));
            value /= 10;
        } while (value > 0);

        var endPos = pos - 1;
        while (startPos < endPos)
        {
            (charsSpan[startPos], charsSpan[endPos]) = (charsSpan[endPos], charsSpan[startPos]);
            startPos++;
            endPos--;
        }
        position = pos;
    }

    /// <summary>Ultra-fast double append - использует TryFormat (сложно оптимизировать вручную).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    internal void AppendDoubleFast(double value)
    {
        const int MaxLen = 32;
        var pos = position;
        if (pos > chars.Length - MaxLen) Grow(MaxLen);

        if (value.TryFormat(chars[pos..], out var written, default, CultureInfo.InvariantCulture))
            position = pos + written;
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // UNSAFE CHAR APPEND — для handler (capacity уже pre-allocated)
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>Добавляет 1 символ без проверки capacity (для handler).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void AppendCharUnsafe(char c) => chars[position++] = c;

    // Type-specialized AppendSpanFormattable с fast-path для int/long/double
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void AppendSpanFormattable(int value, ReadOnlySpan<char> format, IFormatProvider? provider, int alignment)
    {
        if (format.IsEmpty && alignment == 0) { AppendIntFast(value); return; }
        AppendSpanFormattableCore(value, format, provider, alignment, 16);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void AppendSpanFormattable(long value, ReadOnlySpan<char> format, IFormatProvider? provider, int alignment)
    {
        if (format.IsEmpty && alignment == 0) { AppendLongFast(value); return; }
        AppendSpanFormattableCore(value, format, provider, alignment, 24);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void AppendSpanFormattable(double value, ReadOnlySpan<char> format, IFormatProvider? provider, int alignment)
    {
        if (format.IsEmpty && alignment == 0) { AppendDoubleFast(value); return; }
        AppendSpanFormattableCore(value, format, provider, alignment, 32);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendSpanFormattableCore<T>(T value, ReadOnlySpan<char> format, IFormatProvider? provider, int alignment, int maxLen) where T : ISpanFormattable
    {
        var width = alignment is 0 ? 0 : Math.Abs(alignment);
        EnsureCapacity(position + Math.Max(maxLen, width));
        if (value.TryFormat(chars[position..], out var written, format, provider ?? CultureInfo.InvariantCulture))
            ApplyAlignment(chars[position..], written, alignment);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void AppendAligned(scoped ReadOnlySpan<char> value, int alignment)
    {
        if (alignment == 0)
        {
            Append(value);
            return;
        }

        var width = Math.Abs(alignment);
        var padding = width - value.Length;

        if (padding <= 0)
        {
            Append(value);
            return;
        }

        var destination = GetSpan(width);

        if (alignment < 0)
        {
            value.CopyTo(destination);
            destination.Slice(value.Length, padding).Fill(' ');
        }
        else
        {
            destination[..padding].Fill(' ');
            value.CopyTo(destination[padding..]);
        }

        Advance(width);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ApplyAlignment(Span<char> destination, int written, int alignment)
    {
        if (alignment == 0)
        {
            Advance(written);
            return;
        }

        var width = Math.Abs(alignment);
        var padding = width - written;

        if (padding <= 0)
        {
            Advance(written);
            return;
        }

        ApplyAlignmentCore(destination, written, padding, alignment);

        Advance(width);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ApplyAlignmentCore(Span<char> destination, int written, int padding, int alignment)
    {
        if (alignment < 0)
        {
            // Left-aligned: fill spaces after content
            destination.Slice(written, padding).Fill(' ');
        }
        else
        {
            // Right-aligned: memmove + fill (handles overlapping correctly)
            destination[..written].CopyTo(destination[padding..]);
            destination[..padding].Fill(' ');
        }
    }

    private void AppendSingleCompositeFormat(IFormatProvider provider, string format, object? arg0) =>
        AppendCompositeFormat(provider, format, arg0, arg1: null, arg2: null, argCount: 1);

    private static object? GetArgumentValue(int index, int argCount, object? arg0, object? arg1, object? arg2, ReadOnlySpan<object?> args)
    {
        if (index == 0 && argCount > 0) return arg0;
        if (index == 1 && argCount > 1) return arg1;
        if (index == 2 && argCount > 2) return arg2;
        if ((uint)index < (uint)args.Length) return args[index];
        return ThrowFormatReturn<object?>();
    }

    private void AppendDoubleCompositeFormat(IFormatProvider provider, string format, object? arg0, object? arg1) =>
        AppendCompositeFormat(provider, format, arg0, arg1, arg2: null, argCount: 2);

    private void AppendTripleCompositeFormat(IFormatProvider provider, string format, object? arg0, object? arg1, object? arg2) =>
        AppendCompositeFormat(provider, format, arg0, arg1, arg2, argCount: 3);

    private void AppendCompositeFormat(IFormatProvider provider, string format, ReadOnlySpan<object?> args) =>
        AppendCompositeFormat(provider, format, arg0: null, arg1: null, arg2: null, args, args.Length);

    private void AppendCompositeFormat(IFormatProvider provider, string format, object? arg0, object? arg1, object? arg2, int argCount) =>
        AppendCompositeFormat(provider, format, arg0, arg1, arg2, [], argCount);

    private void AppendCompositeFormat(IFormatProvider provider, string format, object? arg0, object? arg1, object? arg2, ReadOnlySpan<object?> args, int argCount)
    {
        // Передаём format напрямую, без промежуточного ToString()
        var template = CompositeTemplateCache.GetOrAdd(format);

        foreach (var segment in template.Segments)
        {
            if (segment.IsLiteral)
            {
                Append(segment.Literal);
                continue;
            }

            var index = (int)segment.Index;
            var value = GetArgumentValue(index, argCount, arg0, arg1, arg2, args);
            var fmtSpan = segment.Format is null ? default : segment.Format.AsSpan();
            AppendFormattedArgument(value, fmtSpan, segment.Alignment, provider);
        }
    }

    private void AppendFormattedArgument(object? arg, ReadOnlySpan<char> format, int alignment, IFormatProvider provider)
    {
        if (arg is null)
        {
            AppendAligned([], alignment);
            return;
        }

        if (arg is ISpanFormattable spanFormattable)
        {
            AppendSpanFormattable(spanFormattable, format, provider, alignment);
            return;
        }

        if (arg is IFormattable formattable)
        {
            // Fallback: IFormattable с временной строкой формата (no need to check ISpanFormattable again)
            var formatString = format.Length > 0 ? format.ToString() : null;
            var str = formattable.ToString(formatString, provider);
            AppendAligned(str.AsSpan(), alignment);
            return;
        }

        var strValue = arg.ToString();
        AppendAligned(strValue is null ? [] : strValue.AsSpan(), alignment);
    }

    [DoesNotReturn]
    private static void ThrowFormat() => throw new FormatException("Неверная строка формата");

    [DoesNotReturn]
    private static T ThrowFormatReturn<T>() => throw new FormatException("Неверная строка формата");

    // ═══════════════════════════════════════════════════════════════════════════════
    // NESTED INTERPOLATED STRING HANDLER
    // Двухрежимный: direct write в builder (fast path) или own buffer (slow path).
    // Fast path: zero allocation, прямая запись в переданный Span.
    // Slow path: ArrayPool буфер, когда оценочного размера не хватило.
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>Интерполированный обработчик для append операций.</summary>
    [InterpolatedStringHandler]
    [StructLayout(LayoutKind.Auto)]
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "Handler must be public for interpolated string pattern")]
    [SuppressMessage("Style", "IDE0044:Add readonly modifier", Justification = "Fields modified during append operations")]
    public ref struct AppendInterpolatedStringHandler
    {
#pragma warning disable IDE0032, S2933 // ref struct fields cannot be auto-properties, field is set in constructor only
        // Начальная позиция в builder'е (для расчёта написанного)
        private readonly int _startPos;

        // Direct mode: пишем прямо в буфер builder'а
        private readonly ref char _directRef;
        private readonly int _directCapacity;

        // Текущая позиция записи (абсолютная)
        private int _pos;

        // Overflow mode: собственный буфер когда direct не хватает
        private char[]? _ownBuffer;
        private int _ownPos;
#pragma warning restore IDE0032, S2933

        /// <summary>Создает handler для записи в указанный builder.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public AppendInterpolatedStringHandler(int literalLength, int formattedCount, ValueStringBuilder builder)
        {
            _startPos = builder.position;
            _pos = builder.position;

            // НЕ вызываем EnsureCapacity на копии builder'а!
            // Просто берём ссылку на текущий буфер.
            _directRef = ref MemoryMarshal.GetReference(builder.chars);
            _directCapacity = builder.chars.Length;

            _ownBuffer = null;
            _ownPos = 0;
        }

        /// <summary>Создает handler с провайдером форматов.</summary>
        [SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Required for interpolated string handler pattern")]
        public AppendInterpolatedStringHandler(int literalLength, int formattedCount, ValueStringBuilder builder, IFormatProvider? provider)
            : this(literalLength, formattedCount, builder) { }

        /// <summary>Добавляет литеральную строку.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AppendLiteral(string value)
        {
            if (string.IsNullOrEmpty(value)) return;

            // Overflow mode?
            if (_ownBuffer is not null)
            {
                AppendToOwnBuffer(value.AsSpan());
                return;
            }

            var len = value.Length;
            var newPos = _pos + len;

            // Хватает места в direct буфере?
            if (newPos <= _directCapacity)
            {
                ref var src = ref Unsafe.AsRef(in value.GetPinnableReference());
                ref var dst = ref Unsafe.Add(ref _directRef, _pos);
                Unsafe.CopyBlock(ref Unsafe.As<char, byte>(ref dst), ref Unsafe.As<char, byte>(ref src), (uint)(len * sizeof(char)));
                _pos = newPos;
            }
            else
            {
                // Переключаемся в overflow mode
                SwitchToOwnBuffer();
                AppendToOwnBuffer(value.AsSpan());
            }
        }

        /// <summary>Добавляет строку.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AppendFormatted(string? value)
        {
            if (value is null) return;

            // Overflow mode?
            if (_ownBuffer is not null)
            {
                AppendToOwnBuffer(value.AsSpan());
                return;
            }

            var len = value.Length;
            var newPos = _pos + len;

            if (newPos <= _directCapacity)
            {
                ref var src = ref Unsafe.AsRef(in value.GetPinnableReference());
                ref var dst = ref Unsafe.Add(ref _directRef, _pos);
                Unsafe.CopyBlock(ref Unsafe.As<char, byte>(ref dst), ref Unsafe.As<char, byte>(ref src), (uint)(len * sizeof(char)));
                _pos = newPos;
            }
            else
            {
                SwitchToOwnBuffer();
                AppendToOwnBuffer(value.AsSpan());
            }
        }

        /// <summary>Добавляет int.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AppendFormatted(int value)
        {
            // Overflow mode?
            if (_ownBuffer is not null)
            {
                AppendFormattedToOwn(value);
                return;
            }

            var remaining = _directCapacity - _pos;
            if (remaining >= 11) // int.MinValue = "-2147483648" = 11 chars max
            {
                var span = MemoryMarshal.CreateSpan(ref Unsafe.Add(ref _directRef, _pos), remaining);
                if (value.TryFormat(span, out var w))
                {
                    _pos += w;
                    return;
                }
            }

            SwitchToOwnBuffer();
            AppendFormattedToOwn(value);
        }

        /// <summary>Добавляет int с форматом.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AppendFormatted(int value, string? format)
        {
            if (_ownBuffer is not null)
            {
                AppendFormattedToOwn(value, format);
                return;
            }

            var remaining = _directCapacity - _pos;
            if (remaining >= 32)
            {
                var span = MemoryMarshal.CreateSpan(ref Unsafe.Add(ref _directRef, _pos), remaining);
                if (value.TryFormat(span, out var w, format))
                {
                    _pos += w;
                    return;
                }
            }

            SwitchToOwnBuffer();
            AppendFormattedToOwn(value, format);
        }

        /// <summary>Добавляет long.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AppendFormatted(long value)
        {
            if (_ownBuffer is not null)
            {
                AppendFormattedToOwn(value);
                return;
            }

            var remaining = _directCapacity - _pos;
            if (remaining >= 20) // long.MinValue = 20 chars max
            {
                var span = MemoryMarshal.CreateSpan(ref Unsafe.Add(ref _directRef, _pos), remaining);
                if (value.TryFormat(span, out var w))
                {
                    _pos += w;
                    return;
                }
            }

            SwitchToOwnBuffer();
            AppendFormattedToOwn(value);
        }

        /// <summary>Добавляет DateTime.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AppendFormatted(DateTime value)
        {
            if (_ownBuffer is not null)
            {
                AppendFormattedToOwn(value);
                return;
            }

            var remaining = _directCapacity - _pos;
            if (remaining >= 64)
            {
                var span = MemoryMarshal.CreateSpan(ref Unsafe.Add(ref _directRef, _pos), remaining);
                if (value.TryFormat(span, out var w))
                {
                    _pos += w;
                    return;
                }
            }

            SwitchToOwnBuffer();
            AppendFormattedToOwn(value);
        }

        /// <summary>Добавляет DateTime с форматом.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AppendFormatted(DateTime value, string? format)
        {
            if (_ownBuffer is not null)
            {
                AppendFormattedToOwn(value, format);
                return;
            }

            var remaining = _directCapacity - _pos;
            if (remaining >= 64)
            {
                var span = MemoryMarshal.CreateSpan(ref Unsafe.Add(ref _directRef, _pos), remaining);
                if (value.TryFormat(span, out var w, format))
                {
                    _pos += w;
                    return;
                }
            }

            SwitchToOwnBuffer();
            AppendFormattedToOwn(value, format);
        }

        /// <summary>Добавляет ISpanFormattable.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AppendFormatted<T>(T value) where T : ISpanFormattable
        {
            if (_ownBuffer is not null)
            {
                AppendFormattedToOwn(value);
                return;
            }

            var remaining = _directCapacity - _pos;
            if (remaining >= 64)
            {
                var span = MemoryMarshal.CreateSpan(ref Unsafe.Add(ref _directRef, _pos), remaining);
                if (value.TryFormat(span, out var w, format: default, provider: null))
                {
                    _pos += w;
                    return;
                }
            }

            SwitchToOwnBuffer();
            AppendFormattedToOwn(value);
        }

        /// <summary>Добавляет ISpanFormattable с форматом.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AppendFormatted<T>(T value, string? format) where T : ISpanFormattable
        {
            if (_ownBuffer is not null)
            {
                AppendFormattedToOwn(value, format);
                return;
            }

            var remaining = _directCapacity - _pos;
            if (remaining >= 64)
            {
                var span = MemoryMarshal.CreateSpan(ref Unsafe.Add(ref _directRef, _pos), remaining);
                if (value.TryFormat(span, out var w, format: format, provider: null))
                {
                    _pos += w;
                    return;
                }
            }

            SwitchToOwnBuffer();
            AppendFormattedToOwn(value, format);
        }

        /// <summary>Добавляет ISpanFormattable с выравниванием.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void AppendFormatted<T>(T value, int alignment) where T : ISpanFormattable
            => AppendFormattedAligned(value, alignment, format: default);

        /// <summary>Добавляет ISpanFormattable с форматом и выравниванием.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void AppendFormatted<T>(T value, int alignment, string? format) where T : ISpanFormattable
            => AppendFormattedAligned(value, alignment, format: format);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void AppendFormattedAligned<T>(T value, int alignment, string? format) where T : ISpanFormattable
        {
            Span<char> tmp = stackalloc char[64];
            if (!value.TryFormat(tmp, out var w, format: format, provider: null)) return;

            var abs = Math.Abs(alignment);
            var pad = abs - w;
            var totalLen = pad > 0 ? abs : w;

            // Проверяем, хватает ли места
            if (_ownBuffer is null)
            {
                var remaining = _directCapacity - _pos;
                if (remaining >= totalLen)
                {
                    var span = MemoryMarshal.CreateSpan(ref Unsafe.Add(ref _directRef, _pos), remaining);
                    if (pad <= 0)
                    {
                        tmp[..w].CopyTo(span);
                        _pos += w;
                    }
                    else if (alignment > 0)
                    {
                        span[..pad].Fill(' ');
                        tmp[..w].CopyTo(span[pad..]);
                        _pos += abs;
                    }
                    else
                    {
                        tmp[..w].CopyTo(span);
                        span.Slice(w, pad).Fill(' ');
                        _pos += abs;
                    }
                    return;
                }

                SwitchToOwnBuffer();
            }

            // Own buffer mode
            EnsureOwnCapacity(totalLen);
            var ownSpan = _ownBuffer.AsSpan(_ownPos);
            if (pad <= 0)
            {
                tmp[..w].CopyTo(ownSpan);
                _ownPos += w;
            }
            else if (alignment > 0)
            {
                ownSpan[..pad].Fill(' ');
                tmp[..w].CopyTo(ownSpan[pad..]);
                _ownPos += abs;
            }
            else
            {
                tmp[..w].CopyTo(ownSpan);
                ownSpan.Slice(w, pad).Fill(' ');
                _ownPos += abs;
            }
        }

        /// <summary>Добавляет object.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AppendFormatted(object? value)
        {
            if (value is ISpanFormattable sf)
                AppendFormatted(sf);
            else
                AppendFormatted(value?.ToString());
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        // OVERFLOW BUFFER MANAGEMENT
        // ═══════════════════════════════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void SwitchToOwnBuffer()
        {
            // Копируем уже записанное в direct буфер
            var written = _pos - _startPos;
            var initialSize = Math.Max(256, written * 2);

            _ownBuffer = ArrayPool<char>.Shared.Rent(initialSize);

            if (written > 0)
            {
                var src = MemoryMarshal.CreateSpan(ref Unsafe.Add(ref _directRef, _startPos), written);
                src.CopyTo(_ownBuffer);
            }

            _ownPos = written;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AppendToOwnBuffer(ReadOnlySpan<char> value)
        {
            EnsureOwnCapacity(value.Length);
            value.CopyTo(_ownBuffer.AsSpan(_ownPos));
            _ownPos += value.Length;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void AppendFormattedToOwn<T>(T value, string? format = null) where T : ISpanFormattable
        {
            // Попробуем отформатировать в текущий буфер
            var remaining = _ownBuffer!.Length - _ownPos;
            if (value.TryFormat(_ownBuffer.AsSpan(_ownPos, remaining), out var w, format: format, provider: null))
            {
                _ownPos += w;
                return;
            }

            // Нужно больше места - увеличиваем буфер
            GrowOwnBuffer(_ownPos + 128);
            if (value.TryFormat(_ownBuffer.AsSpan(_ownPos), out w, format: format, provider: null))
            {
                _ownPos += w;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureOwnCapacity(int additionalRequired)
        {
            if (_ownPos + additionalRequired > _ownBuffer!.Length)
                GrowOwnBuffer(_ownPos + additionalRequired);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void GrowOwnBuffer(int minCapacity)
        {
            var newSize = Math.Max(minCapacity, _ownBuffer!.Length * 2);
            var newBuffer = ArrayPool<char>.Shared.Rent(newSize);
            _ownBuffer.AsSpan(0, _ownPos).CopyTo(newBuffer);
            ArrayPool<char>.Shared.Return(_ownBuffer);
            _ownBuffer = newBuffer;
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        // FINALIZATION - вызывается методом Append
        // ═══════════════════════════════════════════════════════════════════════════════

        /// <summary>Возвращает данные для записи в builder.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal readonly bool TryGetDirectResult(out int newPosition)
        {
            if (_ownBuffer is null)
            {
                newPosition = _pos;
                return true;
            }

            newPosition = 0;
            return false;
        }

        /// <summary>Получает данные из overflow буфера и возвращает буфер в пул.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlySpan<char> GetOverflowDataAndReturn()
        {
            var data = _ownBuffer.AsSpan(0, _ownPos);
            // Буфер будет возвращён в пул после копирования
            return data;
        }

        /// <summary>Возвращает overflow буфер в пул.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void ReturnOwnBuffer()
        {
            if (_ownBuffer is not null)
            {
                ArrayPool<char>.Shared.Return(_ownBuffer);
            }
        }

        /// <summary>Проверяет, использовался ли overflow буфер.</summary>
        internal readonly bool HasOverflow => _ownBuffer is not null;

        /// <summary>Получает overflow буфер напрямую.</summary>
        internal readonly char[]? OwnBuffer => _ownBuffer;

        /// <summary>Получает позицию в overflow буфере.</summary>
        internal readonly int OwnPosition => _ownPos;

        /// <summary>Получает начальную позицию.</summary>
        internal readonly int StartPosition => _startPos;
    }
}

file static class CompositeTemplateCache
{
    // Упрощённый 2-level cache БЕЗ ConcurrentDictionary
    // Thread-local micro cache (4 slots) - основной кеш для single-threaded сценариев
    // Ring buffer (16 slots) - fallback для многопоточности, lock-free

    // Lock-free ring cache (16 slots)
    private const int RingSize = 16;
    private const int RingMask = RingSize - 1;
    private static readonly string?[] ringFormats = new string?[RingSize];
    private static readonly CompositeTemplate[] ringTemplates = new CompositeTemplate[RingSize];
    private static int ringIndex;

    // Thread-local micro cache (4 slots)
    [ThreadStatic]
    private static string?[]? tlFormats;
    [ThreadStatic]
    private static CompositeTemplate[]? tlTemplates;
    [ThreadStatic]
    private static int tlIndex;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static CompositeTemplate? TryGetFromThreadLocal(string format)
    {
        var formats = tlFormats;
        if (formats is null) return null;

        for (var i = 0; i < 4; i++)
        {
            if (ReferenceEquals(formats[i], format))
                return tlTemplates![i];
        }
        return null;
    }

    internal static CompositeTemplate GetOrAdd(string format)
    {
        // Ultra-fast path: thread-local micro cache (4 slots)
        var cached = TryGetFromThreadLocal(format);
        if (cached.HasValue) return cached.Value;

        // Fast path: lock-free ring cache (16 slots)
        for (var i = 0; i < RingSize; i++)
        {
            if (string.Equals(format, ringFormats[i], StringComparison.Ordinal))
            {
                var template = ringTemplates[i];
                UpdateThreadLocalCache(format, template);
                return template;
            }
        }

        // Cache miss: parse immediately (cheaper than ConcurrentDictionary)
        var newTemplate = Parse(format);

        // Update ring cache (lock-free circular buffer)
        var index = Interlocked.Increment(ref ringIndex) & RingMask;
        ringFormats[index] = format;
        ringTemplates[index] = newTemplate;

        UpdateThreadLocalCache(format, newTemplate);

        return newTemplate;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void UpdateThreadLocalCache(string format, CompositeTemplate template)
    {
        var formats = tlFormats;
        if (formats is null)
        {
            formats = tlFormats = new string?[4];
            tlTemplates = new CompositeTemplate[4];
        }

        var idx = tlIndex;
        formats[idx] = format;
        tlTemplates![idx] = template;
        tlIndex = (idx + 1) & 3;
    }

    private static CompositeTemplate Parse(string format)
    {
#if DEBUG
        ArgumentNullException.ThrowIfNull(format);
#endif
        var rented = ArrayPool<CompositeSegment>.Shared.Rent(Math.Max(16, (format.Length / 2) + 1));
        var count = 0;

        void Add(CompositeSegment segment)
        {
            if ((uint)count >= (uint)rented.Length)
            {
                var newBuffer = ArrayPool<CompositeSegment>.Shared.Rent(rented.Length * 2);
                rented.AsSpan(0, count).CopyTo(newBuffer);
                ArrayPool<CompositeSegment>.Shared.Return(rented, clearArray: true);
                rented = newBuffer;
            }

            rented[count++] = segment;
        }

        var length = format.Length;
        var pos = 0;

        while (true)
        {
            var span = format.AsSpan(pos);
            var braceOffset = span.IndexOfAny('{', '}');
            if (braceOffset < 0)
            {
                if (pos < length) Add(CompositeSegment.FromLiteral(format[pos..]));
                break;
            }

            var bracePos = pos + braceOffset;
            if (bracePos > pos)
            {
                Add(CompositeSegment.FromLiteral(format[pos..bracePos]));
            }

            var braceChar = format[bracePos];
            var nextPos = bracePos + 1;

            if (nextPos < length && format[nextPos] == braceChar)
            {
                Add(CompositeSegment.FromLiteral(braceChar.ToString()));
                pos = bracePos + 2;
                continue;
            }

            if (braceChar == '}') ThrowFormatLocal();

            pos = bracePos;
            Add(ParsePlaceholder(format, ref pos));
        }

        var result = new CompositeSegment[count];
        rented.AsSpan(0, count).CopyTo(result);
        ArrayPool<CompositeSegment>.Shared.Return(rented, clearArray: true);

        return new CompositeTemplate(result);
    }

    private static CompositeSegment ParsePlaceholder(string format, ref int pos)
    {
        var index = ParseIndex(format, ref pos);
        var alignment = ParseAlignmentValue(format, ref pos);
        var formatString = ParseFormatString(format, ref pos);

        if (pos >= format.Length || format[pos] != '}') ThrowFormatLocal();
        pos++;

        return CompositeSegment.FromPlaceholder(index, alignment, formatString);
    }

    private static void SkipSpaces(string format, ref int pos)
    {
        while (pos < format.Length && format[pos] == ' ') pos++;
    }

    private static int ParseIndex(string format, ref int pos)
    {
        pos++;
        if (pos >= format.Length || !char.IsDigit(format[pos])) ThrowFormatLocal();

        var index = 0;
        while (pos < format.Length && char.IsDigit(format[pos]))
        {
            index = (index * 10) + (format[pos] - '0');
            pos++;
        }

        SkipSpaces(format, ref pos);
        return index;
    }

    private static int ParseAlignmentValue(string format, ref int pos)
    {
        if (pos >= format.Length || format[pos] != ',') return 0;

        pos++;
        SkipSpaces(format, ref pos);

        var negative = false;
        if (pos < format.Length && format[pos] == '-')
        {
            negative = true;
            pos++;
        }

        var value = 0;
        while (pos < format.Length && char.IsDigit(format[pos]))
        {
            value = (value * 10) + (format[pos] - '0');
            pos++;
        }

        SkipSpaces(format, ref pos);
        return negative ? -value : value;
    }

    private static string? ParseFormatString(string format, ref int pos)
    {
        if (pos >= format.Length || format[pos] != ':') return null;

        pos++;
        var start = pos;

        while (pos < format.Length && format[pos] != '}')
        {
            if (format[pos] == '{') ThrowFormatLocal();
            pos++;
        }

        if (pos > format.Length) ThrowFormatLocal();
        return format[start..pos];
    }

    [DoesNotReturn]
    private static void ThrowFormatLocal() => throw new FormatException("Неверная строка формата");
}

file readonly struct CompositeTemplate
{
    internal CompositeTemplate(CompositeSegment[] segments) => Segments = segments;
    internal CompositeSegment[] Segments { get; }
}

file readonly struct CompositeSegment
{
    // Union-like structure: either Literal OR (Index, Alignment, Format)
    // When IsLiteral=true, _data holds string reference (literal)
    // When IsLiteral=false, _data is unused, Index/Alignment/Format are used
    private readonly object? _data; // string for literals, null for placeholders
    private readonly short _index;

    private CompositeSegment(string literal)
    {
        _data = literal;
        _index = 0;
        Alignment = 0;
    }

    private CompositeSegment(short index, short alignment, string? format)
    {
        _data = format; // Reuse _data for format string in placeholders
        _index = (short)(-index - 1); // Negative index signals placeholder (allows 0 index)
        Alignment = alignment;
    }

    internal static CompositeSegment FromLiteral(string value) => new(value);

    internal static CompositeSegment FromPlaceholder(int index, int alignment, string? format) =>
        new((short)index, (short)alignment, format);

    internal bool IsLiteral => _index >= 0 || (_index == 0 && _data is string s && s.Length > 0);
    internal string Literal => (_data as string) ?? string.Empty;
    internal short Index => _index < 0 ? (short)(-_index - 1) : (short)0;
    internal short Alignment { get; }
    internal string? Format => IsLiteral ? null : _data as string;
}