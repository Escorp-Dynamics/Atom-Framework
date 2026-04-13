using System.Buffers;
using System.Numerics;
using System.Runtime.InteropServices;
using Atom.Media.Audio.Backends;
using Atom.Media.Audio.Effects;

namespace Atom.Media.Audio;

/// <summary>
/// Представляет устройство виртуального микрофона.
/// Кроссплатформенно создаёт виртуальное аудиоустройство (микрофон)
/// и транслирует в него аудиопоток.
/// </summary>
/// <remarks>
/// <para>
/// Создание микрофона выполняется через фабричный метод
/// <see cref="CreateAsync(VirtualMicrophoneSettings, CancellationToken)"/>.
/// Аудио записывается через <see cref="WriteSamples(ReadOnlySpan{byte})"/>.
/// </para>
/// <para>
/// На Linux используется нативный PipeWire API — микрофон создаётся
/// как PipeWire audio source нода без root-прав.
/// </para>
/// <example>
/// <code>
/// var settings = new VirtualMicrophoneSettings { SampleRate = 48000, Channels = 1 };
/// await using var mic = await VirtualMicrophone.CreateAsync(settings);
///
/// await mic.StartCaptureAsync();
/// mic.WriteSamples(audioSamples);
/// await mic.StopCaptureAsync();
/// </code>
/// </example>
/// </remarks>
public sealed class VirtualMicrophone : IAsyncDisposable
{
    private static Func<IVirtualMicrophoneBackend>? backendFactoryOverride;
    private readonly IVirtualMicrophoneBackend backend;
    private bool isDisposed;
    private float currentLevel;
    private float peakLevel;
    private AudioRingBuffer? monitorBuffer;
    private AudioRingBuffer? latencyBuffer;

    /// <summary>
    /// Настройки микрофона.
    /// </summary>
    public VirtualMicrophoneSettings Settings { get; }

    /// <summary>
    /// Цепочка аудиоэффектов, применяемых к аудиопотоку при каждом вызове
    /// <see cref="WriteSamples(ReadOnlySpan{byte})"/>.
    /// </summary>
    public AudioEffectChain Effects { get; } = new();

    /// <summary>
    /// Измеритель пикового уровня с удержанием и затуханием.
    /// Обновляется автоматически при каждом <see cref="WriteSamples(ReadOnlySpan{byte})"/>.
    /// </summary>
    public PeakHoldMeter PeakHold { get; } = new();

    /// <summary>
    /// Текущий среднеквадратичный (RMS) уровень сигнала.
    /// Обновляется при каждом вызове <see cref="WriteSamples(ReadOnlySpan{byte})"/>.
    /// </summary>
    public float CurrentLevel => Volatile.Read(ref currentLevel);

    /// <summary>
    /// Текущий пиковый уровень сигнала.
    /// Обновляется при каждом вызове <see cref="WriteSamples(ReadOnlySpan{byte})"/>.
    /// </summary>
    public float PeakLevel => Volatile.Read(ref peakLevel);

    /// <summary>
    /// Кольцевой буфер мониторинга. <see langword="null"/>, если мониторинг не включён.
    /// Содержит последние записанные данные (после обработки эффектами).
    /// </summary>
    public AudioRingBuffer? MonitorBuffer => Volatile.Read(ref monitorBuffer);

    /// <summary>
    /// Задержка аудиопотока в миллисекундах для компенсации латентности A/V синхронизации.
    /// 0 — без задержки.
    /// </summary>
    public double LatencyCompensationMs
    {
        get;
        set
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);

            if (value <= 0)
            {
                latencyBuffer = null;
                field = 0.0;
                return;
            }

