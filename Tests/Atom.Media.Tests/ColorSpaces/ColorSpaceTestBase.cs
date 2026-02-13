#pragma warning disable CA1000, CA1707, CA1819, MA0048, NUnit2007, NUnit2045, S2368, S4144

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Media.ColorSpaces.Tests;

/// <summary>
/// Базовый абстрактный класс для тестов конвертации между цветовыми пространствами.
/// Использует IColorSpace.From/To для автоматической диспетчеризации конвертаций.
/// </summary>
/// <typeparam name="TSource">Исходное цветовое пространство.</typeparam>
/// <typeparam name="TTarget">Целевое цветовое пространство.</typeparam>
[TestFixture]
public abstract class ColorSpaceTestBase<TSource, TTarget>
    where TSource : unmanaged, IColorSpace<TSource>
    where TTarget : unmanaged, IColorSpace<TTarget>
{
    #region Constants

    /// <summary>Количество прогревов перед замером производительности.</summary>
    protected const int WarmupIterations = 20;

    /// <summary>Количество итераций для замера производительности.</summary>
    protected const int BenchmarkIterations = 100;

    /// <summary>Количество первых замеров, исключаемых из расчёта среднего ("холодные" запуски).</summary>
    protected const int WarmupMeasurements = 10;

    /// <summary>
    /// SSE должен быть не медленнее Scalar.
    /// Правильная SIMD реализация всегда быстрее или равна.
    /// </summary>
    private const double MinSseVsScalar = 1.0;

    /// <summary>
    /// AVX2 должен быть не медленнее SSE.
    /// 256-bit должен быть >= 128-bit.
    /// </summary>
    private const double MinAvx2VsSse = 1.0;

    /// <summary>
    /// Forward и Backward не должны отличаться более чем в 2x.
    /// Симметричные операции должны быть сбалансированы.
    /// </summary>
    private const double MaxForwardBackwardRatio = 2.0;

    #endregion

    #region Abstract Properties

    /// <summary>Имя пары конвертации (например, "Rgb24 ↔ Rgba32").</summary>
    protected abstract string PairName { get; }

    /// <summary>Ожидаемая точность round-trip (0 = lossless).</summary>
    protected virtual int RoundTripTolerance { get; }

    /// <summary>
    /// Максимальное соотношение Forward/Backward.
    /// Для асимметричных операций (например Gray8↔RGBA) можно увеличить.
    /// По умолчанию: 1.5 (50% разница).
    /// </summary>
    protected virtual double ForwardBackwardRatioLimit { get; } = MaxForwardBackwardRatio;

    /// <summary>Реализованные ускорители для этой пары.</summary>
    protected virtual HardwareAcceleration ImplementedAccelerations { get; } = HardwareAcceleration.None
        | HardwareAcceleration.Sse41
        | HardwareAcceleration.Avx2;

    /// <summary>Эталонные значения для проверки корректности.</summary>
    protected abstract (TSource Source, TTarget Target, int Tolerance)[] ReferenceValues { get; }

    #endregion

    #region Virtual Methods - Conversion (using IColorSpace)

    /// <summary>Пакетная конвертация Source → Target.</summary>
    protected virtual void ConvertForward(ReadOnlySpan<TSource> source, Span<TTarget> destination)
        => TTarget.From(source, destination);

    /// <summary>Пакетная конвертация Target → Source.</summary>
    protected virtual void ConvertBackward(ReadOnlySpan<TTarget> source, Span<TSource> destination)
        => TSource.From(source, destination);

    /// <summary>Пакетная конвертация Source → Target с явным ускорителем.</summary>
    protected virtual void ConvertForward(ReadOnlySpan<TSource> source, Span<TTarget> destination, HardwareAcceleration acceleration)
        => TTarget.From(source, destination, acceleration);

    /// <summary>Пакетная конвертация Target → Source с явным ускорителем.</summary>
    protected virtual void ConvertBackward(ReadOnlySpan<TTarget> source, Span<TSource> destination, HardwareAcceleration acceleration)
        => TSource.From(source, destination, acceleration);

    /// <summary>Конвертация одного пикселя Source → Target.</summary>
    protected virtual TTarget ConvertSingle(TSource source)
        => TTarget.From(source);

    /// <summary>Конвертация одного пикселя Target → Source.</summary>
    protected virtual TSource ConvertSingleBack(TTarget target)
        => TSource.From(target);

    #endregion

    #region Abstract Methods - Comparison

    /// <summary>Сравнение двух значений TTarget с допуском.</summary>
    protected abstract bool EqualsTarget(TTarget a, TTarget b, int tolerance);

    /// <summary>Сравнение двух значений TSource с допуском.</summary>
    protected abstract bool EqualsSource(TSource a, TSource b, int tolerance);

    /// <summary>Получение максимальной покомпонентной ошибки.</summary>
    protected abstract int GetMaxComponentError(TSource a, TSource b);

    #endregion

    #region Lazy Test Data Cache

    private static TSource[]? _cachedSourceData;
    private static TTarget[]? _cachedTargetData;
    private static readonly Lock _cacheLock = new();

    /// <summary>Lazy-кешированные данные Source.</summary>
    protected TSource[] CachedSourceData
    {
        get
        {
            if (_cachedSourceData is null)
            {
                lock (_cacheLock)
                    _cachedSourceData ??= GenerateExhaustiveData<TSource>();
            }
            return _cachedSourceData;
        }
    }

    /// <summary>Lazy-кешированные данные Target.</summary>
    protected TTarget[] CachedTargetData
    {
        get
        {
            if (_cachedTargetData is null)
            {
                lock (_cacheLock)
                    _cachedTargetData ??= GenerateExhaustiveData<TTarget>();
            }
            return _cachedTargetData;
        }
    }

    #endregion

    #region Resolution Presets

    /// <summary>Стандартные разрешения для тестов производительности.</summary>
    protected static readonly (string Name, int Width, int Height)[] Resolutions =
    [
        ("480p", 640, 480),
        ("720p", 1280, 720),
        ("1080p", 1920, 1080),
        ("2K", 2560, 1440),
        ("4K", 3840, 2160),
        ("8K", 7680, 4320),
    ];

    /// <summary>Разрешение 1080p для основных бенчмарков.</summary>
    protected static readonly int Resolution1080p = 1920 * 1080;

    #endregion

    #region Test: Reference Values Correctness

    /// <summary>
    /// Тест: Проверка эталонных значений конвертации.
    /// </summary>
    [Test, Order(1)]
    public void ReferenceValuesCorrectness()
    {
        TestContext.Out.WriteLine($"═══ {PairName}: Проверка эталонных значений ═══");

        var passed = 0;
        var failed = 0;

        foreach (var (source, expectedTarget, tolerance) in ReferenceValues)
        {
            var actualTarget = ConvertSingle(source);
            var match = EqualsTarget(actualTarget, expectedTarget, tolerance);

            if (match)
            {
                passed++;
                TestContext.Out.WriteLine($"  ✓ {source} → {actualTarget}");
            }
            else
            {
                failed++;
                TestContext.Out.WriteLine($"  ✗ {source} → {actualTarget} (ожидалось: {expectedTarget})");
            }
        }

        TestContext.Out.WriteLine($"  Результат: {passed}/{passed + failed} эталонных значений корректны");
        Assert.That(failed, Is.Zero, $"Некорректные эталонные значения: {failed}");
    }

    #endregion

    #region Test: Accelerator Correctness

    /// <summary>
    /// Тест: Проверка корректности всех ускорителей.
    /// </summary>
    [Test, Order(2)]
    public void AcceleratorCorrectness()
    {
        TestContext.Out.WriteLine($"═══ {PairName}: Проверка корректности ускорителей ═══");

        var accelerators = new[]
        {
            (Name: "Scalar", Accel: HardwareAcceleration.None),
            (Name: "SSE4.1", Accel: HardwareAcceleration.Sse41),
            (Name: "AVX2", Accel: HardwareAcceleration.Avx2),
            (Name: "AVX-512", Accel: HardwareAcceleration.Avx512BW),
        };

        // Используем данные для 720p для баланса скорости/покрытия
        var testSize = 1280 * 720;
        var sourceData = CachedSourceData.AsSpan(0, Math.Min(testSize, CachedSourceData.Length)).ToArray();

        var referenceResult = new TTarget[sourceData.Length];
        ConvertForward(sourceData, referenceResult, HardwareAcceleration.None);

        foreach (var (name, accel) in accelerators)
        {
            var isImplemented = (ImplementedAccelerations & accel) != 0;

            if (!isImplemented && accel != HardwareAcceleration.None)
                continue; // Пропускаем нереализованные

            if (!HardwareAccelerationInfo.IsSupported(accel))
            {
                TestContext.Out.WriteLine($"  ⊘ {name}: не поддерживается на этом CPU");
                continue;
            }

            var result = new TTarget[sourceData.Length];
            ConvertForward(sourceData, result, accel);

            var errors = CountMismatches(referenceResult, result, 0);

            if (errors == 0)
            {
                TestContext.Out.WriteLine($"  ✓ {name}: все {sourceData.Length:N0} пикселей корректны");
            }
            else
            {
                TestContext.Out.WriteLine($"  ✗ {name}: {errors:N0} ошибок из {sourceData.Length:N0}");
                Assert.Fail($"{name} имеет {errors} ошибок конвертации");
            }
        }
    }

    #endregion

    #region Test: Round-Trip Correctness

    /// <summary>
    /// Тест: Проверка round-trip (Source → Target → Source).
    /// </summary>
    [Test, Order(3)]
    public void RoundTripCorrectness()
    {
        TestContext.Out.WriteLine($"═══ {PairName}: Проверка round-trip ═══");

        var sourceData = CachedSourceData;
        var intermediate = new TTarget[sourceData.Length];
        var result = new TSource[sourceData.Length];

        ConvertForward(sourceData, intermediate);
        ConvertBackward(intermediate, result);

        var errors = 0;
        var maxError = 0;

        for (var i = 0; i < sourceData.Length; i++)
        {
            if (!EqualsSource(sourceData[i], result[i], RoundTripTolerance))
            {
                errors++;
                var componentError = GetMaxComponentError(sourceData[i], result[i]);
                maxError = Math.Max(maxError, componentError);
            }
        }

        if (errors == 0)
        {
            TestContext.Out.WriteLine($"  ✓ Round-trip: все {sourceData.Length:N0} значений корректны (tolerance={RoundTripTolerance})");
        }
        else
        {
            var errorRate = (double)errors / sourceData.Length * 100;
            TestContext.Out.WriteLine($"  ✗ Round-trip: {errors:N0} ошибок ({errorRate:F2}%), max error={maxError}");
        }

        Assert.That(errors, Is.Zero, $"Round-trip ошибки: {errors}");
    }

    #endregion

    #region Test: Accelerator Accuracy Analysis

    /// <summary>
    /// Тест: Анализ точности каждого ускорителя отдельно.
    /// Выявляет, какой ускоритель менее точен.
    /// </summary>
    [Test, Order(4)]
    public void AcceleratorAccuracyAnalysis()
    {
        TestContext.Out.WriteLine($"═══ {PairName}: Анализ точности ускорителей ═══");

        var accelerators = new[]
        {
            (Name: "Scalar", Accel: HardwareAcceleration.None),
            (Name: "SSE4.1", Accel: HardwareAcceleration.Sse41),
            (Name: "AVX2", Accel: HardwareAcceleration.Avx2),
            (Name: "AVX-512", Accel: HardwareAcceleration.Avx512BW),
        };

        var sourceData = CachedSourceData;
        var results = new List<(string Name, int Errors, int MaxError, double ErrorRate)>();

        // Получаем эталонный результат от Scalar
        var intermediateRef = new TTarget[sourceData.Length];
        var resultRef = new TSource[sourceData.Length];
        ConvertForward(sourceData, intermediateRef, HardwareAcceleration.None);
        ConvertBackward(intermediateRef, resultRef, HardwareAcceleration.None);

        TestContext.Out.WriteLine();
        TestContext.Out.WriteLine($"{"Ускоритель",-12} {"Forward",-10} {"Backward",-10} {"RoundTrip",-12} {"Max Err",-8} {"Неточных",-12} {"Брак(>tol)",-12}");
        TestContext.Out.WriteLine(new string('─', 84));

        foreach (var (name, accel) in accelerators)
        {
            var isImplemented = (ImplementedAccelerations & accel) != 0 || accel == HardwareAcceleration.None;

            if (!isImplemented)
                continue;

            if (!HardwareAccelerationInfo.IsSupported(accel))
            {
                TestContext.Out.WriteLine($"{name,-12} {"N/A",-10} {"N/A",-10} {"N/A",-12} {"N/A",-8} {"N/A",-12} {"N/A",-12}");
                continue;
            }

            // Forward: Source → Target
            var intermediate = new TTarget[sourceData.Length];
            ConvertForward(sourceData, intermediate, accel);

            var forwardErrors = CountMismatches(intermediateRef, intermediate, 0);
            var forwardStatus = forwardErrors == 0 ? "✓ OK" : $"✗ {forwardErrors}";

            // Backward: Target → Source (используем эталонный intermediate)
            var backResult = new TSource[sourceData.Length];
            ConvertBackward(intermediateRef, backResult, accel);

            var backwardErrors = 0;
            for (var i = 0; i < sourceData.Length; i++)
            {
                if (!EqualsSource(resultRef[i], backResult[i], 0))
                    backwardErrors++;
            }
            var backwardStatus = backwardErrors == 0 ? "✓ OK" : $"✗ {backwardErrors}";

            // Round-trip: Source → Target → Source (полный цикл через этот ускоритель)
            var rtIntermediate = new TTarget[sourceData.Length];
            var rtResult = new TSource[sourceData.Length];
            ConvertForward(sourceData, rtIntermediate, accel);
            ConvertBackward(rtIntermediate, rtResult, accel);

            var rtErrors = 0;       // Ошибки превышающие tolerance (брак)
            var rtMaxError = 0;     // Максимальная ошибка (включая в пределах tolerance)
            var rtNonZeroErrors = 0; // Любые ненулевые ошибки (неточные пиксели)

            for (var i = 0; i < sourceData.Length; i++)
            {
                var err = GetMaxComponentError(sourceData[i], rtResult[i]);
                rtMaxError = Math.Max(rtMaxError, err);

                if (err > 0) rtNonZeroErrors++;
                if (err > RoundTripTolerance) rtErrors++;
            }

            // Статус показывает ошибки превышающие tolerance
            var rtStatus = rtErrors == 0 ? "✓ OK" : $"✗ {rtErrors}";
            var nonZeroRate = (double)rtNonZeroErrors / sourceData.Length * 100;
            var errorRate = (double)rtErrors / sourceData.Length * 100;

            results.Add((name, rtErrors, rtMaxError, errorRate));

            // Неточных = сколько пикселей с ненулевой ошибкой (даже ±1)
            // Брак = сколько превысили tolerance (реальные ошибки)
            var nonZeroStr = $"{rtNonZeroErrors:N0} ({nonZeroRate:F1}%)";
            var errorStr = rtErrors > 0 ? $"{rtErrors:N0} ({errorRate:F2}%)" : "0";

            TestContext.Out.WriteLine(
                $"{name,-12} {forwardStatus,-10} {backwardStatus,-10} {rtStatus,-12} {rtMaxError,-8} {nonZeroStr,-12} {errorStr,-12}");
        }

        TestContext.Out.WriteLine();

        // Проверяем что все ускорители имеют одинаковую точность
        var (Name, Errors, MaxError, ErrorRate) = results.FirstOrDefault(r => r.Name == "Scalar");
        var hasAccuracyRegression = false;

        foreach (var result in results.Where(r => r.Name != "Scalar"))
        {
            if (result.Errors > Errors)
            {
                TestContext.Out.WriteLine($"  ⚠ {result.Name} менее точен чем Scalar: {result.Errors} vs {Errors} ошибок");
                hasAccuracyRegression = true;
            }
            else if (result.MaxError > MaxError && result.MaxError > RoundTripTolerance)
            {
                TestContext.Out.WriteLine($"  ⚠ {result.Name} имеет большую макс. ошибку: {result.MaxError} vs {MaxError}");
                hasAccuracyRegression = true;
            }
        }

        if (!hasAccuracyRegression)
        {
            TestContext.Out.WriteLine($"  ✓ Все ускорители имеют одинаковую точность");
        }

        // Fail если какой-то ускоритель хуже Scalar
        Assert.That(hasAccuracyRegression, Is.False,
            "Обнаружена регрессия точности: некоторые SIMD ускорители менее точны чем Scalar");
    }

    #endregion

    #region Test: Accelerator Performance Comparison

    /// <summary>
    /// Тест: Сравнение производительности ускорителей (1080p).
    /// Жёсткие требования — тест падает если:
    /// - SSE4.1 медленнее Scalar (speedup &lt; 1.0x)
    /// - AVX2 медленнее SSE4.1 (speedup &lt; 1.0x)
    /// - Forward и Backward отличаются более чем на 50% (ratio &gt; 1.5x) для ЛЮБОГО ускорителя
    /// </summary>
    [Test, Order(10)]
    public void AcceleratorPerformanceComparison()
    {
        TestContext.Out.WriteLine($"═══ {PairName}: Производительность ускорителей (1080p) ═══");

        var pixelCount = Resolution1080p;
        var sourceData = CreateTestBuffer<TSource>(pixelCount);
        var targetData = CreateTestBuffer<TTarget>(pixelCount);

        var results = new List<(string Name, HardwareAcceleration Accel, double ForwardMs, double BackwardMs, double TotalMs)>();

        var accelerators = new[]
        {
            (Name: "Scalar", Accel: HardwareAcceleration.None),
            (Name: "SSE4.1", Accel: HardwareAcceleration.Sse41),
            (Name: "AVX2", Accel: HardwareAcceleration.Avx2),
            (Name: "AVX-512", Accel: HardwareAcceleration.Avx512BW),
        };

        foreach (var (name, accel) in accelerators)
        {
            if (!HardwareAccelerationInfo.IsSupported(accel))
                continue;

            if ((ImplementedAccelerations & accel) == 0 && accel != HardwareAcceleration.None)
                continue;

            // Прогрев
            for (var w = 0; w < WarmupIterations; w++)
            {
                ConvertForward(sourceData, targetData, accel);
                ConvertBackward(targetData, sourceData, accel);
            }

            // Замер Forward — собираем время каждой итерации
            var forwardTimes = new double[BenchmarkIterations];
            for (var i = 0; i < BenchmarkIterations; i++)
            {
                var sw = Stopwatch.StartNew();
                ConvertForward(sourceData, targetData, accel);
                sw.Stop();
                forwardTimes[i] = sw.Elapsed.TotalMilliseconds;
            }

            // Замер Backward — собираем время каждой итерации
            var backwardTimes = new double[BenchmarkIterations];
            for (var i = 0; i < BenchmarkIterations; i++)
            {
                var sw = Stopwatch.StartNew();
                ConvertBackward(targetData, sourceData, accel);
                sw.Stop();
                backwardTimes[i] = sw.Elapsed.TotalMilliseconds;
            }

            // Вычисляем среднее, исключая первые WarmupMeasurements "холодных" замеров
            var forwardMs = forwardTimes.Skip(WarmupMeasurements).Average();
            var backwardMs = backwardTimes.Skip(WarmupMeasurements).Average();

            results.Add((name, accel, forwardMs, backwardMs, forwardMs + backwardMs));
        }

        // Вывод таблицы
        PrintPerformanceTable(results, pixelCount);

        // === Проверки регрессий ===
        var failures = new List<string>();

        var scalarResult = results.FirstOrDefault(r => r.Accel == HardwareAcceleration.None);
        var sseResult = results.FirstOrDefault(r => r.Accel == HardwareAcceleration.Sse41);
        var avx2Result = results.FirstOrDefault(r => r.Accel == HardwareAcceleration.Avx2);

        // 1. Проверка SSE vs Scalar (жёсткое требование: SSE ≥ Scalar)
        if (scalarResult != default && sseResult != default)
        {
            var sseSpeedup = scalarResult.TotalMs / sseResult.TotalMs;

            if (sseSpeedup < MinSseVsScalar)
            {
                failures.Add(
                    $"SSE4.1 регрессия: {sseSpeedup:F2}x " +
                    $"(ожидалось ≥{MinSseVsScalar:F2}x, Scalar={scalarResult.TotalMs:F2}ms, SSE={sseResult.TotalMs:F2}ms)");
            }
            else
            {
                TestContext.Out.WriteLine($"  ✓ SSE4.1 vs Scalar: {sseSpeedup:F2}x (мин. {MinSseVsScalar:F2}x)");
            }
        }

        // 2. Проверка AVX2 vs SSE (жёсткое требование: AVX2 ≥ SSE)
        if (sseResult != default && avx2Result != default)
        {
            var avx2Speedup = sseResult.TotalMs / avx2Result.TotalMs;

            if (avx2Speedup < MinAvx2VsSse)
            {
                failures.Add(
                    $"AVX2 регрессия vs SSE: {avx2Speedup:F2}x " +
                    $"(ожидалось ≥{MinAvx2VsSse:F2}x, SSE={sseResult.TotalMs:F2}ms, AVX2={avx2Result.TotalMs:F2}ms)");
            }
            else
            {
                TestContext.Out.WriteLine($"  ✓ AVX2 vs SSE4.1: {avx2Speedup:F2}x (мин. {MinAvx2VsSse:F2}x)");
            }
        }

        // 3. Проверка баланса Forward/Backward для ВСЕХ ускорителей (жёсткое требование: ≤50% разница)
        var fbLimit = ForwardBackwardRatioLimit;

        foreach (var (name, _, forward, backward, _) in results)
        {
            var ratio = Math.Max(forward, backward) / Math.Min(forward, backward);

            if (ratio > fbLimit)
            {
                var slower = forward > backward ? "Forward" : "Backward";
                failures.Add(
                    $"{name} дисбаланс направлений: {slower} в {ratio:F2}x медленнее " +
                    $"(макс. {fbLimit:F2}x, Forward={forward:F2}ms, Backward={backward:F2}ms)");
            }
            else
            {
                TestContext.Out.WriteLine($"  ✓ {name} F/B баланс: {ratio:F2}x (макс. {fbLimit:F2}x)");
            }
        }

        // Fail если есть регрессии
        if (failures.Count > 0)
        {
            TestContext.Out.WriteLine();
            TestContext.Out.WriteLine("═══ РЕГРЕССИИ ═══");
            foreach (var failure in failures)
            {
                TestContext.Out.WriteLine($"  ✗ {failure}");
            }

            Assert.Fail($"Обнаружено {failures.Count} регрессий производительности:\n" +
                string.Join("\n", failures.Select(f => $"  • {f}")));
        }
    }

    #endregion

    #region Test: Resolution Performance Matrix

    /// <summary>
    /// Тест: Матрица производительности по разрешениям.
    /// </summary>
    [Test, Order(11)]
    public void ResolutionPerformanceMatrix()
    {
        TestContext.Out.WriteLine($"═══ {PairName}: Матрица производительности по разрешениям ═══");

        var accelerators = new[]
        {
            (Name: "Scalar", Accel: HardwareAcceleration.None),
            (Name: "SSE4.1", Accel: HardwareAcceleration.Sse41),
            (Name: "AVX2", Accel: HardwareAcceleration.Avx2),
        };

        // Фильтруем доступные ускорители
        var availableAccels = accelerators
            .Where(a => HardwareAccelerationInfo.IsSupported(a.Accel))
            .Where(a => a.Accel == HardwareAcceleration.None || (ImplementedAccelerations & a.Accel) != 0)
            .ToArray();

        TestContext.Out.WriteLine();
        TestContext.Out.WriteLine($"{"Разрешение",-12} {"Пикселей",12} │ {string.Join(" │ ", availableAccels.Select(a => $"{a.Name + " (ms)",12} {"FPS",8}"))}");
        TestContext.Out.WriteLine(new string('─', 28 + (availableAccels.Length * 24)));

        foreach (var (resName, width, height) in Resolutions)
        {
            var pixelCount = width * height;
            var sourceData = CreateTestBuffer<TSource>(pixelCount);
            var targetData = CreateTestBuffer<TTarget>(pixelCount);

            var times = new List<string>();

            foreach (var (_, accel) in availableAccels)
            {
                // Прогрев
                for (var w = 0; w < WarmupIterations; w++)
                {
                    ConvertForward(sourceData, targetData, accel);
                    ConvertBackward(targetData, sourceData, accel);
                }

                // Замер полного цикла — собираем время каждой итерации
                var iterationTimes = new double[BenchmarkIterations];
                for (var i = 0; i < BenchmarkIterations; i++)
                {
                    var sw = Stopwatch.StartNew();
                    ConvertForward(sourceData, targetData, accel);
                    ConvertBackward(targetData, sourceData, accel);
                    sw.Stop();
                    iterationTimes[i] = sw.Elapsed.TotalMilliseconds;
                }

                // Среднее, исключая первые WarmupMeasurements "холодных" замеров
                var totalMs = iterationTimes.Skip(WarmupMeasurements).Average();
                var fps = 1000.0 / totalMs;
                times.Add($"{totalMs,9:F2} ms {fps,8:F1}");
            }

            TestContext.Out.WriteLine($"{resName,-12} {pixelCount,12:N0} │ {string.Join(" │ ", times)}");
        }

        TestContext.Out.WriteLine();
    }

    #endregion

    #region Helper Methods

    /// <summary>Подсчёт несовпадений между двумя массивами.</summary>
    protected int CountMismatches(ReadOnlySpan<TTarget> expected, ReadOnlySpan<TTarget> actual, int tolerance)
    {
        var count = 0;
        for (var i = 0; i < expected.Length; i++)
        {
            if (!EqualsTarget(expected[i], actual[i], tolerance))
                count++;
        }
        return count;
    }

    /// <summary>Вывод таблицы производительности.</summary>
    private static void PrintPerformanceTable(List<(string Name, HardwareAcceleration Accel, double ForwardMs, double BackwardMs, double TotalMs)> results, int pixelCount)
    {
        TestContext.Out.WriteLine();
        TestContext.Out.WriteLine($"{"Ускоритель",-12} {"Forward",12} {"Backward",12} {"Total",12} {"Speedup",10} {"MPix/s",12} {"FPS",10}");
        TestContext.Out.WriteLine(new string('─', 84));

        var scalarTotal = results.FirstOrDefault(r => r.Accel == HardwareAcceleration.None).TotalMs;

        foreach (var (name, _, forward, backward, total) in results)
        {
            var speedup = scalarTotal > 0 ? scalarTotal / total : 1.0;
            var mpixPerSec = pixelCount / (total / 1000.0) / 1_000_000;
            var fps = 1000.0 / total;

            TestContext.Out.WriteLine($"{name,-12} {forward,9:F2} ms {backward,9:F2} ms {total,9:F2} ms {speedup,9:F2}x {mpixPerSec,9:F1} MP/s {fps,9:F1}");
        }

        TestContext.Out.WriteLine();
    }

    /// <summary>Создание тестового буфера заданного размера.</summary>
    protected static T[] CreateTestBuffer<T>(int size) where T : unmanaged
    {
        var buffer = new T[size];
        // Заполняем паттерном для детерминированности
        var bytes = MemoryMarshal.AsBytes(buffer.AsSpan());
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)(i & 0xFF);
        return buffer;
    }

    /// <summary>Генерация полного набора значений для N-компонентного типа.</summary>
    private static T[] GenerateExhaustiveData<T>() where T : unmanaged
    {
        var size = Unsafe.SizeOf<T>();

        return size switch
        {
            1 => GenerateExhaustive1Component<T>(),
            3 => GenerateExhaustive3Component<T>(),
            4 => GenerateExhaustive4Component<T>(),
            _ => GenerateRandom<T>(256 * 256 * 256),
        };
    }

    private static T[] GenerateExhaustive1Component<T>() where T : unmanaged
    {
        var result = new T[256];
        var bytes = MemoryMarshal.AsBytes(result.AsSpan());
        for (var i = 0; i < 256; i++)
            bytes[i] = (byte)i;
        return result;
    }

    private static T[] GenerateExhaustive3Component<T>() where T : unmanaged
    {
        // 16M значений (256^3)
        var count = 256 * 256 * 256;
        var result = new T[count];
        var bytes = MemoryMarshal.AsBytes(result.AsSpan());

        var idx = 0;
        for (var r = 0; r < 256; r++)
        {
            for (var g = 0; g < 256; g++)
            {
                for (var b = 0; b < 256; b++)
                {
                    bytes[idx++] = (byte)r;
                    bytes[idx++] = (byte)g;
                    bytes[idx++] = (byte)b;
                }
            }
        }

        return result;
    }

    private static T[] GenerateExhaustive4Component<T>() where T : unmanaged
    {
        // 16M значений с фиксированным alpha=255
        var count = 256 * 256 * 256;
        var result = new T[count];
        var bytes = MemoryMarshal.AsBytes(result.AsSpan());

        var idx = 0;
        for (var r = 0; r < 256; r++)
        {
            for (var g = 0; g < 256; g++)
            {
                for (var b = 0; b < 256; b++)
                {
                    bytes[idx++] = (byte)r;
                    bytes[idx++] = (byte)g;
                    bytes[idx++] = (byte)b;
                    bytes[idx++] = 255;
                }
            }
        }

        return result;
    }

    private static T[] GenerateRandom<T>(int count) where T : unmanaged
    {
        var result = new T[count];
        var bytes = MemoryMarshal.AsBytes(result.AsSpan());
        Random.Shared.NextBytes(bytes);
        return result;
    }

    /// <summary>Сравнение компонент с допуском.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static bool ComponentEquals(byte a, byte b, int tolerance)
        => Math.Abs(a - b) <= tolerance;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static bool ComponentEquals(int a, int b, int tolerance)
        => Math.Abs(a - b) <= tolerance;

    #endregion
}
