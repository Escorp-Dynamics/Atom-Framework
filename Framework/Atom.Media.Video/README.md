# Atom.Media.Video

Кроссплатформенная библиотека для создания виртуальных камер и управления видеоустройствами без root-прав.

## Основные возможности

- **Виртуальная камера** — создание видеоустройства в системе через нативные API
- **PipeWire** — нативная интеграция на Linux без v4l2loopback
- **Полные метаданные** — эмуляция реальной камеры (производитель, модель, серийный номер, USB VID/PID)
- **UVC контролы** — управление яркостью, контрастом, экспозицией и другими параметрами через SPA props
- **20 форматов пикселей** — YUV planar/packed, RGB, NV12, P010, grayscale
- **Zero-root** — не требует привилегий суперпользователя
- **NativeAOT-ready** — совместимо с AOT-компиляцией и тримингом

## Платформы

| Платформа | Бэкенд | Статус |
|-----------|--------|--------|
| Linux | PipeWire (нативный P/Invoke) | ✅ Реализован |
| macOS | CoreAudio / DAL | 🔲 Планируется |
| Windows | MediaFoundation | 🔲 Планируется |

## Быстрый старт

### Создание виртуальной камеры

```csharp
using Atom.Media.Video;

var settings = new VirtualCameraSettings
{
    Width = 1920,
    Height = 1080,
    FrameRate = 30,
    PixelFormat = VideoPixelFormat.Yuv420P,
    Name = "My Virtual Camera",
};

await using var camera = await VirtualCamera.CreateAsync(settings);
await camera.StartCaptureAsync();

// Запись кадра (все плоскости последовательно для planar форматов)
camera.WriteFrame(rawFrameData);

await camera.StopCaptureAsync();
```

### Запись из файла изображения или потока

```csharp
// Из файла — формат определяется по расширению (PNG, WebP)
camera.WriteFrame("/path/to/frame.png");
camera.WriteFrame("/path/to/frame.webp");

// Из потока — формат указывается явно
using var stream = File.OpenRead("/path/to/frame.png");
camera.WriteFrame(stream, ".png");
```

### Стриминг из видеофайла или URL

Камера может непрерывно транслировать видео из контейнера (MP4, MKV, WebM и др.), покадрово декодируя и записывая с заданным фреймрейтом.

```csharp
using var cts = new CancellationTokenSource();

// Из файла
await camera.StreamFromAsync("/path/to/video.mp4", cts.Token);

// Из потока
using var fileStream = File.OpenRead("/path/to/video.mkv");
await camera.StreamFromAsync(fileStream, ".mkv", cts.Token);

// Из URL (формат определяется по расширению)
var url = new Uri("https://example.com/stream.webm");
await camera.StreamFromAsync(url, cancellationToken: cts.Token);

// Из URL с переиспользованием HttpClient
using var httpClient = new HttpClient();
await camera.StreamFromAsync(url, httpClient, cts.Token);

// Зацикленное воспроизведение
await camera.StreamFromAsync("/path/to/video.mp4", loop: true, cancellationToken: cts.Token);
```

> Требуется зарегистрированный демуксер и видеокодек в `ContainerFactory` / `CodecRegistry` для соответствующего формата.
> Фреймрейт трансляции определяется настройкой `VirtualCameraSettings.FrameRate`.

### Эмуляция реальной камеры

```csharp
var settings = new VirtualCameraSettings
{
    Width = 1920,
    Height = 1080,
    Name = "HD Pro Webcam C920",
    Vendor = "Logitech",
    Model = "C920",
    SerialNumber = "A1B2C3D4",
    FirmwareVersion = "2.0.14",
    UsbVendorId = 0x046D,
    UsbProductId = 0x0825,
    BusType = "usb",
    FormFactor = "webcam",
    IconName = "camera-web",
};

await using var camera = await VirtualCamera.CreateAsync(settings);
```

### Произвольные свойства PipeWire

```csharp
var settings = new VirtualCameraSettings
{
    Width = 640,
    Height = 480,
    ExtraProperties = new Dictionary<string, string>
    {
        ["node.latency"] = "512/48000",
        ["custom.app.version"] = "1.0",
    },
};
```

## Поддерживаемые форматы пикселей

| Категория | Форматы |
|-----------|---------|
| YUV planar 8-bit | `Yuv420P`, `Yuv422P`, `Yuv444P` |
| YUV planar 10-bit | `Yuv420P10Le`, `Yuv422P10Le`, `Yuv444P10Le` |
| YUV semi-planar | `Nv12`, `Nv21`, `P010Le` |
| YUV packed | `Yuyv422`, `Uyvy422` |
| RGB | `Rgb24`, `Bgr24`, `Rgba32`, `Bgra32`, `Argb32`, `Abgr32` |
| Grayscale | `Gray8`, `Gray16Le` |
| Кодеки | `Mjpeg`, `H264`, `Vp8`, `Vp9` |

