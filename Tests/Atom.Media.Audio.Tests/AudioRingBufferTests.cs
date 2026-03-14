using Atom.Media.Audio;

namespace Atom.Media.Audio.Tests;

[TestFixture]
public class AudioRingBufferTests(ILogger logger) : BenchmarkTests<AudioRingBufferTests>(logger)
{
    public AudioRingBufferTests() : this(ConsoleLogger.Unicode) { }

    // ═══════════════════════════════════════════════════════════════
    // Конструктор и ёмкость
    // ═══════════════════════════════════════════════════════════════

    [TestCase(TestName = "Кольцевой буфер: ёмкость округляется до степени двойки")]
    public void CapacityRoundsUpToPowerOfTwo()
    {
        var buffer = new AudioRingBuffer(1000);
        Assert.That(buffer.Capacity, Is.EqualTo(1024));
    }

    [TestCase(TestName = "Кольцевой буфер: ёмкость степени двойки не меняется")]
    public void CapacityExactPowerOfTwo()
    {
        var buffer = new AudioRingBuffer(512);
        Assert.That(buffer.Capacity, Is.EqualTo(512));
    }

    [TestCase(TestName = "Кольцевой буфер: ёмкость 1 → 1")]
    public void CapacityMinimum()
    {
        var buffer = new AudioRingBuffer(1);
        Assert.That(buffer.Capacity, Is.EqualTo(1));
    }

    [TestCase(TestName = "Кольцевой буфер: ёмкость 0 выбрасывает исключение")]
    public void CapacityZeroThrows()
    {
        Assert.That(() => new AudioRingBuffer(0), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    // ═══════════════════════════════════════════════════════════════
    // Запись и чтение
    // ═══════════════════════════════════════════════════════════════

    [TestCase(TestName = "Кольцевой буфер: запись и чтение базовые")]
    public void WriteReadBasic()
    {
        var buffer = new AudioRingBuffer(16);
        byte[] data = [1, 2, 3, 4, 5];

        var written = buffer.Write(data);
        Assert.That(written, Is.EqualTo(5));
        Assert.That(buffer.AvailableRead, Is.EqualTo(5));

        var output = new byte[5];
        var read = buffer.Read(output);
        Assert.That(read, Is.EqualTo(5));
        Assert.That(output, Is.EqualTo(data));
    }

    [TestCase(TestName = "Кольцевой буфер: чтение из пустого возвращает 0")]
    public void ReadEmptyReturnsZero()
    {
        var buffer = new AudioRingBuffer(16);
        var output = new byte[8];

        var read = buffer.Read(output);
        Assert.That(read, Is.Zero);
    }

    [TestCase(TestName = "Кольцевой буфер: запись больше ёмкости обрезается")]
    public void WriteOverCapacityTruncates()
    {
        var buffer = new AudioRingBuffer(4);
        var data = new byte[10];
        for (var i = 0; i < 10; i++) data[i] = (byte)(i + 1);

        var written = buffer.Write(data);
        Assert.That(written, Is.EqualTo(buffer.Capacity));
    }

    [TestCase(TestName = "Кольцевой буфер: AvailableRead и AvailableWrite")]
    public void AvailableReadWrite()
    {
        var buffer = new AudioRingBuffer(16);

        Assert.That(buffer.AvailableRead, Is.Zero);
        Assert.That(buffer.AvailableWrite, Is.EqualTo(16));

        buffer.Write(new byte[6]);

        Assert.That(buffer.AvailableRead, Is.EqualTo(6));
        Assert.That(buffer.AvailableWrite, Is.EqualTo(10));
    }

    // ═══════════════════════════════════════════════════════════════
    // Wrap-around
    // ═══════════════════════════════════════════════════════════════

    [TestCase(TestName = "Кольцевой буфер: wrap-around корректно читает данные")]
    public void WrapAroundWorks()
    {
        var buffer = new AudioRingBuffer(8);

        // Заполняем 6 байт, читаем 6
        buffer.Write(new byte[6]);
        buffer.Read(new byte[6]);

        // Теперь head=6, tail=6. Пишем 5 байт — оборачивается
        byte[] data = [10, 20, 30, 40, 50];
        buffer.Write(data);

        var output = new byte[5];
        buffer.Read(output);
        Assert.That(output, Is.EqualTo(data));
    }

    [TestCase(TestName = "Кольцевой буфер: множественная запись/чтение")]
    public void MultipleWriteRead()
    {
        var buffer = new AudioRingBuffer(16);

        for (var round = 0; round < 20; round++)
        {
            byte[] data = [(byte)round, (byte)(round + 1), (byte)(round + 2)];
            buffer.Write(data);

            var output = new byte[3];
            buffer.Read(output);
            Assert.That(output, Is.EqualTo(data));
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Peek и Skip
    // ═══════════════════════════════════════════════════════════════

    [TestCase(TestName = "Кольцевой буфер: Peek не продвигает позицию")]
    public void PeekDoesNotConsume()
    {
        var buffer = new AudioRingBuffer(16);
        byte[] data = [1, 2, 3];
        buffer.Write(data);

        var peeked = new byte[3];
        buffer.Peek(peeked);

        Assert.That(buffer.AvailableRead, Is.EqualTo(3));
        Assert.That(peeked, Is.EqualTo(data));
    }

    [TestCase(TestName = "Кольцевой буфер: Skip продвигает позицию чтения")]
    public void SkipAdvancesReader()
    {
        var buffer = new AudioRingBuffer(16);
        byte[] data = [1, 2, 3, 4, 5];
        buffer.Write(data);

        var skipped = buffer.Skip(2);
        Assert.That(skipped, Is.EqualTo(2));
        Assert.That(buffer.AvailableRead, Is.EqualTo(3));

        var output = new byte[3];
        buffer.Read(output);
        Assert.That(output, Is.EqualTo(new byte[] { 3, 4, 5 }));
    }

    [TestCase(TestName = "Кольцевой буфер: Skip больше доступного обрезается")]
    public void SkipMoreThanAvailable()
    {
        var buffer = new AudioRingBuffer(16);
        buffer.Write(new byte[3]);

        var skipped = buffer.Skip(10);
        Assert.That(skipped, Is.EqualTo(3));
        Assert.That(buffer.AvailableRead, Is.Zero);
    }

    // ═══════════════════════════════════════════════════════════════
    // Clear
    // ═══════════════════════════════════════════════════════════════

    [TestCase(TestName = "Кольцевой буфер: Clear сбрасывает доступные данные")]
    public void ClearResetsAvailable()
    {
        var buffer = new AudioRingBuffer(16);
        buffer.Write(new byte[10]);

        buffer.Clear();

        Assert.That(buffer.AvailableRead, Is.Zero);
        Assert.That(buffer.AvailableWrite, Is.EqualTo(16));
    }
}
