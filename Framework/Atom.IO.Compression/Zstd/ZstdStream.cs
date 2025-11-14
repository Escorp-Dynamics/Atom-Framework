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
/// Поддерживаются: skippable-кадры, FrameHeader (SingleSegment/Checksum/NoDict), блоки RAW/RLE, ContentChecksum (xxHash64 low-4 LE).
/// </remarks>
/// <remarks>
/// Создаёт поток Zstd в явном режиме с контролем владения базовым потоком.
/// </remarks>
/// <param name="stream">Базовый поток ввода/вывода.</param>
/// <param name="mode">Режим работы.</param>
/// <param name="leaveOpen">Не закрывать базовый поток при закрытии <see cref="ZstdStream"/>.</param>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public sealed class ZstdStream(System.IO.Stream stream, CompressionMode mode, bool leaveOpen) : System.IO.Stream
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
    /// Необязательный провайдер словарей: при наличии DID в кадре пытается предоставить байты словаря.
    /// Поддерживаются raw-content и форматированные словари Zstandard.
    /// </summary>
    public IZstdDictionaryProvider? DictionaryProvider { get; init; }

    /// <summary>
    /// Использовать FSE-таблицы из словаря для кодирования последовательностей (LL/ML/OF).
    /// </summary>
    public bool UseDictionaryTablesForSequences { get; init; } = true;

    /// <summary>
    /// Использовать Huffman-таблицу словаря для кодирования литералов (treeless, 1/4 потока).
    /// </summary>
    public bool UseDictionaryHuffmanForLiterals { get; init; } = true;

    /// <summary>
    /// Использовать межблочное окно истории (до <see cref="WindowSize"/>) для улучшения матчей в кодере.
    /// </summary>
    public bool UseInterBlockHistory { get; init; } = true;

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

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
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
        var alreadyDisposed = Interlocked.Exchange(ref isDisposed, value: true);
        if (alreadyDisposed)
        {
            base.Dispose(disposing);
            return;
        }

        if (disposing)
        {
            encoder?.Dispose();
            decoder?.Dispose();

            if (!leaveOpen) baseStream.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int Read(Span<byte> buffer)
    {
        if (mode is not CompressionMode.Decompress) throw new NotSupportedException("Поток открыт в режиме кодирования");

        decoder ??= new ZstdDecoder(baseStream, DictionaryProvider);
        return decoder.Read(buffer);
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (buffer.Length - offset < count) throw new ArgumentException("Недостаточно места в буфере", nameof(buffer));

        return Read(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (mode is not CompressionMode.Decompress) throw new NotSupportedException("Поток открыт в режиме кодирования");

        decoder ??= new ZstdDecoder(baseStream, DictionaryProvider);
        return decoder.ReadAsync(buffer, cancellationToken);
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
            DictionaryContent = (DictionaryId.HasValue && DictionaryProvider is not null && DictionaryProvider.TryGet(DictionaryId.Value, out var __dict)) ? __dict : ReadOnlyMemory<byte>.Empty,
            UseDictTablesForSequences = UseDictionaryTablesForSequences,
            UseDictHuffmanForLiterals = UseDictionaryHuffmanForLiterals,
            UseInterBlockHistory = UseInterBlockHistory,
            FrameContentSize = FrameContentSize,
            BlockCap = Math.Min(WindowSize, MaxRawBlockSize),
            CompressionLevel = CompressionLevel,
            IsCompressedBlocksEnabled = true,
        });

        encoder.Write(buffer);
    }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (buffer.Length - offset < count) throw new ArgumentException("Недостаточно места в буфере", nameof(buffer));

        Write(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (mode is not CompressionMode.Compress) throw new NotSupportedException("Поток открыт в режиме декодирования");

        encoder ??= new ZstdEncoder(baseStream, new ZstdEncoderSettings
        {
            IsContentChecksumEnabled = IsContentChecksumEnabled,
            IsSingleSegment = IsSingleSegment,
            WindowSize = WindowSize,
            DictionaryId = DictionaryId,
            DictionaryContent = (DictionaryId.HasValue && DictionaryProvider is not null && DictionaryProvider.TryGet(DictionaryId.Value, out var __dict)) ? __dict : ReadOnlyMemory<byte>.Empty,
            UseDictTablesForSequences = UseDictionaryTablesForSequences,
            UseDictHuffmanForLiterals = UseDictionaryHuffmanForLiterals,
            UseInterBlockHistory = UseInterBlockHistory,
            FrameContentSize = FrameContentSize,
            BlockCap = Math.Min(WindowSize, MaxRawBlockSize),
            CompressionLevel = CompressionLevel,
            IsCompressedBlocksEnabled = true,
        });

        return encoder.WriteAsync(buffer, cancellationToken);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Flush() => baseStream.Flush();

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override Task FlushAsync(CancellationToken cancellationToken) => baseStream.FlushAsync(cancellationToken);

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override async ValueTask DisposeAsync()
    {
        var alreadyDisposed = Interlocked.Exchange(ref isDisposed, value: true);
        if (alreadyDisposed)
        {
            await base.DisposeAsync().ConfigureAwait(false);
            return;
        }

        encoder?.Dispose();
        decoder?.Dispose();

        if (!leaveOpen) await baseStream.DisposeAsync().ConfigureAwait(false);

        await base.DisposeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetCompressionLevel(CompressionLevel compressionLevel) => compressionLevel switch
    {
        System.IO.Compression.CompressionLevel.NoCompression => default,
        System.IO.Compression.CompressionLevel.Fastest => 1,
        System.IO.Compression.CompressionLevel.SmallestSize => 7,
        _ => 3,
    };
}