### Кодеки

Для кодированных форматов камера объявляет соответствующий SPA media subtype. Пользователь предоставляет уже закодированные кадры (MJPEG фреймы, H.264 NAL units и т.д.):

```csharp
var settings = new VirtualCameraSettings
{
    Width = 1920,
    Height = 1080,
    PixelFormat = VideoPixelFormat.Mjpeg,
    Name = "MJPEG Camera",
};

await using var camera = await VirtualCamera.CreateAsync(settings);
await camera.StartCaptureAsync();

// Предоставляем закодированный MJPEG фрейм
camera.WriteFrame(encodedMjpegFrame);
```

## Метаданные камеры

| Свойство | Описание | PipeWire property |
|----------|----------|-------------------|
| `Name` | Название камеры | `node.description` |
| `Vendor` | Производитель | `device.vendor.name` |
| `Model` | Модель | `device.product.name` |
| `SerialNumber` | Серийный номер | `device.serial` |
| `Description` | Описание | `device.description` |
| `FirmwareVersion` | Версия прошивки | `device.firmware.version` |
| `UsbVendorId` | USB Vendor ID | `device.vendor.id` |
| `UsbProductId` | USB Product ID | `device.product.id` |
| `BusType` | Тип шины | `device.bus` |
| `FormFactor` | Форм-фактор | `device.form-factor` |
| `IconName` | Иконка (freedesktop) | `device.icon-name` |
| `ExtraProperties` | Произвольные свойства | любые |

## UVC контролы камеры

Виртуальная камера поддерживает UVC-совместимые контролы через PipeWire SPA props:

```csharp
await using var camera = await VirtualCamera.CreateAsync(settings);
await camera.StartCaptureAsync();

// Установить яркость
camera.SetControl(CameraControlType.Brightness, 0.7f);

// Получить текущее значение контраста
var contrast = camera.GetControl(CameraControlType.Contrast);

// Получить диапазон контрола (min, max, default)
var range = camera.GetControlRange(CameraControlType.Brightness);
if (range is not null)
{
    Console.WriteLine($"Brightness: [{range.Min}..{range.Max}], default={range.Default}");
}

// Подписаться на изменения контролов (от PipeWire control_info)
camera.ControlChanged += (sender, e) =>
{
    Console.WriteLine($"{e.Control} = {e.Value}");
    if (e.Range is not null)
    {
        Console.WriteLine($"  Range: [{e.Range.Min}..{e.Range.Max}]");
    }
};
```

| Контрол | SPA property | Описание |
|---------|-------------|----------|
| `Brightness` | `SPA_PROP_brightness` | Яркость изображения |
| `Contrast` | `SPA_PROP_contrast` | Контрастность |
| `Saturation` | `SPA_PROP_saturation` | Насыщенность цвета |
| `Hue` | `SPA_PROP_hue` | Оттенок (сдвиг по цветовому кругу) |
| `Gamma` | `SPA_PROP_gamma` | Гамма-коррекция |
| `Exposure` | `SPA_PROP_exposure` | Экспозиция |
| `Gain` | `SPA_PROP_gain` | Усиление сигнала |
| `Sharpness` | `SPA_PROP_sharpness` | Резкость |

### Диапазоны контролов

`GetControlRange` возвращает `CameraControlRange` (min, max, default) после того как PipeWire отправит `control_info`. До этого момент возвращается `null`.

### Событие `ControlChanged`

Срабатывает когда PipeWire изменяет значение контрола (через `control_info` callback). EventArgs содержит:

- `Control` — `CameraControlType` (какой контрол изменился)
- `Value` — `float` (новое значение)
- `Range` — `CameraControlRange?` (текущий диапазон, если известен)

## Требования (Linux)

- `libpipewire-0.3` — `sudo dnf install pipewire-devel` / `sudo apt install libpipewire-0.3-dev`
- PipeWire daemon запущен (`systemctl --user status pipewire`)

## Оптимизации

- **ArrayPool буферизация** — `WriteFrame` использует `ArrayPool<byte>.Shared` вместо прямых аллокаций. Буфер арендуется при инициализации и возвращается при Dispose.
- **Pre-allocation** — размер буфера вычисляется при `CreateAsync` на основе формата и разрешения, что исключает аллокации при первой записи кадра.
- **Lockless stride** — для кодированных форматов stride = 0, размер кадра передаётся напрямую.