            field = value;
            InitializeLatencyBuffer(value);
        }
    }

    /// <summary>
    /// Идентификатор виртуального устройства микрофона в системе.
    /// </summary>
    public string DeviceIdentifier => backend.DeviceIdentifier;

    /// <summary>
    /// Определяет, активен ли захват аудиопотока.
    /// </summary>
    public bool IsCapturing => backend.IsCapturing;

    /// <summary>
    /// Уровень громкости (0.0 = тишина, 1.0 = максимум).
    /// </summary>
    public float Volume
    {
        get => backend.GetControl(MicrophoneControlType.Volume);
        set => backend.SetControl(MicrophoneControlType.Volume, Math.Clamp(value, 0.0f, 1.0f));
    }

    /// <summary>
    /// Определяет, отключён ли звук микрофона.
    /// </summary>
    public bool IsMuted
    {
        get => backend.GetControl(MicrophoneControlType.Mute) >= 0.5f;
        set => backend.SetControl(MicrophoneControlType.Mute, value ? 1.0f : 0.0f);
    }

    /// <summary>
    /// Усиление в децибелах. 0 dB = без изменений, -∞ dB = тишина.
    /// Диапазон: -∞ .. +20 dB.
    /// </summary>
    public double GainDb
    {
        get
        {
            var vol = backend.GetControl(MicrophoneControlType.Volume);
            return vol <= 0.0f ? double.NegativeInfinity : 20.0 * Math.Log10(vol);
        }
        set
        {
            var linear = double.IsNegativeInfinity(value)
                ? 0.0f
                : (float)Math.Clamp(Math.Pow(10.0, value / 20.0), 0.0, 10.0);
            backend.SetControl(MicrophoneControlType.Volume, linear);
        }
    }

    internal VirtualMicrophone(IVirtualMicrophoneBackend backend, VirtualMicrophoneSettings settings)
    {
        this.backend = backend;
        Settings = settings;
    }

    /// <summary>
    /// Начинает захват аудиопотока.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    public ValueTask StartCaptureAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);
        return backend.StartCaptureAsync(cancellationToken);
    }

    /// <summary>
    /// Начинает захват аудиопотока.
    /// </summary>
    public ValueTask StartCaptureAsync() => StartCaptureAsync(CancellationToken.None);

    /// <summary>
    /// Записывает сырые аудио семплы в виртуальный микрофон.
    /// Если цепочка эффектов активна, обрабатывает семплы перед отправкой.
    /// Обновляет уровни <see cref="CurrentLevel"/> и <see cref="PeakLevel"/>.
    /// </summary>
    /// <param name="sampleData">
    /// Данные семплов. Для interleaved форматов — чередующиеся каналы (L R L R).
    /// Для planar форматов — все плоскости последовательно.
    /// </param>
    public void WriteSamples(ReadOnlySpan<byte> sampleData)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);

        ReadOnlySpan<byte> processed;
        byte[]? rentedOutput = null;

        if (Effects.IsEnabled && Effects.Effects.Count > 0)
        {
            rentedOutput = ApplyEffects(sampleData, out var peak, out var rms);
            processed = rentedOutput.AsSpan(0, sampleData.Length);
            Volatile.Write(ref peakLevel, peak);
            Volatile.Write(ref currentLevel, rms);
        }
        else
        {
            processed = sampleData;
            var format = Settings.SampleFormat;
            var channels = Settings.Channels;
            Volatile.Write(ref peakLevel, AudioMeter.MeasurePeak(processed, format, channels));
            Volatile.Write(ref currentLevel, AudioMeter.MeasureRms(processed, format, channels));
        }

        PeakHold.Update(Volatile.Read(ref peakLevel));

        ReadOnlySpan<byte> output;
        byte[]? delayRented = null;

        if (latencyBuffer is { } lb)
        {
            delayRented = ArrayPool<byte>.Shared.Rent(processed.Length);
            var delayed = delayRented.AsSpan(0, processed.Length);
            lb.Write(processed);
            lb.Read(delayed);
            output = delayed;
        }
        else
        {
            output = processed;
        }

        Volatile.Read(ref monitorBuffer)?.Write(output);
        backend.WriteSamples(output);

        if (delayRented is not null)
        {
            ArrayPool<byte>.Shared.Return(delayRented);
        }

        if (rentedOutput is not null)
        {
            ArrayPool<byte>.Shared.Return(rentedOutput);
        }
    }

    /// <summary>
    /// Читает аудиоданные из WAV-файла и записывает их в виртуальный микрофон.
    /// Параметры WAV-файла (частота дискретизации, количество каналов, формат семплов)
    /// должны совпадать с настройками микрофона.
    /// </summary>
    /// <param name="path">Путь к WAV-файлу.</param>
    /// <exception cref="InvalidDataException">Файл не является валидным WAV.</exception>
    /// <exception cref="InvalidOperationException">
    /// Параметры WAV-файла не совпадают с настройками микрофона.
    /// </exception>
    public void WriteSamples(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);

        WriteSamplesCore(WavReader.Read(path));
    }

    /// <summary>
    /// Читает аудиоданные из WAV-потока и записывает их в виртуальный микрофон.
    /// Параметры WAV-данных (частота дискретизации, количество каналов, формат семплов)
    /// должны совпадать с настройками микрофона.
    /// </summary>
    /// <param name="stream">Поток с WAV-данными.</param>
    /// <exception cref="InvalidDataException">Поток не содержит валидных WAV-данных.</exception>
    /// <exception cref="InvalidOperationException">
    /// Параметры WAV-данных не совпадают с настройками микрофона.
    /// </exception>
    public void WriteSamples(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);

        WriteSamplesCore(WavReader.Read(stream));
    }

    private void WriteSamplesCore(WavReader.WavInfo wav)
    {
        if (wav.SampleRate != Settings.SampleRate)
        {
            throw new InvalidOperationException(
                "Частота WAV-файла (" + wav.SampleRate.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " Hz) не совпадает с настройками микрофона ("
                + Settings.SampleRate.ToString(System.Globalization.CultureInfo.InvariantCulture) + " Hz).");
        }

        if (wav.Channels != Settings.Channels)
        {
            throw new InvalidOperationException(
                "Количество каналов WAV-файла (" + wav.Channels.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ") не совпадает с настройками микрофона ("
                + Settings.Channels.ToString(System.Globalization.CultureInfo.InvariantCulture) + ").");
        }

        if (wav.SampleFormat != Settings.SampleFormat)
        {
            throw new InvalidOperationException(
                "Формат семплов WAV-файла (" + wav.SampleFormat
                + ") не совпадает с настройками микрофона (" + Settings.SampleFormat + ").");
        }

        WriteSamples((ReadOnlySpan<byte>)wav.Data);
    }

    /// <summary>
    /// Стримит аудио из медиафайла, покадрово декодируя и записывая в виртуальный микрофон.
    /// </summary>
    /// <param name="filePath">Путь к аудио/видеофайлу.</param>
    /// <param name="loop">Если true, воспроизведение зацикливается.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task StreamFromAsync(string filePath, bool loop = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);

        var extension = Path.GetExtension(filePath);
        var formatName = ContainerFactory.GetFormatFromExtension(extension)
            ?? throw new NotSupportedException(
                "Формат контейнера '" + extension + "' не поддерживается.");

        using var demuxer = ContainerFactory.CreateDemuxer(formatName)
            ?? throw new NotSupportedException(
                "Демуксер для формата '" + formatName + "' не зарегистрирован.");

        if (demuxer.Open(filePath) != ContainerResult.Success)
        {
            throw new InvalidOperationException(
                "Не удалось открыть файл '" + filePath + "'.");
        }

        await StreamFromCoreAsync(demuxer, loop, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Стримит аудио из потока, покадрово декодируя и записывая в виртуальный микрофон.
    /// </summary>
    /// <param name="stream">Поток с аудио/видеоданными.</param>
    /// <param name="format">Формат контейнера (например, "mp4", "ogg") или расширение (".mp3", ".flac").</param>
    /// <param name="loop">Если true, воспроизведение зацикливается.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task StreamFromAsync(Stream stream, string format, bool loop = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrEmpty(format);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);

        var formatName = (format.StartsWith('.') ? ContainerFactory.GetFormatFromExtension(format) : format)
            ?? throw new NotSupportedException(
                "Формат контейнера '" + format + "' не поддерживается.");

        using var demuxer = ContainerFactory.CreateDemuxer(formatName)
            ?? throw new NotSupportedException(
                "Демуксер для формата '" + formatName + "' не зарегистрирован.");

        if (demuxer.Open(stream) != ContainerResult.Success)
        {
            throw new InvalidOperationException("Не удалось открыть поток.");
        }

        await StreamFromCoreAsync(demuxer, loop, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Стримит аудио из URL, покадрово декодируя и записывая в виртуальный микрофон.
    /// Формат определяется по расширению URL.
    /// </summary>
    /// <param name="url">URL аудиофайла.</param>
    /// <param name="httpClient">HTTP-клиент для загрузки. Если null, создаётся временный.</param>
    /// <param name="loop">Если true, воспроизведение зацикливается.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task StreamFromAsync(
        Uri url,
        HttpClient? httpClient = null,
        bool loop = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);

        if (httpClient is not null)
        {
            await StreamFromUrlCoreAsync(url, httpClient, loop, cancellationToken).ConfigureAwait(false);
            return;
        }

        using var ownedClient = new HttpClient();
        await StreamFromUrlCoreAsync(url, ownedClient, loop, cancellationToken).ConfigureAwait(false);
    }

    private async Task StreamFromUrlCoreAsync(
        Uri url, HttpClient httpClient, bool loop, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(url.LocalPath);
        var formatName = ContainerFactory.GetFormatFromExtension(extension)
            ?? throw new NotSupportedException(
                "Формат контейнера '" + extension + "' не поддерживается.");

        var stream = await httpClient.GetStreamAsync(url, cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using var demuxer = ContainerFactory.CreateDemuxer(formatName)
                ?? throw new NotSupportedException(
                    "Демуксер для формата '" + formatName + "' не зарегистрирован.");

            if (demuxer.Open(stream) != ContainerResult.Success)
            {
                throw new InvalidOperationException("Не удалось открыть HTTP-поток.");
            }

            await StreamFromCoreAsync(demuxer, loop, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task StreamFromCoreAsync(
        IDemuxer demuxer, bool loop, CancellationToken cancellationToken)
    {
        var audioStreamIndex = demuxer.BestAudioStreamIndex;
        if (audioStreamIndex < 0)
        {
            throw new InvalidOperationException("Аудиопоток не найден в контейнере.");
        }

        var streamInfo = demuxer.Streams[audioStreamIndex];
        using var codec = CodecRegistry.CreateAudioCodec(streamInfo.CodecId)
            ?? throw new NotSupportedException(
                "Аудиокодек " + streamInfo.CodecId + " не зарегистрирован.");

        var decoderParams = streamInfo.AudioParameters
            ?? CreateFallbackAudioParams();

        codec.InitializeDecoder(in decoderParams)
            .ThrowIfError("Не удалось инициализировать аудиодекодер.");

        using var packet = new MediaPacketBuffer();
        var bufferSamples = Settings.SampleRate * Settings.LatencyMs / 1000;
        using var audioBuffer = new AudioFrameBuffer(
            bufferSamples > 0 ? bufferSamples : 1024,
            Settings.Channels,
            Settings.SampleRate,
            (Atom.Media.AudioSampleFormat)(byte)Settings.SampleFormat);

        await DecodeAndWriteSamplesAsync(
            demuxer, codec, packet, audioBuffer, audioStreamIndex, loop, cancellationToken)
            .ConfigureAwait(false);
    }

    private Atom.Media.AudioCodecParameters CreateFallbackAudioParams()
    {
        return new Atom.Media.AudioCodecParameters
        {
            SampleRate = Settings.SampleRate,
            ChannelCount = Settings.Channels,
            SampleFormat = (Atom.Media.AudioSampleFormat)(byte)Settings.SampleFormat,
        };
    }

    private async Task DecodeAndWriteSamplesAsync(
        IDemuxer demuxer,
        IAudioCodec codec,
        MediaPacketBuffer packet,
        AudioFrameBuffer audioBuffer,
        int audioStreamIndex,
        bool loop,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var readResult = await demuxer.ReadPacketAsync(packet, cancellationToken)
                .ConfigureAwait(false);

            if (readResult == ContainerResult.EndOfFile)
            {
                if (loop)
                {
                    demuxer.Reset();
                    continue;
                }

                break;
            }

            if (readResult != ContainerResult.Success || packet.StreamIndex != audioStreamIndex)
            {
                continue;
            }

            var decodeResult = await codec.DecodeAsync(
                packet.GetMemory(), audioBuffer, cancellationToken).ConfigureAwait(false);

            if (decodeResult != CodecResult.Success)
            {
                continue;
            }

            ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);
            WriteSamples(audioBuffer.GetRawData());

            var sampleDuration = TimeSpan.FromSeconds(
                (double)audioBuffer.SampleCount / Settings.SampleRate);
            await Task.Delay(sampleDuration, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Включает мониторинг аудиопотока через кольцевой буфер.
    /// Записанные данные (после эффектов) доступны через <see cref="MonitorBuffer"/>.
    /// </summary>
    /// <param name="bufferCapacity">Ёмкость буфера в байтах (округляется до степени двойки).</param>
    public void EnableMonitoring(int bufferCapacity)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);
        Volatile.Write(ref monitorBuffer, new AudioRingBuffer(bufferCapacity));
    }

    /// <summary>
    /// Отключает мониторинг аудиопотока.
    /// </summary>
    public void DisableMonitoring() =>
        Volatile.Write(ref monitorBuffer, value: null);

    /// <summary>
    /// Сбрасывает уровни <see cref="CurrentLevel"/>, <see cref="PeakLevel"/>
    /// и <see cref="PeakHold"/> в 0.
    /// </summary>
    public void ResetLevels()
    {
        Volatile.Write(ref peakLevel, 0.0f);
        Volatile.Write(ref currentLevel, 0.0f);
        PeakHold.Reset();
    }

    /// <summary>
    /// Останавливает захват аудиопотока.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    public ValueTask StopCaptureAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);
        return backend.StopCaptureAsync(cancellationToken);
    }

    /// <summary>
    /// Останавливает захват аудиопотока.
    /// </summary>
    public ValueTask StopCaptureAsync() => StopCaptureAsync(CancellationToken.None);

    /// <summary>
    /// Устанавливает значение контрола микрофона.
    /// </summary>
    /// <param name="control">Тип контрола.</param>
    /// <param name="value">Значение контрола.</param>
    public void SetControl(MicrophoneControlType control, float value)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);
        backend.SetControl(control, value);
    }

    /// <summary>
    /// Получает текущее значение контрола микрофона.
    /// </summary>
    /// <param name="control">Тип контрола.</param>
    /// <returns>Текущее значение контрола.</returns>
    public float GetControl(MicrophoneControlType control)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);
        return backend.GetControl(control);
    }

    /// <summary>
    /// Получает диапазон контрола микрофона (min, max, default).
    /// </summary>
    /// <param name="control">Тип контрола.</param>
    /// <returns>Диапазон контрола или null, если неизвестен.</returns>
    public MicrophoneControlRange? GetControlRange(MicrophoneControlType control)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);
        return backend.GetControlRange(control);
    }

    /// <summary>
    /// Событие изменения контрола микрофона внешним приложением.
    /// </summary>
    public event EventHandler<MicrophoneControlChangedEventArgs>? ControlChanged
    {
        add => backend.ControlChanged += value;
        remove => backend.ControlChanged -= value;
    }

    /// <summary>
    /// Высвобождает ресурсы виртуального микрофона.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref isDisposed, value: true)) return;

        Volatile.Write(ref monitorBuffer, value: null);
        latencyBuffer = null;
        await backend.DisposeAsync().ConfigureAwait(false);

        GC.SuppressFinalize(this);
    }

    private void InitializeLatencyBuffer(double delayMs)
    {
        var bps = Settings.SampleFormat.GetBytesPerSample();
        var delaySamples = (int)(delayMs * 0.001 * Settings.SampleRate);
        var delayBytes = delaySamples * Settings.Channels * bps;

        if (delayBytes <= 0)
        {
            latencyBuffer = null;
            return;
        }

        var frameBytes = Settings.SampleRate * Settings.LatencyMs * Settings.Channels * bps / 1000;
        var bufferSize = Math.Max(delayBytes + (frameBytes * 2), delayBytes * 2);

        var buf = new AudioRingBuffer(bufferSize);
        var silence = new byte[delayBytes];
        buf.Write(silence);
        latencyBuffer = buf;
    }

    private byte[] ApplyEffects(ReadOnlySpan<byte> sampleData, out float peak, out float rms)
    {
        var format = Settings.SampleFormat;
        var channels = Settings.Channels;
        var sampleRate = Settings.SampleRate;
        var bps = format.GetBytesPerSample();
        var sampleCount = sampleData.Length / bps;

        var floatBuffer = ArrayPool<float>.Shared.Rent(sampleCount);
        try
        {
            var samples = floatBuffer.AsSpan(0, sampleCount);

            if (format is AudioSampleFormat.F32)
            {
                MemoryMarshal.Cast<byte, float>(sampleData).CopyTo(samples);
            }
            else
            {
                for (var i = 0; i < sampleCount; i++)
                {
                    samples[i] = (float)AudioResampler.ReadNormalized(sampleData, format, i);
                }
            }

            Effects.Process(samples, channels, sampleRate);
            MeasureLevels(samples, out peak, out rms);

            var output = ArrayPool<byte>.Shared.Rent(sampleData.Length);

            if (format is AudioSampleFormat.F32)
            {
                MemoryMarshal.AsBytes(samples).CopyTo(output);
            }
            else
            {
                for (var i = 0; i < sampleCount; i++)
                {
                    AudioResampler.WriteNormalized(output, format, i, samples[i]);
                }
            }

            return output;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(floatBuffer);
        }
    }

    private static void MeasureLevels(ReadOnlySpan<float> samples, out float peak, out float rms)
    {
        if (samples.Length == 0)
        {
            peak = 0.0f;
            rms = 0.0f;
            return;
        }

        if (Vector.IsHardwareAccelerated && samples.Length >= Vector<float>.Count)
        {
            MeasureLevelsSimd(samples, out peak, out rms);
            return;
        }

        var maxAbs = 0.0f;
        var sumSq = 0.0;

        foreach (var s in samples)
        {
            var abs = Math.Abs(s);
            if (abs > maxAbs) maxAbs = abs;
            sumSq += (double)s * s;
        }

        peak = maxAbs;
        rms = (float)Math.Sqrt(sumSq / samples.Length);
    }

    private static void MeasureLevelsSimd(ReadOnlySpan<float> samples, out float peak, out float rms)
    {
        var vectorSize = Vector<float>.Count;
        var absMax = Vector<float>.Zero;
        var sumSq = Vector<float>.Zero;
        var i = 0;

        for (; i + vectorSize <= samples.Length; i += vectorSize)
        {
            var v = new Vector<float>(samples.Slice(i, vectorSize));
            absMax = Vector.Max(absMax, Vector.Abs(v));
            sumSq += v * v;
        }

        var maxVal = 0.0f;
        var sum = 0.0;

        for (var j = 0; j < vectorSize; j++)
        {
            if (absMax[j] > maxVal) maxVal = absMax[j];
            sum += sumSq[j];
        }

        for (; i < samples.Length; i++)
        {
            var abs = Math.Abs(samples[i]);
            if (abs > maxVal) maxVal = abs;
            sum += (double)samples[i] * samples[i];
        }

        peak = maxVal;
        rms = (float)Math.Sqrt(sum / samples.Length);
    }

    /// <summary>
    /// Создаёт новый экземпляр виртуального микрофона.
    /// </summary>
    /// <param name="settings">Настройки микрофона.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Инициализированный экземпляр виртуального микрофона.</returns>
