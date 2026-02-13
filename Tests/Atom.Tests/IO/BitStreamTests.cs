namespace Atom.IO.Tests;

/// <summary>
/// Тесты BitReader и BitWriter.
/// </summary>
public sealed class BitStreamTests(ILogger logger) : BenchmarkTests<BitStreamTests>(logger)
{
    public BitStreamTests() : this(ConsoleLogger.Unicode) { }

    #region BitWriter MSB Tests

    [TestCase(TestName = "BitWriter MSB: запись отдельных бит")]
    public void BitWriterMsbWriteSingleBits()
    {
        Span<byte> buffer = stackalloc byte[4];
        var writer = new BitWriter(buffer, lsbFirst: false);

        // 0b10110100 = 0xB4
        writer.WriteBit(true);   // 1
        writer.WriteBit(false);  // 0
        writer.WriteBit(true);   // 1
        writer.WriteBit(true);   // 1
        writer.WriteBit(false);  // 0
        writer.WriteBit(true);   // 1
        writer.WriteBit(false);  // 0
        writer.WriteBit(false);  // 0

        var result = writer.GetWrittenSpan();
        Assert.That(result.Length, Is.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(0xB4));
    }

    [TestCase(TestName = "BitWriter MSB: запись нескольких бит")]
    public void BitWriterMsbWriteMultipleBits()
    {
        Span<byte> buffer = stackalloc byte[4];
        var writer = new BitWriter(buffer, lsbFirst: false);

        // 4 бит: 0b1011 = 11
        writer.WriteBits(0b1011, 4);
        // 4 бит: 0b0100 = 4
        writer.WriteBits(0b0100, 4);

        var result = writer.GetWrittenSpan();
        Assert.That(result.Length, Is.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(0xB4)); // 0b10110100
    }

