using Atom.IO.Compression.Huffman;

namespace Atom.IO.Compression.Tests.Huffman;

/// <summary>
/// Тесты модуля Huffman.
/// </summary>
[TestFixture]
public sealed class HuffmanTests
{
    #region HuffmanCode Tests

    [TestCase(TestName = "HuffmanCode: создание и свойства")]
    public void HuffmanCodeCreation()
    {
        var code = new HuffmanCode(symbol: 65, code: 0b101, length: 3);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code.Symbol, Is.EqualTo(65));
            Assert.That(code.Code, Is.EqualTo(0b101u));
            Assert.That(code.Length, Is.EqualTo(3));
            Assert.That(code.IsEmpty, Is.False);
        }
    }

    [TestCase(TestName = "HuffmanCode: пустой код")]
    public void HuffmanCodeEmpty()
    {
        var code = new HuffmanCode(symbol: 0, code: 0, length: 0);

        Assert.That(code.IsEmpty, Is.True);
    }

    [TestCase(TestName = "HuffmanCode: реверс битов")]
    public void HuffmanCodeReverse()
    {
        // 0b101 (3 бита) → 0b101 (симметричный)
        var code1 = new HuffmanCode(symbol: 1, code: 0b101, length: 3);
        var reversed1 = code1.Reverse();
        Assert.That(reversed1.Code, Is.EqualTo(0b101u));

        // 0b110 (3 бита) → 0b011
        var code2 = new HuffmanCode(symbol: 2, code: 0b110, length: 3);
        var reversed2 = code2.Reverse();
        Assert.That(reversed2.Code, Is.EqualTo(0b011u));

        // 0b1000 (4 бита) → 0b0001
        var code3 = new HuffmanCode(symbol: 3, code: 0b1000, length: 4);
        var reversed3 = code3.Reverse();
        Assert.That(reversed3.Code, Is.EqualTo(0b0001u));
    }

    [TestCase(TestName = "HuffmanCode: равенство")]
    public void HuffmanCodeEquality()
    {
        var code1 = new HuffmanCode(symbol: 65, code: 0b101, length: 3);
        var code2 = new HuffmanCode(symbol: 65, code: 0b101, length: 3);
        var code3 = new HuffmanCode(symbol: 66, code: 0b101, length: 3);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code1, Is.EqualTo(code2));
            Assert.That(code1, Is.Not.EqualTo(code3));
            Assert.That(code1, Is.EqualTo(code2));
        }
    }

    #endregion

    #region HuffmanTreeBuilder Tests

    [TestCase(TestName = "HuffmanTreeBuilder: построение из code lengths")]
    public void BuildDecodeTableFromCodeLengths()
    {
        // Простой алфавит: A=1 бит, B=2 бита, C=2 бита
        byte[] codeLengths = [1, 2, 2, 0]; // A, B, C, D(отсутствует)

        var symbols = new byte[4]; // 2^2 = 4
        var lengths = new byte[4];

        var maxBits = HuffmanTreeBuilder.BuildDecodeTable(codeLengths, symbols, lengths, maxBits: 2, lsbFirst: true);

        Assert.That(maxBits, Is.EqualTo(2));

        // Проверяем таблицу
        // При LSB-first: A=0, B=01, C=11
        // Индекс 0b00: A (код 0), длина 1
        // Индекс 0b01: B (код 01), длина 2
        // Индекс 0b10: A (реплицирован), длина 1
        // Индекс 0b11: C (код 11), длина 2

        using (Assert.EnterMultipleScope())
        {
            Assert.That(symbols[0], Is.Zero); // A
            Assert.That(lengths[0], Is.EqualTo(1));
            Assert.That(symbols[1], Is.EqualTo(1)); // B
            Assert.That(lengths[1], Is.EqualTo(2));
            Assert.That(symbols[2], Is.Zero); // A (реплицирован)
            Assert.That(lengths[2], Is.EqualTo(1));
            Assert.That(symbols[3], Is.EqualTo(2)); // C
            Assert.That(lengths[3], Is.EqualTo(2));
        }
    }

    [TestCase(TestName = "HuffmanTreeBuilder: построение из частот")]
    public void BuildFromFrequencies()
    {
        // Частоты: A=100, B=50, C=25, D=25
        uint[] frequencies = [100, 50, 25, 25];
        var codeLengths = new byte[4];

        var maxBits = HuffmanTreeBuilder.BuildFromFrequencies(frequencies, codeLengths, maxCodeLength: 15);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(maxBits, Is.GreaterThan(0));

            // A должен иметь самый короткий код
            Assert.That(codeLengths[0], Is.LessThanOrEqualTo(codeLengths[1]));
        }

        Assert.That(codeLengths[0], Is.LessThanOrEqualTo(codeLengths[2]));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(codeLengths[0], Is.LessThanOrEqualTo(codeLengths[3]));

            // Проверяем Kraft inequality
            Assert.That(HuffmanTreeBuilder.ValidateCodeLengths(codeLengths), Is.True);
        }

    }

    [TestCase(TestName = "HuffmanTreeBuilder: построение напрямую из данных (SIMD)")]
    public void BuildFromData()
    {
        // Данные: много 'A', меньше 'B', ещё меньше 'C' и 'D'
        var data = new byte[200];
        for (var i = 0; i < 100; i++)
        {
            data[i] = 0; // A
        }

        for (var i = 100; i < 150; i++)
        {
            data[i] = 1; // B
        }

        for (var i = 150; i < 175; i++)
        {
            data[i] = 2; // C
        }

        for (var i = 175; i < 200; i++)
        {
            data[i] = 3; // D
        }

        var codeLengths = new byte[256];
        var maxBits = HuffmanTreeBuilder.BuildFromData(data, codeLengths, maxCodeLength: 15);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(maxBits, Is.GreaterThan(0));

            // A (самый частый) должен иметь самый короткий код
            Assert.That(codeLengths[0], Is.LessThanOrEqualTo(codeLengths[1]));
            Assert.That(codeLengths[0], Is.LessThanOrEqualTo(codeLengths[2]));
            Assert.That(codeLengths[0], Is.LessThanOrEqualTo(codeLengths[3]));

            // Проверяем Kraft inequality
            Assert.That(HuffmanTreeBuilder.ValidateCodeLengths(codeLengths.AsSpan(0, 4)), Is.True);
        }
    }

    [TestCase(TestName = "HuffmanTreeBuilder: построение напрямую из данных с кодами")]
    public void BuildFromDataWithCodes()
    {
        // Случайные данные
        var data = new byte[1000];
        var random = new Random(42);
        random.NextBytes(data);

        var codeLengths = new byte[256];
        var codes = new uint[256];
        var maxBits = HuffmanTreeBuilder.BuildFromData(data, codeLengths, codes, maxCodeLength: 11);

        Assert.That(maxBits, Is.GreaterThan(0));

        // Проверяем, что все активные символы имеют ненулевую длину и код
        var activeCount = 0;
        for (var i = 0; i < 256; i++)
        {
            if (codeLengths[i] > 0)
            {
                activeCount++;
            }
        }

        Assert.That(activeCount, Is.GreaterThan(0));
    }

    [TestCase(TestName = "HuffmanTreeBuilder: построение кодов для кодирования")]
    public void BuildEncodeCodes()
    {
        byte[] codeLengths = [1, 2, 2, 0];
        var codes = new uint[4];

        var maxBits = HuffmanTreeBuilder.BuildEncodeCodes(codeLengths, codes, lsbFirst: true);

        Assert.That(maxBits, Is.EqualTo(2));

        // Проверяем, что все активные символы имеют уникальные коды
        using (Assert.EnterMultipleScope())
        {
            Assert.That(codes[0], Is.Not.EqualTo(codes[1])); // A != B
            Assert.That(codes[1], Is.Not.EqualTo(codes[2])); // B != C
        }
    }

    [TestCase(TestName = "HuffmanTreeBuilder: валидация Kraft inequality")]
    public void ValidateKraftInequality()
    {
        // Валидный набор
        byte[] valid = [1, 2, 2];
        Assert.That(HuffmanTreeBuilder.ValidateCodeLengths(valid), Is.True);

        // Невалидный набор (сумма > 1)
        byte[] invalid = [1, 1, 1];
        Assert.That(HuffmanTreeBuilder.ValidateCodeLengths(invalid), Is.False);
    }

    #endregion

    #region HuffmanTable Tests

    [TestCase(TestName = "HuffmanTableBuffer: создание и использование")]
    public void HuffmanTableBufferUsage()
    {
        using var buffer = new HuffmanTableBuffer(tableLog: 8);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(buffer.TableLog, Is.EqualTo(8));
            Assert.That(buffer.TableSize, Is.EqualTo(256));
            Assert.That(buffer.Symbols.Length, Is.EqualTo(256));
            Assert.That(buffer.Lengths.Length, Is.EqualTo(256));
        }
    }

    [TestCase(TestName = "HuffmanTable: декодирование символа")]
    public void HuffmanTableDecodeSymbol()
    {
        // Строим таблицу вручную
        byte[] codeLengths = [1, 2, 2];

        // Используем managed буфер
        using var buffer = new HuffmanTableBuffer(tableLog: 2);
        HuffmanTreeBuilder.BuildDecodeTable(codeLengths, buffer.Symbols, buffer.Lengths, maxBits: 2, lsbFirst: true);

        var table = buffer.ToTable();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(table.TableLog, Is.EqualTo(2));
            Assert.That(table.TableSize, Is.EqualTo(4));
            Assert.That(table.IsValid, Is.True);
        }

        // Декодируем
        var sym0 = table.DecodeSymbol(0b00, out var len0);
        var sym1 = table.DecodeSymbol(0b01, out var len1);
        var sym2 = table.DecodeSymbol(0b11, out var len2);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sym0, Is.Zero); // A
            Assert.That(len0, Is.EqualTo(1));
            Assert.That(sym1, Is.EqualTo(1)); // B
            Assert.That(len1, Is.EqualTo(2));
            Assert.That(sym2, Is.EqualTo(2)); // C
            Assert.That(len2, Is.EqualTo(2));
        }
    }

    #endregion

    #region Round-trip Tests

    [TestCase(TestName = "Huffman: диагностический тест кодирования")]
    public void DiagnosticEncodingTest()
    {
        // Простейший случай: один символ
        byte[] original = [0, 0, 0, 0, 0];

        // Частоты
        var frequencies = new uint[256];
        frequencies[0] = 5;

        // Строим коды
        var codeLengths = new byte[256];
        var maxBits = HuffmanTreeBuilder.BuildFromFrequencies(frequencies, codeLengths, maxCodeLength: 11);

        Assert.That(maxBits, Is.GreaterThan(0), "maxBits должен быть > 0");
        Assert.That(codeLengths[0], Is.EqualTo(1), "Единственный символ должен иметь длину 1");

        // Строим коды для кодирования
        var codes = new uint[256];
        var encodeMaxBits = HuffmanTreeBuilder.BuildEncodeCodes(codeLengths, codes, lsbFirst: true);

        Assert.That(encodeMaxBits, Is.GreaterThan(0), "encodeMaxBits должен быть > 0");

        // Кодируем
        var encoded = new byte[64];
        var encodedBytes = HuffmanEncoder.EncodeReverseStream(original, encoded, codes, codeLengths);

        Assert.That(encodedBytes, Is.GreaterThan(0), $"Кодирование должно быть успешным. codes[0]={codes[0]}, len[0]={codeLengths[0]}");

        // Выводим закодированные байты для отладки
        var encodedHex = string.Join(" ", encoded[..encodedBytes].ToArray().Select(b => b.ToString("X2")));
        TestContext.Out.WriteLine($"Encoded: {encodedHex}");
    }

    [TestCase(TestName = "Huffman: минимальный round-trip тест")]
    public void MinimalRoundTrip()
    {
        // Простейший случай: 2 символа
        byte[] original = [0, 1, 0, 0, 1];

        // Частоты — размер 256 для полного алфавита
        var frequencies = new uint[256];
        foreach (var b in original)
            frequencies[b]++;

        // Строим коды
        var codeLengths = new byte[256];
        var maxBits = HuffmanTreeBuilder.BuildFromFrequencies(frequencies, codeLengths, maxCodeLength: 11);

        TestContext.Out.WriteLine($"maxBits={maxBits}, codeLengths[0]={codeLengths[0]}, codeLengths[1]={codeLengths[1]}");

        Assert.Multiple(() =>
        {
            // Проверяем, что длины назначены
            Assert.That(codeLengths[0], Is.GreaterThan(0), "codeLengths[0] должен быть > 0");
            Assert.That(codeLengths[1], Is.GreaterThan(0), "codeLengths[1] должен быть > 0");
        });

        // Строим коды для кодирования
        var codes = new uint[256];
        HuffmanTreeBuilder.BuildEncodeCodes(codeLengths, codes, lsbFirst: true);

        TestContext.Out.WriteLine($"codes[0]={codes[0]:X}, codes[1]={codes[1]:X}");

        // Кодируем
        var encoded = new byte[64];
        var encodedBytes = HuffmanEncoder.EncodeReverseStream(original, encoded, codes, codeLengths);

        Assert.That(encodedBytes, Is.GreaterThan(0), $"Кодирование должно быть успешным, len[0]={codeLengths[0]}, len[1]={codeLengths[1]}");

        var encodedHex = string.Join(" ", encoded[..encodedBytes].ToArray().Select(b => b.ToString("X2")));
        TestContext.Out.WriteLine($"Encoded ({encodedBytes} bytes): {encodedHex}");

        // Строим таблицу декодирования
        using var buffer = new HuffmanTableBuffer(tableLog: maxBits);
        HuffmanTreeBuilder.BuildDecodeTable(codeLengths, buffer.Symbols, buffer.Lengths, maxBits, lsbFirst: true);

        // Выводим таблицу
        for (var i = 0; i < buffer.TableSize; i++)
        {
            TestContext.Out.WriteLine($"table[{i}]: sym={buffer.Symbols[i]}, len={buffer.Lengths[i]}");
        }

        var table = buffer.ToTable();

        // Декодируем
        var decoded = new byte[5];
        var decodedCount = HuffmanDecoder.DecodeReverseStream(encoded.AsSpan(0, encodedBytes), decoded, table);

        TestContext.Out.WriteLine($"Decoded {decodedCount} symbols: {string.Join(", ", decoded.ToArray())}");

        Assert.That(decodedCount, Is.EqualTo(original.Length), $"Декодировано {decodedCount}, ожидалось {original.Length}");

        for (var i = 0; i < original.Length; i++)
        {
            Assert.That(decoded[i], Is.EqualTo(original[i]), $"Несовпадение на позиции {i}");
        }
    }

    [TestCase(TestName = "Huffman: round-trip кодирование/декодирование")]
    public void RoundTripEncodeDecode()
    {
        // Простейший тест: 5 символов с 1-битными кодами
        byte[] original = [0, 1, 0, 0, 1];

        // codes: 0→0, 1→1 (длина 1)
        var codeLengths = new byte[256];
        codeLengths[0] = 1;
        codeLengths[1] = 1;
        var maxBits = 1;

        TestContext.Out.WriteLine($"Original: {string.Join(", ", original)}");

        // Строим таблицу кодирования
        var codes = new uint[256];
        HuffmanTreeBuilder.BuildEncodeCodes(codeLengths, codes, lsbFirst: true);

        TestContext.Out.WriteLine($"codes[0]={codes[0]}, codes[1]={codes[1]}");

        // Кодируем
        var encoded = new byte[64];
        var encodedBytes = HuffmanEncoder.EncodeReverseStream(original, encoded, codes, codeLengths);

        Assert.That(encodedBytes, Is.GreaterThan(0), "Кодирование должно быть успешным");

        var encodedBinary = string.Join(" ", encoded.Take(encodedBytes).Select(b => Convert.ToString(b, 2).PadLeft(8, '0')));
        TestContext.Out.WriteLine($"Encoded ({encodedBytes} bytes): {encodedBinary}");

        // Строим таблицу декодирования
        using var buffer = new HuffmanTableBuffer(tableLog: maxBits);
        HuffmanTreeBuilder.BuildDecodeTable(codeLengths, buffer.Symbols, buffer.Lengths, maxBits, lsbFirst: true);
        var table = buffer.ToTable();

        TestContext.Out.WriteLine($"table[0]={buffer.Symbols[0]}, table[1]={buffer.Symbols[1]}");

        // Декодируем
        var decoded = new byte[original.Length];
        var decodedCount = HuffmanDecoder.DecodeReverseStream(encoded.AsSpan(0, encodedBytes), decoded, table);

        TestContext.Out.WriteLine($"Decoded ({decodedCount}): {string.Join(", ", decoded)}");

        Assert.That(decodedCount, Is.EqualTo(original.Length), "Количество декодированных символов должно совпадать");
        Assert.That(decoded, Is.EqualTo(original));
    }

    [TestCase(TestName = "Huffman: сжатие эффективно для неравномерного распределения")]
    public void CompressionEfficiency()
    {
        // Данные с очень неравномерным распределением
        var original = new byte[1000];
        for (var i = 0; i < original.Length; i++)
        {
            // 80% символов = 0, 20% = случайные
            original[i] = i % 5 == 0 ? (byte)(i % 256) : (byte)0;
        }

        // Подсчитываем частоты
        var frequencies = new uint[256];
        foreach (var b in original)
            frequencies[b]++;

        // Строим коды
        var codeLengths = new byte[256];
        HuffmanTreeBuilder.BuildFromFrequencies(frequencies, codeLengths, maxCodeLength: 15);

        // Строим таблицу кодирования
        var codes = new uint[256];
        HuffmanTreeBuilder.BuildEncodeCodes(codeLengths, codes, lsbFirst: true);

        // Вычисляем размер сжатых данных
        var encodedBits = HuffmanEncoder.CalculateEncodedBits(original, codeLengths);
        var encodedBytes = HuffmanEncoder.CalculateEncodedBytes(original, codeLengths);

        Assert.Multiple(() =>
        {
            Assert.That(encodedBits, Is.GreaterThan(0));
            Assert.That(encodedBytes, Is.LessThan(original.Length), "Сжатие должно уменьшить размер");

            // Символ 0 должен иметь относительно короткий код (учитывая, что есть 200 разных символов)
            Assert.That(codeLengths[0], Is.LessThanOrEqualTo(5), "Частый символ должен иметь короткий код");
        });
    }

    #endregion
}
