#pragma warning disable MA0011, MA0074, MA0102

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Atom.Text;

namespace Atom.Net.Https;

/// <summary>
/// Представляет данные о трафике.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public struct Traffic : IEquatable<Traffic>
{
    private static readonly string[] suffixes = ["Б", "КБ", "МБ", "ГБ", "ТБ"];
    private ulong sended;
    private ulong received;

    /// <summary>
    /// Количество отправленных байт.
    /// </summary>
    public ulong Sended => Volatile.Read(ref sended);

    /// <summary>
    /// Количество полученных байт.
    /// </summary>
    public ulong Received => Volatile.Read(ref received);

    /// <summary>
    /// Общее количество отправленных и полученных байт.
    /// </summary>
    public ulong Total => Received + Sended;

    /// <summary>
    /// Добавляет трафик.
    /// </summary>
    /// <param name="sended">Количество отправленных байт.</param>
    /// <param name="received">Количество полученных байт.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(ulong sended, ulong received)
    {
        if (sended > 0) Interlocked.Add(ref this.sended, sended);
        if (received > 0) Interlocked.Add(ref this.received, received);
    }

    /// <summary>
    /// Добавляет трафик.
    /// </summary>
    /// <param name="other">Данные о трафике.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(Traffic other) => Add(Volatile.Read(ref other.sended), Volatile.Read(ref other.received));

    /// <summary>
    /// Сбрасывает данные о трафике.
    /// </summary>
    /// <param name="sended">Указывает, требуется ли сбросить количество отправленных байт.</param>
    /// <param name="received">Указывает, требуется ли сбросить количество полученных байт.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset(bool sended, bool received)
    {
        if (sended) Interlocked.Exchange(ref this.sended, default);
        if (received) Interlocked.Exchange(ref this.received, default);
    }

    /// <summary>
    /// Сбрасывает данные о трафике.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset() => Reset(sended: true, received: true);

    /// <summary>
    /// Получает хэш-код текущего экземпляра.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => HashCode.Combine(Volatile.Read(ref sended).GetHashCode(), Volatile.Read(ref received).GetHashCode());

    /// <summary>
    /// Сравнивает текущий экземпляр с другим.
    /// </summary>
    /// <param name="other">Другой экземпляр.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(Traffic other) => Volatile.Read(ref sended) == Volatile.Read(ref other.sended) && Volatile.Read(ref received) == Volatile.Read(ref other.received);

    /// <summary>
    /// Сравнивает текущий экземпляр с другим значением.
    /// </summary>
    /// <param name="obj">Другой экземпляр.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj switch
    {
        Traffic other => Equals(other),
        _ => default,
    };

    /// <summary>
    /// Преобразует данные о трафике в строковое представление.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override string ToString()
    {
        var input = Volatile.Read(ref sended);
        var output = Volatile.Read(ref received);

        if (input is 0 && output is 0) return string.Empty;

        using var sb = new ValueStringBuilder();

        if (input > 0) sb.Append("↑ ").Append(Format(input));

        if (output > 0)
        {
            if (input > 0) sb.Append(' ');
            sb.Append("↓ ").Append(Format(output));
        }

        return sb.ToString();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string Format(double bytes)
    {
        var order = 0;

        while (bytes >= 1024 && order < suffixes.Length - 1)
        {
            bytes /= 1024;
            ++order;
        }

        bytes = Math.Round(bytes, 2);

        var value = bytes.ToString("0.00");
        if (value.EndsWith(".00")) value = value[..^3];

        return $"{value} {suffixes[order]}";
    }

    /// <summary>
    /// Сравнивает два значения данных о трафике.
    /// </summary>
    /// <param name="left">Значение слева.</param>
    /// <param name="right">Значение справа.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Traffic left, Traffic right) => left.Equals(right);

    /// <summary>
    /// Сравнивает два значения данных о трафике.
    /// </summary>
    /// <param name="left">Значение слева.</param>
    /// <param name="right">Значение справа.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Traffic left, Traffic right) => !(left == right);
}

#pragma warning restore MA0011, MA0074, MA0102