#pragma warning disable CA2000

    public static async ValueTask<VirtualMicrophone> CreateAsync(
        VirtualMicrophoneSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var micBackend = CreateBackend();

        try
        {
            await micBackend.InitializeAsync(settings, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await micBackend.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return new VirtualMicrophone(micBackend, settings);
    }

#pragma warning restore CA2000

    /// <summary>
    /// Создаёт новый экземпляр виртуального микрофона.
    /// </summary>
    /// <param name="settings">Настройки микрофона.</param>
    /// <returns>Инициализированный экземпляр виртуального микрофона.</returns>
    public static ValueTask<VirtualMicrophone> CreateAsync(VirtualMicrophoneSettings settings)
        => CreateAsync(settings, CancellationToken.None);

    internal static IDisposable PushBackendFactoryOverride(Func<IVirtualMicrophoneBackend> backendFactory)
    {
        ArgumentNullException.ThrowIfNull(backendFactory);

        if (Interlocked.CompareExchange(location1: ref backendFactoryOverride, value: backendFactory, comparand: null) is not null)
        {
            throw new InvalidOperationException("Virtual microphone backend factory override is already active.");
        }

        return new BackendFactoryOverrideScope(static () => Interlocked.Exchange(location1: ref backendFactoryOverride, value: null));
    }

    private static IVirtualMicrophoneBackend CreateBackend()
    {
        var overrideFactory = Volatile.Read(ref backendFactoryOverride);
        if (overrideFactory is not null)
        {
            return overrideFactory();
        }

        if (OperatingSystem.IsLinux()) return new LinuxMicrophoneBackend();
        if (OperatingSystem.IsMacOS()) return new MacOSMicrophoneBackend();
        if (OperatingSystem.IsWindows()) return new WindowsMicrophoneBackend();

        throw new PlatformNotSupportedException(
            "Виртуальный микрофон не поддерживается на текущей платформе.");
    }

    private sealed class BackendFactoryOverrideScope(Action dispose) : IDisposable
    {
        private Action? dispose = dispose;

        public void Dispose() => Interlocked.Exchange(location1: ref dispose, value: null)?.Invoke();
    }
}
