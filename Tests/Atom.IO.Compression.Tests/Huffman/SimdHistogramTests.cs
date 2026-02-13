using Atom.IO.Compression.Huffman;

namespace Atom.IO.Compression.Tests.Huffman;

/// <summary>
/// Тесты SIMD-оптимизированной гистограммы.
/// </summary>
[TestFixture]
public sealed class SimdHistogramTests
{
    #region ComputeHistogram Tests

    [TestCase(TestName = "SimdHistogram: пустые данные")]
    public void EmptyData()
    {
        var histogram = new uint[256];
        SimdHistogram.ComputeHistogram([], histogram);

        Assert.That(histogram.All(x => x == 0), Is.True);
    }

    [TestCase(TestName = "SimdHistogram: один байт")]
    public void SingleByte()
    {
        var histogram = new uint[256];
        SimdHistogram.ComputeHistogram([42], histogram);

        Assert.That(histogram[42], Is.EqualTo(1));
        Assert.That(histogram.Where((_, i) => i != 42).All(x => x == 0), Is.True);
    }

    [TestCase(TestName = "SimdHistogram: повторяющиеся байты")]
    public void RepeatingBytes()
    {
        var data = new byte[100];
        Array.Fill(data, (byte)0xAB);

        var histogram = new uint[256];
        SimdHistogram.ComputeHistogram(data, histogram);

        Assert.That(histogram[0xAB], Is.EqualTo(100));
    }

    [TestCase(TestName = "SimdHistogram: все байты 0-255")]
    public void AllBytes()
    {
        var data = new byte[256];
        for (var i = 0; i < 256; i++)
            data[i] = (byte)i;

        var histogram = new uint[256];
        SimdHistogram.ComputeHistogram(data, histogram);

        using (Assert.EnterMultipleScope())
        {
            for (var i = 0; i < 256; i++)
                Assert.That(histogram[i], Is.EqualTo(1), $"histogram[{i}]");
        }
    }

    [TestCase(TestName = "SimdHistogram: большие данные (SIMD путь)")]
    public void LargeData()
    {
        // 1KB данных — достаточно для активации SIMD пути
        var data = new byte[1024];
        var random = new Random(42);
        random.NextBytes(data);

        // Эталонная гистограмма скалярным методом
        var expected = new uint[256];
        foreach (var b in data)
            expected[b]++;

        // SIMD гистограмма
        var actual = new uint[256];
        SimdHistogram.ComputeHistogram(data, actual);

        Assert.That(actual, Is.EqualTo(expected));
    }

    [TestCase(TestName = "SimdHistogram: очень большие данные (1MB)")]
    public void VeryLargeData()
    {
        var data = new byte[1024 * 1024];
        var random = new Random(123);
        random.NextBytes(data);

        // Эталонная гистограмма
        var expected = new uint[256];
        foreach (var b in data)
            expected[b]++;

        // SIMD гистограмма
        var actual = new uint[256];
        SimdHistogram.ComputeHistogram(data, actual);

        Assert.That(actual, Is.EqualTo(expected));
    }

    [TestCase(TestName = "SimdHistogram: нечётная длина данных")]
    public void OddLength()
    {
        var data = new byte[333];
        for (var i = 0; i < data.Length; i++)
            data[i] = (byte)(i % 10);

        var expected = new uint[256];
        foreach (var b in data)
            expected[b]++;

        var actual = new uint[256];
        SimdHistogram.ComputeHistogram(data, actual);

        Assert.That(actual, Is.EqualTo(expected));
    }

    #endregion

    #region AccumulateHistogram Tests

