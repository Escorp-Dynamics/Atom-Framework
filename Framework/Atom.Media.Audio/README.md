# Atom.Media.Audio

Кроссплатформенная библиотека для создания виртуальных микрофонов и управления аудиоустройствами без root-прав.

## Основные возможности

- **Виртуальный микрофон** — создание аудиоустройства-источника в системе через нативные API
- **PipeWire** — нативная интеграция на Linux
- **Аудио контролы** — управление громкостью и мутом через SPA props
- **10 форматов семплов** — U8/S16/S32/F32/F64 interleaved + planar
- **Связка с камерой** — объединение микрофона и камеры через общий `DeviceId`
- **Zero-root** — не требует привилегий суперпользователя
- **NativeAOT-ready** — совместимо с AOT-компиляцией и тримингом

## Платформы

| Платформа | Бэкенд | Статус |
|-----------|--------|--------|
| Linux | PipeWire (нативный P/Invoke) | ✅ Реализован |
| macOS | CoreAudio HAL Plugin | 🔲 Планируется |
| Windows | WASAPI | 🔲 Планируется |

## Быстрый старт

### Создание виртуального микрофона

```csharp
using Atom.Media.Audio;

var settings = new VirtualMicrophoneSettings
{
    SampleRate = 48000,
    Channels = 1,
    SampleFormat = AudioSampleFormat.F32,
    Name = "My Virtual Microphone",
};

await using var mic = await VirtualMicrophone.CreateAsync(settings);
await mic.StartCaptureAsync();

// Запись аудиосемплов
mic.WriteSamples(pcmData);

await mic.StopCaptureAsync();
```

### Запись из WAV-файла или потока

```csharp
// Из файла — параметры WAV должны совпадать с настройками микрофона
mic.WriteSamples("/path/to/audio.wav");

// Из потока (например, из сети или памяти)
using var stream = File.OpenRead("/path/to/audio.wav");
mic.WriteSamples(stream);

// Из URL (WAV через HTTP)
using var httpClient = new HttpClient();
using var httpStream = await httpClient.GetStreamAsync("https://example.com/audio.wav");
mic.WriteSamples(httpStream);
```

### Стриминг из медиафайла или URL

Метод `StreamFromAsync` декодирует аудио через демуксер+кодек и записывает в микрофон в реальном времени:

```csharp
// Из файла — формат определяется по расширению
await mic.StreamFromAsync("/path/to/music.wav");

// Из потока с указанием формата
using var stream = File.OpenRead("audio.wav");
await mic.StreamFromAsync(stream, "wav");

// Из URL
await mic.StreamFromAsync(new Uri("https://example.com/audio.wav"));

// С собственным HttpClient
using var httpClient = new HttpClient();
await mic.StreamFromAsync(
    new Uri("https://example.com/audio.wav"),
    httpClient);

// Зацикленное воспроизведение
using var cts = new CancellationTokenSource();
await mic.StreamFromAsync("/path/to/loop.wav", loop: true, cancellationToken: cts.Token);
```

> **Примечание:** формат контейнера должен иметь зарегистрированный демуксер. WAV поддерживается из коробки.

### Связка с виртуальной камерой

Микрофон и камера объединяются через общий `DeviceId` — PipeWire увидит их как одно устройство:

```csharp
using Atom.Media.Audio;
using Atom.Media.Video;

var deviceId = Guid.NewGuid().ToString();

var camSettings = new VirtualCameraSettings
{
    Width = 1920,
    Height = 1080,
    DeviceId = deviceId,
};

var micSettings = new VirtualMicrophoneSettings
{
    SampleRate = 48000,
    Channels = 1,
    DeviceId = deviceId,
};

await using var camera = await VirtualCamera.CreateAsync(camSettings);
await using var mic = await VirtualMicrophone.CreateAsync(micSettings);

await camera.StartCaptureAsync();
await mic.StartCaptureAsync();

// Оба устройства связаны через device.id и node.group
```

### Произвольные свойства PipeWire

```csharp
var settings = new VirtualMicrophoneSettings
{
    SampleRate = 44100,
    Channels = 2,
    ExtraProperties = new Dictionary<string, string>
    {
        ["node.latency"] = "256/44100",
        ["media.role"] = "Music",
    },
};
```

## Поддерживаемые форматы семплов

| Категория | Форматы |
|-----------|---------|
| Interleaved | `U8`, `S16`, `S32`, `F32`, `F64` |
| Planar | `U8Planar`, `S16Planar`, `S32Planar`, `F32Planar`, `F64Planar` |

## Контролы микрофона

| Контрол | SPA Prop | Диапазон |
|---------|----------|----------|
| `Volume` | `0x10001` | 0.0 — 1.0+ |
| `Mute` | `0x10002` | 0.0 (off) / 1.0 (on) |

### Управление контролами

```csharp
await using var mic = await VirtualMicrophone.CreateAsync(settings);
await mic.StartCaptureAsync();

// Установить громкость
mic.SetControl(MicrophoneControlType.Volume, 0.8f);

// Получить текущее значение
float volume = mic.GetControl(MicrophoneControlType.Volume);

// Диапазон значений
MicrophoneControlRange? range = mic.GetControlRange(MicrophoneControlType.Volume);
```

### Событие изменения контрола

```csharp
mic.ControlChanged += (sender, args) =>
{
    Console.WriteLine($"{args.Control}: {args.Value}");
    if (args.Range is { } r)
        Console.WriteLine($"  [{r.Min} — {r.Max}], default={r.Default}");
};
```

## Метаданные микрофона

| Свойство | Описание | PipeWire property |
|----------|----------|-------------------|
| `Name` | Имя устройства | `node.description` |
| `Vendor` | Производитель | `device.vendor.name` |
| `Model` | Модель | `device.product.name` |
| `SerialNumber` | Серийный номер | `device.serial` |
| `Description` | Краткое описание | `node.nick` |
| `DeviceId` | ID для связки с камерой | `device.id` + `node.group` |

## Архитектура

```
VirtualMicrophone (public API)
  └─ IVirtualMicrophoneBackend (internal)
       ├─ LinuxMicrophoneBackend   → PipeWire Audio/Source
       ├─ MacOSMicrophoneBackend   → CoreAudio HAL Plugin (planned)
       └─ WindowsMicrophoneBackend → WASAPI (planned)
```

## Требования

- **.NET 10.0**
- **Linux**: PipeWire ≥ 0.3 (`libpipewire-0.3.so.0`)
