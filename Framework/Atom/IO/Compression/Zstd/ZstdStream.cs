#pragma warning disable CA2215

using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using Atom.IO.Compression.Zstd;

namespace Atom.IO.Compression;

/// <summary>
/// Потоковая обёртка Zstandard. В режиме декодирования читает Zstd-кадры и выдаёт несжатые данные.
/// В режиме кодирования принимает несжатые данные и пишет корректные Zstd-кадры (RAW/RLE).
/// </summary>
/// <remarks>
/// Поддерживаются: skippable-кадры, FrameHeader (SingleSegment/Checksum/NoDict), блоки RAW/RLE, ContentChecksum (xxHash32).
/// </remarks>
/// <remarks>
/// Создаёт поток Zstd в явном режиме с контролем владения базовым потоком.
/// </remarks>
/// <param name="stream">Базовый поток ввода/вывода.</param>
/// <param name="mode">Режим работы.</param>
/// <param name="leaveOpen">Не закрывать базовый поток при закрытии <see cref="ZstdStream"/>.</param>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public sealed class ZstdStream(System.IO.Stream stream, CompressionMode mode, bool leaveOpen) : Stream
{
    /// <summary>
    /// Магическое число Zstd-кадра (LE): 0xFD2FB528.
    /// </summary>
    internal const uint FrameMagic = 0xFD2FB528u;

    /// <summary>
    /// Skippable-маска магических чисел: 0x184D2A50..0x184D2A5F.
    /// </summary>
    internal const uint SkippableBase = 0x184D2A50u;

    /// <summary>
    /// Максимальный допустимый размер окна (sanity-check).
    /// </summary>
    internal const int MaxWindowSize = 1 << 27; // 128 МБ

    /// <summary>
    /// Максимальный размер RAW-блока при записи.
    /// </summary>
    internal const int MaxRawBlockSize = 128 * 1024; // 128 КБ (RFC 8878 §3.1.1.2.4)

    private readonly System.IO.Stream baseStream = stream;
    private readonly bool leaveOpen = leaveOpen;
    private readonly CompressionMode mode = mode;

    // Внутренние движки (создаются лениво).
    private ZstdDecoder? decoder;
    private ZstdEncoder? encoder;

    private bool isDisposed;

    /// <summary>
    /// Уровень сжатия, используемый в режиме кодирования.
    /// </summary>
    public int CompressionLevel { get; init; }

    /// <summary>
    /// Включить Content Checksum (XXH64 low-4 LE) в конце кадра.
    /// </summary>
    public bool IsContentChecksumEnabled { get; init; }

    /// <summary>
    /// Признак Single Segment. При true WD не пишется, а FCS обязателен.
    /// По умолчанию false (стриминговая модель).
    /// </summary>
    public bool IsSingleSegment { get; init; }

    /// <summary>
    /// Размер окна (Window_Size) для WD. По умолчанию 8 MiB — безопасный максимум для HTTP‑совместимости.
    /// </summary>
    public int WindowSize { get; init; } = 8 * 1024 * 1024; // см. RFC 8878 §3.1.1.1.2 и RFC 9659

    /// <summary>
    /// Необязательный DictionaryId (DID). Если null — поле отсутствует.
    /// </summary>
    public uint? DictionaryId { get; init; }

    /// <summary>
    /// Необязательный Frame_Content_Size. Если null и SS=false — FCS отсутствует. Если SS=true и null, будет ошибка.
    /// </summary>
    public ulong? FrameContentSize { get; init; }

    /// <inheritdoc />
    public override bool CanRead
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => !Volatile.Read(ref isDisposed) && mode is CompressionMode.Decompress && baseStream.CanRead;
    }

    /// <inheritdoc />
    public override bool CanWrite
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => !Volatile.Read(ref isDisposed) && mode is CompressionMode.Compress && baseStream.CanWrite;
    }

    /// <summary>
    /// Создаёт поток Zstd в явном режиме <see cref="CompressionMode"/>.
    /// </summary>
    /// <param name="stream">Базовый поток ввода/вывода.</param>
    /// <param name="mode">Режим работы: <see cref="CompressionMode.Decompress"/> или <see cref="CompressionMode.Compress"/>.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ZstdStream(System.IO.Stream stream, CompressionMode mode) : this(stream, mode, default) { }

    /// <summary>
    /// Создаёт поток Zstd для <b>кодирования</b> с уровнем сжатия и контролем владения базовым потоком.
    /// </summary>
    /// <param name="stream">Базовый поток для вывода Zstd-данных.</param>
    /// <param name="compressionLevel">Уровень сжатия (используется только в режиме кодирования).</param>
    /// <param name="leaveOpen">Не закрывать базовый поток при закрытии <see cref="ZstdStream"/>.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ZstdStream(System.IO.Stream stream, int compressionLevel, bool leaveOpen)
        : this(stream, CompressionMode.Compress, leaveOpen) => CompressionLevel = compressionLevel;

    /// <summary>
    /// Создаёт поток Zstd для <b>кодирования</b> с уровнем сжатия и контролем владения базовым потоком.
    /// </summary>
    /// <param name="stream">Базовый поток для вывода Zstd-данных.</param>
    /// <param name="compressionLevel">Уровень сжатия (используется только в режиме кодирования).</param>
    /// <param name="leaveOpen">Не закрывать базовый поток при закрытии <see cref="ZstdStream"/>.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ZstdStream(System.IO.Stream stream, CompressionLevel compressionLevel, bool leaveOpen)
        : this(stream, GetCompressionLevel(compressionLevel), leaveOpen) { }

    /// <summary>
    /// Создаёт поток Zstd для <b>кодирования</b> с заданным уровнем сжатия.
    /// </summary>
    /// <param name="stream">Базовый поток для вывода Zstd-данных.</param>
    /// <param name="compressionLevel">Уровень сжатия (используется только в режиме кодирования).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ZstdStream(System.IO.Stream stream, int compressionLevel) : this(stream, compressionLevel, default) { }

    /// <summary>
    /// Создаёт поток Zstd для <b>кодирования</b> с заданным уровнем сжатия.
    /// </summary>
    /// <param name="stream">Базовый поток для вывода Zstd-данных.</param>
    /// <param name="compressionLevel">Уровень сжатия (используется только в режиме кодирования).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ZstdStream(System.IO.Stream stream, CompressionLevel compressionLevel) : this(stream, compressionLevel, default) { }

    /// <summary>
    /// Создаёт поток Zstd, принимая <see cref="ZLibCompressionOptions"/> (учитывается только <c>Level</c>).
    /// </summary>
    /// <remarks>
    /// Zstd не использует параметры zlib (WindowBits/Strategy/WrapperType и т.д.) — они игнорируются.
    /// Берётся только <see cref="ZLibCompressionOptions.CompressionLevel"/> для унификации с BCL.
    /// </remarks>
    /// <param name="stream">Базовый поток ввода/вывода.</param>
    /// <param name="compressionOptions">Опции zlib (будет взят только <c>Level</c>).</param>
    /// <param name="leaveOpen">Не закрывать базовый поток при закрытии <see cref="ZstdStream"/>.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ZstdStream(System.IO.Stream stream, [NotNull] ZLibCompressionOptions compressionOptions, bool leaveOpen = false) : this(stream, compressionOptions.CompressionLevel, leaveOpen) { }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (Interlocked.CompareExchange(ref isDisposed, true, default) || !disposing) return;

        encoder?.Dispose();
        decoder?.Dispose();

        if (!leaveOpen) baseStream.Dispose();
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int Read(Span<byte> buffer)
    {
        if (mode is not CompressionMode.Decompress) throw new NotSupportedException("Поток открыт в режиме кодирования");

        decoder ??= new ZstdDecoder(baseStream);
        return decoder.Read(buffer);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (mode is not CompressionMode.Decompress) throw new NotSupportedException("Поток открыт в режиме кодирования");

        decoder ??= new ZstdDecoder(baseStream);
        return await decoder.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (mode is not CompressionMode.Compress) throw new NotSupportedException("Поток открыт в режиме декодирования");

        encoder ??= new ZstdEncoder(baseStream, new ZstdEncoderSettings
        {
            IsContentChecksumEnabled = IsContentChecksumEnabled,
            IsSingleSegment = IsSingleSegment,
            WindowSize = WindowSize,
            DictionaryId = DictionaryId,
            FrameContentSize = FrameContentSize,
            BlockCap = Math.Min(WindowSize, MaxRawBlockSize),
            CompressionLevel = CompressionLevel,
        });

        encoder.Write(buffer);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (mode is not CompressionMode.Compress) throw new NotSupportedException("Поток открыт в режиме декодирования");

        encoder ??= new ZstdEncoder(baseStream, new ZstdEncoderSettings
        {
            IsContentChecksumEnabled = IsContentChecksumEnabled,
            IsSingleSegment = IsSingleSegment,
            WindowSize = WindowSize,
            DictionaryId = DictionaryId,
            FrameContentSize = FrameContentSize,
            BlockCap = Math.Min(WindowSize, MaxRawBlockSize),
            CompressionLevel = CompressionLevel,
        });

        await encoder.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Flush() => baseStream.Flush();

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override async Task FlushAsync(CancellationToken cancellationToken) => await baseStream.FlushAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref isDisposed, true, default)) return;

        encoder?.Dispose();
        decoder?.Dispose();

        if (!leaveOpen) await baseStream.DisposeAsync().ConfigureAwait(false);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetCompressionLevel(CompressionLevel compressionLevel) => compressionLevel switch
    {
        System.IO.Compression.CompressionLevel.NoCompression => default,
        System.IO.Compression.CompressionLevel.Fastest => 1,
        System.IO.Compression.CompressionLevel.SmallestSize => 19,
        _ => 5,
    };
}