    [TestCase(TestName = "BitWriter MSB: запись полных байт")]
    public void BitWriterMsbWriteBytes()
    {
        Span<byte> buffer = stackalloc byte[4];
        var writer = new BitWriter(buffer, lsbFirst: false);

        writer.WriteBits(0xAB, 8);
        writer.WriteBits(0xCD, 8);

        var result = writer.GetWrittenSpan();
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0], Is.EqualTo(0xAB));
        Assert.That(result[1], Is.EqualTo(0xCD));
    }

    [TestCase(TestName = "BitWriter MSB: запись 32 бит")]
    public void BitWriterMsbWrite32Bits()
    {
        Span<byte> buffer = stackalloc byte[8];
        var writer = new BitWriter(buffer, lsbFirst: false);

        writer.WriteBits(0xDEADBEEF, 32);

        var result = writer.GetWrittenSpan();
        Assert.That(result.Length, Is.EqualTo(4));
        Assert.That(result[0], Is.EqualTo(0xDE));
        Assert.That(result[1], Is.EqualTo(0xAD));
        Assert.That(result[2], Is.EqualTo(0xBE));
        Assert.That(result[3], Is.EqualTo(0xEF));
    }

    #endregion

    #region BitWriter LSB Tests

    [TestCase(TestName = "BitWriter LSB: запись отдельных бит")]
    public void BitWriterLsbWriteSingleBits()
    {
        Span<byte> buffer = stackalloc byte[4];
        var writer = new BitWriter(buffer, lsbFirst: true);

        // LSB first: биты записываются справа налево в байте
        // 0b00101101 = 0x2D
        writer.WriteBit(true);   // bit 0
        writer.WriteBit(false);  // bit 1
        writer.WriteBit(true);   // bit 2
        writer.WriteBit(true);   // bit 3
        writer.WriteBit(false);  // bit 4
        writer.WriteBit(true);   // bit 5
        writer.WriteBit(false);  // bit 6
        writer.WriteBit(false);  // bit 7

        var result = writer.GetWrittenSpan();
        Assert.That(result.Length, Is.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(0x2D)); // 0b00101101
    }

    [TestCase(TestName = "BitWriter LSB: запись нескольких бит")]
    public void BitWriterLsbWriteMultipleBits()
    {
        Span<byte> buffer = stackalloc byte[4];
        var writer = new BitWriter(buffer, lsbFirst: true);

        // LSB: 4 бит: 0b1101 = 13, затем 0b0010 = 2
        // Результат: 0b00101101 = 0x2D
        writer.WriteBits(0b1101, 4);
        writer.WriteBits(0b0010, 4);

        var result = writer.GetWrittenSpan();
        Assert.That(result.Length, Is.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(0x2D));
    }

    #endregion

    #region BitReader MSB Tests

    [TestCase(TestName = "BitReader MSB: чтение отдельных бит")]
    public void BitReaderMsbReadSingleBits()
    {
        ReadOnlySpan<byte> data = [0xB4]; // 0b10110100
        var reader = new BitReader(data, lsbFirst: false);

        Assert.That(reader.ReadBit(), Is.True);   // 1
        Assert.That(reader.ReadBit(), Is.False);  // 0
        Assert.That(reader.ReadBit(), Is.True);   // 1
        Assert.That(reader.ReadBit(), Is.True);   // 1
        Assert.That(reader.ReadBit(), Is.False);  // 0
        Assert.That(reader.ReadBit(), Is.True);   // 1
        Assert.That(reader.ReadBit(), Is.False);  // 0
        Assert.That(reader.ReadBit(), Is.False);  // 0
    }

    [TestCase(TestName = "BitReader MSB: чтение нескольких бит")]
    public void BitReaderMsbReadMultipleBits()
    {
        ReadOnlySpan<byte> data = [0xB4]; // 0b10110100
        var reader = new BitReader(data, lsbFirst: false);

        Assert.That(reader.ReadBits(4), Is.EqualTo(0b1011U));
        Assert.That(reader.ReadBits(4), Is.EqualTo(0b0100U));
    }

    [TestCase(TestName = "BitReader MSB: чтение 32 бит")]
    public void BitReaderMsbRead32Bits()
    {
        ReadOnlySpan<byte> data = [0xDE, 0xAD, 0xBE, 0xEF];
        var reader = new BitReader(data, lsbFirst: false);

        Assert.That(reader.ReadBits(32), Is.EqualTo(0xDEADBEEFU));
    }

    #endregion

    #region BitReader LSB Tests

    [TestCase(TestName = "BitReader LSB: чтение отдельных бит")]
    public void BitReaderLsbReadSingleBits()
    {
        ReadOnlySpan<byte> data = [0x2D]; // 0b00101101
        var reader = new BitReader(data, lsbFirst: true);

        Assert.That(reader.ReadBit(), Is.True);   // bit 0
        Assert.That(reader.ReadBit(), Is.False);  // bit 1
        Assert.That(reader.ReadBit(), Is.True);   // bit 2
        Assert.That(reader.ReadBit(), Is.True);   // bit 3
        Assert.That(reader.ReadBit(), Is.False);  // bit 4
        Assert.That(reader.ReadBit(), Is.True);   // bit 5
        Assert.That(reader.ReadBit(), Is.False);  // bit 6
        Assert.That(reader.ReadBit(), Is.False);  // bit 7
    }

    [TestCase(TestName = "BitReader LSB: чтение нескольких бит")]
    public void BitReaderLsbReadMultipleBits()
    {
        ReadOnlySpan<byte> data = [0x2D]; // 0b00101101
        var reader = new BitReader(data, lsbFirst: true);

        // LSB: младшие 4 бита = 0b1101 = 13
        // Следующие 4 бита = 0b0010 = 2
        Assert.That(reader.ReadBits(4), Is.EqualTo(0b1101U));
        Assert.That(reader.ReadBits(4), Is.EqualTo(0b0010U));
    }

    #endregion

    #region Round-Trip Tests

    [TestCase(TestName = "BitStream: round-trip MSB 1-32 бит")]
    public void RoundTripMsbVariousBitCounts()
    {
        Span<byte> buffer = stackalloc byte[8];

        for (var bits = 1; bits <= 32; bits++)
        {
            var maxValue = bits == 32 ? uint.MaxValue : (1U << bits) - 1;
            var testValue = maxValue / 2;

            buffer.Clear();
            var writer = new BitWriter(buffer, lsbFirst: false);
            writer.WriteBits(testValue, bits);
            var written = writer.GetWrittenSpan();

            var reader = new BitReader(written.ToArray(), lsbFirst: false);
            var readValue = reader.ReadBits(bits);

            Assert.That(readValue, Is.EqualTo(testValue), $"Mismatch for {bits} bits");
        }
    }

    [TestCase(TestName = "BitStream: round-trip LSB 1-32 бит")]
    public void RoundTripLsbVariousBitCounts()
    {
        Span<byte> buffer = stackalloc byte[8];

        for (var bits = 1; bits <= 32; bits++)
        {
            var maxValue = bits == 32 ? uint.MaxValue : (1U << bits) - 1;
            var testValue = maxValue / 2;

            buffer.Clear();
            var writer = new BitWriter(buffer, lsbFirst: true);
            writer.WriteBits(testValue, bits);
            var written = writer.GetWrittenSpan();

            var reader = new BitReader(written.ToArray(), lsbFirst: true);
            var readValue = reader.ReadBits(bits);

            Assert.That(readValue, Is.EqualTo(testValue), $"Mismatch for {bits} bits");
        }
    }

    [TestCase(TestName = "BitStream: round-trip смешанные размеры MSB")]
    public void RoundTripMsbMixedSizes()
    {
        Span<byte> buffer = stackalloc byte[16];
        var writer = new BitWriter(buffer, lsbFirst: false);

        writer.WriteBits(0b1, 1);
        writer.WriteBits(0b10, 2);
        writer.WriteBits(0b101, 3);
        writer.WriteBits(0b1010, 4);
        writer.WriteBits(0xAB, 8);
        writer.WriteBits(0xCDEF, 16);

        var written = writer.GetWrittenSpan();
        var reader = new BitReader(written.ToArray(), lsbFirst: false);

        Assert.That(reader.ReadBits(1), Is.EqualTo(0b1U));
        Assert.That(reader.ReadBits(2), Is.EqualTo(0b10U));
        Assert.That(reader.ReadBits(3), Is.EqualTo(0b101U));
        Assert.That(reader.ReadBits(4), Is.EqualTo(0b1010U));
        Assert.That(reader.ReadBits(8), Is.EqualTo(0xABU));
        Assert.That(reader.ReadBits(16), Is.EqualTo(0xCDEFU));
    }

    [TestCase(TestName = "BitStream: round-trip смешанные размеры LSB")]
    public void RoundTripLsbMixedSizes()
    {
        Span<byte> buffer = stackalloc byte[16];
        var writer = new BitWriter(buffer, lsbFirst: true);

        writer.WriteBits(0b1, 1);
        writer.WriteBits(0b10, 2);
        writer.WriteBits(0b101, 3);
        writer.WriteBits(0b1010, 4);
        writer.WriteBits(0xAB, 8);
        writer.WriteBits(0xCDEF, 16);

        var written = writer.GetWrittenSpan();
        var reader = new BitReader(written.ToArray(), lsbFirst: true);

        Assert.That(reader.ReadBits(1), Is.EqualTo(0b1U));
        Assert.That(reader.ReadBits(2), Is.EqualTo(0b10U));
        Assert.That(reader.ReadBits(3), Is.EqualTo(0b101U));
        Assert.That(reader.ReadBits(4), Is.EqualTo(0b1010U));
        Assert.That(reader.ReadBits(8), Is.EqualTo(0xABU));
        Assert.That(reader.ReadBits(16), Is.EqualTo(0xCDEFU));
    }

    #endregion

    #region PeekBits Tests

    [TestCase(TestName = "BitReader: PeekBits не продвигает позицию")]
    public void BitReaderPeekBitsDoesNotAdvance()
    {
        ReadOnlySpan<byte> data = [0xAB, 0xCD];
        var reader = new BitReader(data, lsbFirst: false);

        var peeked = reader.PeekBits(8);
        Assert.That(peeked, Is.EqualTo(0xABU));
        Assert.That(reader.BitPosition, Is.Zero);

        var read = reader.ReadBits(8);
        Assert.That(read, Is.EqualTo(0xABU));
        Assert.That(reader.BitPosition, Is.EqualTo(8));
    }

    #endregion

    #region Alignment Tests

    [TestCase(TestName = "BitReader: AlignToByte пропускает биты")]
    public void BitReaderAlignToByte()
    {
        ReadOnlySpan<byte> data = [0xAB, 0xCD];
        var reader = new BitReader(data, lsbFirst: false);

        reader.ReadBits(3);
        Assert.That(reader.BitPosition, Is.EqualTo(3));

        reader.AlignToByte();
        // После AlignToByte позиция должна быть кратна 8
        // Читаем следующий байт
        var next = reader.ReadBits(8);
        Assert.That(next, Is.EqualTo(0xCDU));
    }

    [TestCase(TestName = "BitWriter: AlignToByte дополняет нулями")]
    public void BitWriterAlignToByte()
    {
        Span<byte> buffer = stackalloc byte[4];
        var writer = new BitWriter(buffer, lsbFirst: false);

        writer.WriteBits(0b111, 3); // 3 бита
        writer.AlignToByte();       // +5 нулей
        writer.WriteBits(0xCD, 8);

        var result = writer.GetWrittenSpan();
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0], Is.EqualTo(0b11100000)); // 0xE0
        Assert.That(result[1], Is.EqualTo(0xCD));
    }

    #endregion

    #region Edge Cases

    [TestCase(TestName = "BitReader: RemainingBits корректен")]
    public void BitReaderRemainingBits()
    {
        ReadOnlySpan<byte> data = [0xAB, 0xCD]; // 16 бит
        var reader = new BitReader(data, lsbFirst: false);

        Assert.That(reader.RemainingBits, Is.EqualTo(16));

        reader.ReadBits(5);
        Assert.That(reader.RemainingBits, Is.EqualTo(11));

        reader.ReadBits(11);
        Assert.That(reader.RemainingBits, Is.Zero);
        Assert.That(reader.IsAtEnd, Is.True);
    }

    [TestCase(TestName = "BitWriter: BytesWritten корректен")]
    public void BitWriterBytesWritten()
    {
        Span<byte> buffer = stackalloc byte[8];
        var writer = new BitWriter(buffer, lsbFirst: false);

        writer.WriteBits(0b111, 3);
        Assert.That(writer.BytesWritten, Is.EqualTo(1)); // неполный байт

        writer.WriteBits(0b11111, 5);
        Assert.That(writer.BytesWritten, Is.EqualTo(1)); // ровно 1 байт

        writer.WriteBits(0xFF, 8);
        Assert.That(writer.BytesWritten, Is.EqualTo(2)); // 2 байта
    }

    [TestCase(TestName = "BitReader: ReadBits(0) возвращает 0")]
    public void BitReaderReadZeroBits()
    {
        ReadOnlySpan<byte> data = [0xAB];
        var reader = new BitReader(data, lsbFirst: false);

        Assert.That(reader.ReadBits(0), Is.Zero);
        Assert.That(reader.BitPosition, Is.Zero);
    }

    [TestCase(TestName = "BitWriter: WriteBits(_, 0) ничего не делает")]
    public void BitWriterWriteZeroBits()
    {
        Span<byte> buffer = stackalloc byte[4];
        var writer = new BitWriter(buffer, lsbFirst: false);

        writer.WriteBits(0xFFFFFFFF, 0);
        Assert.That(writer.BitPosition, Is.Zero);
    }

    #endregion

    #region Large Data Tests

    [TestCase(TestName = "BitStream: большой объём данных MSB")]
    public void BitStreamLargeDataMsb()
    {
        const int count = 10000;
        var buffer = new byte[count * 4];
        var writer = new BitWriter(buffer, lsbFirst: false);

        for (var i = 0; i < count; i++)
            writer.WriteBits((uint)i, 17);

        var written = writer.GetWrittenSpan();
        var reader = new BitReader(written.ToArray(), lsbFirst: false);

        for (var i = 0; i < count; i++)
        {
            var value = reader.ReadBits(17);
            Assert.That(value, Is.EqualTo((uint)i & 0x1FFFF), $"Mismatch at index {i}");
        }
    }

    [TestCase(TestName = "BitStream: большой объём данных LSB")]
    public void BitStreamLargeDataLsb()
    {
        const int count = 10000;
        var buffer = new byte[count * 4];
        var writer = new BitWriter(buffer, lsbFirst: true);

        for (var i = 0; i < count; i++)
            writer.WriteBits((uint)i, 17);

        var written = writer.GetWrittenSpan();
        var reader = new BitReader(written.ToArray(), lsbFirst: true);

        for (var i = 0; i < count; i++)
        {
            var value = reader.ReadBits(17);
            Assert.That(value, Is.EqualTo((uint)i & 0x1FFFF), $"Mismatch at index {i}");
        }
    }

    #endregion

    #region Seek Tests

    [TestCase(TestName = "BitReader: Seek позволяет перемещаться")]
    public void BitReaderSeek()
    {
        ReadOnlySpan<byte> data = [0xAB, 0xCD, 0xEF];
        var reader = new BitReader(data, lsbFirst: false);

        reader.Seek(8);
        Assert.That(reader.ReadBits(8), Is.EqualTo(0xCDU));

        reader.Seek(0);
        Assert.That(reader.ReadBits(8), Is.EqualTo(0xABU));

        reader.Seek(16);
        Assert.That(reader.ReadBits(8), Is.EqualTo(0xEFU));
    }

    [TestCase(TestName = "BitReader: Reset сбрасывает в начало")]
    public void BitReaderReset()
    {
        ReadOnlySpan<byte> data = [0xAB, 0xCD];
        var reader = new BitReader(data, lsbFirst: false);

        reader.ReadBits(12);
        reader.Reset();

        Assert.That(reader.BitPosition, Is.Zero);
        Assert.That(reader.ReadBits(8), Is.EqualTo(0xABU));
    }

    #endregion

    #region ReverseBitReader Tests

    [TestCase(TestName = "ReverseBitReader: чтение с конца")]
    public void ReverseBitReaderBasic()
    {
        // Данные: [0x01, 0x02, 0x03]
        // Чтение с конца: сначала 0x03, потом 0x02, потом 0x01
        ReadOnlySpan<byte> data = [0x01, 0x02, 0x03];
        var reader = new ReverseBitReader(data);

        // LSB-first: первые 8 бит из последнего байта (0x03)
        Assert.That(reader.ReadBits(8), Is.EqualTo(0x03U));
        Assert.That(reader.ReadBits(8), Is.EqualTo(0x02U));
        Assert.That(reader.ReadBits(8), Is.EqualTo(0x01U));
    }

    [TestCase(TestName = "ReverseBitReader: чтение нескольких бит")]
    public void ReverseBitReaderMultipleBits()
    {
        // 0x2D = 0b00101101
        ReadOnlySpan<byte> data = [0x2D];
        var reader = new ReverseBitReader(data);

        // LSB-first: младшие 4 бита = 0b1101 = 13
        Assert.That(reader.ReadBits(4), Is.EqualTo(0b1101U));
        // Следующие 4 бита = 0b0010 = 2
        Assert.That(reader.ReadBits(4), Is.EqualTo(0b0010U));
    }

    [TestCase(TestName = "ReverseBitReader: TryReadBits возвращает false при недостатке данных")]
    public void ReverseBitReaderTryReadBits()
    {
        ReadOnlySpan<byte> data = [0xAB];
        var reader = new ReverseBitReader(data);

        Assert.That(reader.TryReadBits(8, out var value), Is.True);
        Assert.That(value, Is.EqualTo(0xABU));

        // Пытаемся прочитать ещё - данных нет
        Assert.That(reader.TryReadBits(1, out _), Is.False);
    }

    [TestCase(TestName = "ReverseBitReader: TrySkipPadding пропускает padding")]
    public void ReverseBitReaderSkipPadding()
    {
        // Padding: 0b00000001 (0x01) - 7 нулей + 1
        ReadOnlySpan<byte> data = [0xAB, 0x01];
        var reader = new ReverseBitReader(data);

        // Пропускаем padding (ищем '1', сбрасываем буфер)
        Assert.That(reader.TrySkipPadding(), Is.True);

        // Теперь читаем полезные данные
        Assert.That(reader.ReadBits(8), Is.EqualTo(0xABU));
    }

    [TestCase(TestName = "ReverseBitReader: round-trip с BitWriter LSB")]
    public void ReverseBitReaderRoundTrip()
    {
        // Записываем данные LSB-first
        // По аналогии с Zstd: данные пишутся вперёд, читаются с конца
        Span<byte> buffer = stackalloc byte[8];
        var writer = new BitWriter(buffer, lsbFirst: true);

        // Записываем данные: 4 бит + 4 бит + 8 бит = 16 бит = 2 байта
        writer.WriteBits(0b1101, 4);   // 0xD в младших 4 битах
        writer.WriteBits(0b1010, 4);   // 0xA в старших 4 битах → байт 0: 0xAD
        writer.WriteBits(0xCD, 8);     // байт 1: 0xCD

        var written = writer.GetWrittenSpan();

        // Проверяем что записано корректно
        Assert.That(written.Length, Is.EqualTo(2));
        Assert.That(written[0], Is.EqualTo(0xAD)); // 0b10101101
        Assert.That(written[1], Is.EqualTo(0xCD));

        // Читаем в обратном порядке (с конца)
        // ReverseBitReader читает байты с конца, но биты внутри байта — LSB-first
        var reader = new ReverseBitReader(written.ToArray());

        // Сначала читается последний байт (0xCD)
        Assert.That(reader.ReadBits(8), Is.EqualTo(0xCDU));

        // Затем байт 0xAD читается LSB-first:
        // младшие 4 бита = 0b1101 = 13, старшие 4 бита = 0b1010 = 10
        Assert.That(reader.ReadBits(4), Is.EqualTo(0b1101U)); // младшие 4 бита 0xAD
        Assert.That(reader.ReadBits(4), Is.EqualTo(0b1010U)); // старшие 4 бита 0xAD
    }

    #endregion

    #region BitWriter Overflow Tests

    [TestCase(TestName = "BitWriter: IsOverflow при переполнении")]
    public void BitWriterIsOverflow()
    {
        Span<byte> buffer = stackalloc byte[1];
        var writer = new BitWriter(buffer, lsbFirst: true);

        Assert.That(writer.IsOverflow, Is.False);

        // Записываем 8 бит - OK (заполняет буфер)
        writer.WriteBits(0xFF, 8);
        Assert.That(writer.IsOverflow, Is.False);

        // Записываем ещё 8 бит - переполнение (при попытке flush второго байта)
        writer.WriteBits(0xFF, 8);
        Assert.That(writer.IsOverflow, Is.True);
    }

    [TestCase(TestName = "BitWriter: TryWriteBits возвращает false при переполнении")]
    public void BitWriterTryWriteBits()
    {
        Span<byte> buffer = stackalloc byte[1];
        var writer = new BitWriter(buffer, lsbFirst: true);

        Assert.That(writer.TryWriteBits(0xFF, 8), Is.True);
        Assert.That(writer.TryWriteBits(0xFF, 8), Is.False); // переполнение при flush
        Assert.That(writer.IsOverflow, Is.True);
    }

    [TestCase(TestName = "BitWriter: Reset сбрасывает IsOverflow")]
    public void BitWriterResetClearsOverflow()
    {
        Span<byte> buffer = stackalloc byte[1];
        var writer = new BitWriter(buffer, lsbFirst: true);

        writer.WriteBits(0xFF, 8);
        writer.WriteBits(1, 8); // overflow
        Assert.That(writer.IsOverflow, Is.True);

        writer.Reset();
        Assert.That(writer.IsOverflow, Is.False);
        Assert.That(writer.BytesWritten, Is.Zero);
    }

    [TestCase(TestName = "BitWriter: TryFinishWithPadding")]
    public void BitWriterTryFinishWithPadding()
    {
        Span<byte> buffer = stackalloc byte[4];
        var writer = new BitWriter(buffer, lsbFirst: true);

        writer.WriteBits(0b1101, 4);
        Assert.That(writer.TryFinishWithPadding(), Is.True);

        // Результат: 4 бита данных + 4 нуля + 1 бит = 0b00001101, 0b00000001
        var result = writer.GetWrittenSpan();
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0], Is.EqualTo(0x0D)); // 0b00001101
        Assert.That(result[1], Is.EqualTo(0x01)); // padding '1'
    }

    #endregion
}
