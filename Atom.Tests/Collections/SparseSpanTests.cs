using Atom.Buffers;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Loggers;

namespace Atom.Collections.Tests;

public class SparseSpanTests(ILogger logger) : BenchmarkTest<SparseSpanTests>(logger)
{
    public override bool IsBenchmarkDisabled => true;
    
    public SparseSpanTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Тест перебора через foreach"), Benchmark(Description = "foreach", Baseline = true)]
    public void ForEachTest()
    {
        var array = new SparseSpan<int>(ushort.MaxValue + 1);

        array[0] = 1;
        array[100] = 2;
        array[0] = 3;

        array.AddOrUpdate(67, -22);

        var count = 0;

        foreach (var item in array)
        {
            if (item is not 3 and not 2 and not -22)
                throw new InvalidOperationException();

            ++count;
        }

        array.Release();

        if (IsTest) Assert.That(count, Is.EqualTo(3));
    }

    [TestCase(TestName = "Тест перебора через foreach (SpanPool)"), Benchmark(Description = "foreach (SpanPool)")]
    public void ForEachSpanPoolTest()
    {
        var span = SpanPool<int>.Shared.Rent(ushort.MaxValue + 1);
        SparseSpan<int> array = span;

        array[0] = 1;
        array[100] = 2;
        array[0] = 3;

        array.AddOrUpdate(67, -22);

        var count = 0;

        foreach (var item in array)
        {
            if (item is not 3 and not 2 and not -22)
                throw new InvalidOperationException();

            ++count;
        }

        array.Release();
        SpanPool<int>.Shared.Return(span);

        if (IsTest) Assert.That(count, Is.EqualTo(3));
    }

    [TestCase(TestName = "Тест перебора через for"), Benchmark(Description = "for")]
    public void ForTest()
    {
        var array = new SparseSpan<int>(ushort.MaxValue + 1);

        array[0] = 1;
        array[100] = 2;
        array[0] = 3;

        array.AddOrUpdate(67, -22);

        var indexes = array.Indexes;
        var count = 0;

        for (var i = 0; i < indexes.Length; ++i)
        {
            if (array[indexes[i]] is not 3 and not 2 and not -22)
                throw new InvalidOperationException();

            ++count;
        }

        array.Release();

        if (IsTest) Assert.That(count, Is.EqualTo(3));
    }

    [TestCase(TestName = "Тест перебора через for (SpanPool)"), Benchmark(Description = "for (SpanPool)")]
    public void ForSpanPoolTest()
    {
        var span = SpanPool<int>.Shared.Rent(ushort.MaxValue + 1);
        SparseSpan<int> array = span;

        array[0] = 1;
        array[100] = 2;
        array[0] = 3;

        array.AddOrUpdate(67, -22);

        var indexes = array.Indexes;
        var count = 0;

        for (var i = 0; i < indexes.Length; ++i)
        {
            if (array[indexes[i]] is not 3 and not 2 and not -22)
                throw new InvalidOperationException();

            ++count;
        }

        array.Release();
        SpanPool<int>.Shared.Return(span);

        if (IsTest) Assert.That(count, Is.EqualTo(3));
    }
}