    [TestCase(TestName = "SimdHistogram: аккумуляция к существующим значениям")]
    public void AccumulateToExisting()
    {
        var histogram = new uint[256];
        histogram[10] = 5;
        histogram[20] = 10;

        byte[] data = [10, 10, 20, 30];
        SimdHistogram.AccumulateHistogram(data, histogram);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(histogram[10], Is.EqualTo(7)); // 5 + 2
            Assert.That(histogram[20], Is.EqualTo(11)); // 10 + 1
            Assert.That(histogram[30], Is.EqualTo(1));
        }
    }

    #endregion

    #region CountNonZeroSymbols Tests

    [TestCase(TestName = "SimdHistogram: подсчёт ненулевых символов (пустая гистограмма)")]
    public void CountNonZeroEmpty()
    {
        var histogram = new uint[256];
        var count = SimdHistogram.CountNonZeroSymbols(histogram);

        Assert.That(count, Is.Zero);
    }

    [TestCase(TestName = "SimdHistogram: подсчёт ненулевых символов (все ненулевые)")]
    public void CountNonZeroAll()
    {
        var histogram = new uint[256];
        for (var i = 0; i < 256; i++)
            histogram[i] = (uint)(i + 1);

        var count = SimdHistogram.CountNonZeroSymbols(histogram);

        Assert.That(count, Is.EqualTo(256));
    }

    [TestCase(TestName = "SimdHistogram: подсчёт ненулевых символов (частичное заполнение)")]
    public void CountNonZeroPartial()
    {
        var histogram = new uint[256];
        histogram[0] = 10;
        histogram[127] = 5;
        histogram[255] = 1;

        var count = SimdHistogram.CountNonZeroSymbols(histogram);

        Assert.That(count, Is.EqualTo(3));
    }

    [TestCase(TestName = "SimdHistogram: подсчёт ненулевых символов (случайные данные)")]
    public void CountNonZeroRandom()
    {
        var histogram = new uint[256];
        var random = new Random(42);
        var expected = 0;

        for (var i = 0; i < 256; i++)
        {
            if (random.Next(2) == 1)
            {
                histogram[i] = (uint)random.Next(1, 1000);
                expected++;
            }
        }

        var count = SimdHistogram.CountNonZeroSymbols(histogram);

        Assert.That(count, Is.EqualTo(expected));
    }

    #endregion

    #region FindMax Tests

    [TestCase(TestName = "SimdHistogram: поиск максимума (пустая гистограмма)")]
    public void FindMaxEmpty()
    {
        var histogram = Array.Empty<uint>();
        var (maxValue, maxIndex) = SimdHistogram.FindMax(histogram);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(maxValue, Is.Zero);
            Assert.That(maxIndex, Is.EqualTo(-1));
        }
    }

    [TestCase(TestName = "SimdHistogram: поиск максимума (один элемент)")]
    public void FindMaxSingle()
    {
        var histogram = new uint[256];
        histogram[100] = 42;

        var (maxValue, maxIndex) = SimdHistogram.FindMax(histogram);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(maxValue, Is.EqualTo(42u));
            Assert.That(maxIndex, Is.EqualTo(100));
        }
    }

    [TestCase(TestName = "SimdHistogram: поиск максимума (несколько равных)")]
    public void FindMaxMultipleEqual()
    {
        var histogram = new uint[256];
        histogram[50] = 100;
        histogram[150] = 100;
        histogram[200] = 100;

        var (maxValue, maxIndex) = SimdHistogram.FindMax(histogram);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(maxValue, Is.EqualTo(100u));
            Assert.That(maxIndex, Is.EqualTo(50)); // Первый с максимальным значением
        }
    }

    [TestCase(TestName = "SimdHistogram: поиск максимума (случайные данные)")]
    public void FindMaxRandom()
    {
        var histogram = new uint[256];
        var random = new Random(42);

        uint expectedMax = 0;
        var expectedIndex = -1;

        for (var i = 0; i < 256; i++)
        {
            histogram[i] = (uint)random.Next(0, 10000);
            if (histogram[i] > expectedMax)
            {
                expectedMax = histogram[i];
                expectedIndex = i;
            }
        }

        var (maxValue, maxIndex) = SimdHistogram.FindMax(histogram);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(maxValue, Is.EqualTo(expectedMax));
            Assert.That(maxIndex, Is.EqualTo(expectedIndex));
        }
    }

    #endregion

    #region Sum Tests

    [TestCase(TestName = "SimdHistogram: сумма (пустая гистограмма)")]
    public void SumEmpty()
    {
        var histogram = new uint[256];
        var sum = SimdHistogram.Sum(histogram);

        Assert.That(sum, Is.Zero);
    }

    [TestCase(TestName = "SimdHistogram: сумма (все единицы)")]
    public void SumAllOnes()
    {
        var histogram = new uint[256];
        Array.Fill(histogram, 1u);

        var sum = SimdHistogram.Sum(histogram);

        Assert.That(sum, Is.EqualTo(256UL));
    }

    [TestCase(TestName = "SimdHistogram: сумма (большие значения без переполнения)")]
    public void SumLargeValues()
    {
        var histogram = new uint[256];
        Array.Fill(histogram, uint.MaxValue / 256);

        var sum = SimdHistogram.Sum(histogram);
        var expected = (ulong)(uint.MaxValue / 256) * 256;

        Assert.That(sum, Is.EqualTo(expected));
    }

    [TestCase(TestName = "SimdHistogram: сумма (случайные данные)")]
    public void SumRandom()
    {
        var histogram = new uint[256];
        var random = new Random(42);
        var expected = 0UL;

        for (var i = 0; i < 256; i++)
        {
            histogram[i] = (uint)random.Next(0, 100000);
            expected += histogram[i];
        }

        var sum = SimdHistogram.Sum(histogram);

        Assert.That(sum, Is.EqualTo(expected));
    }

    #endregion

    #region Edge Cases

    [TestCase(TestName = "SimdHistogram: исключение при маленькой гистограмме")]
    public void ThrowsOnSmallHistogram()
    {
        var histogram = new uint[100];

        Assert.Throws<ArgumentException>(() => SimdHistogram.ComputeHistogram([1, 2, 3], histogram));
    }

    [TestCase(TestName = "SimdHistogram: граничный размер (255 байт — скалярный путь)")]
    public void BoundaryScalarPath()
    {
        var data = new byte[255];
        for (var i = 0; i < data.Length; i++)
            data[i] = (byte)i;

        var expected = new uint[256];
        foreach (var b in data)
            expected[b]++;

        var actual = new uint[256];
        SimdHistogram.ComputeHistogram(data, actual);

        Assert.That(actual, Is.EqualTo(expected));
    }

    [TestCase(TestName = "SimdHistogram: граничный размер (256 байт — SIMD путь)")]
    public void BoundarySimdPath()
    {
        var data = new byte[256];
        for (var i = 0; i < data.Length; i++)
            data[i] = (byte)i;

        var expected = new uint[256];
        foreach (var b in data)
            expected[b]++;

        var actual = new uint[256];
        SimdHistogram.ComputeHistogram(data, actual);

        Assert.That(actual, Is.EqualTo(expected));
    }

    #endregion
}
