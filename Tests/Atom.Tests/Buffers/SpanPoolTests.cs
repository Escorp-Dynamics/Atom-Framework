namespace Atom.Buffers.Tests;

public class SpanPoolTests(ILogger logger) : BenchmarkTests<SpanPoolTests>(logger)
{
    private const int BufferSize = 1024;

    public SpanPoolTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Тест аренды Span<T>"), Benchmark(Baseline = true)]
    public void RentTest()
    {
        var span = SpanPool<int>.Shared.Rent(BufferSize);
        if (!IsBenchmarkEnabled) Assert.That(span.Length, Is.EqualTo(BufferSize));

        span[..5].Fill(5);
        SpanPool<int>.Shared.Return(span);

        span = SpanPool<int>.Shared.Rent(BufferSize);

        if (!IsBenchmarkEnabled)
        {
            Assert.That(span.Length, Is.EqualTo(BufferSize));
            foreach (var i in span[..5]) Assert.That(i, Is.EqualTo(5));
        }

        SpanPool<int>.Shared.Return(span);
    }

    [TestCase(TestName = "Тест аренды Span<T> c очисткой"), Benchmark]
    public void RentWithClearingTest()
    {
        var span = SpanPool<int>.Shared.Rent(BufferSize);
        if (!IsBenchmarkEnabled) Assert.That(span.Length, Is.EqualTo(BufferSize));

        span[..5].Fill(5);
        SpanPool<int>.Shared.Return(span, true);

        span = SpanPool<int>.Shared.Rent(BufferSize);

        if (!IsBenchmarkEnabled)
        {
            Assert.That(span.Length, Is.EqualTo(BufferSize));
            foreach (var i in span[..5]) Assert.That(i, Is.EqualTo(0));
        }

        SpanPool<int>.Shared.Return(span, true);
    }
}