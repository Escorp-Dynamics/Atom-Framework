# Atom.Media

Модуль для работы с мультимедиа контентом на основе FFmpeg.

## Обзор

`Atom.Media` предоставляет высокоуровневый API для работы с видео и аудио потоками, включая:

- **Декодирование и кодирование** видео/аудио потоков
- **Транскодирование** между различными форматами (MP4, AVI, MKV, MOV, FLV, WEBM и др.)
- **Потоковая обработка** с поддержкой V4L2 устройств (Linux)
- **Применение фильтров** (масштабирование, обрезка, поворот, zoom и др.)
- **Генерация белого шума** для тестирования

## Требования

- FFmpeg библиотеки (`libavcodec`, `libavformat`, `libavutil`, `libavfilter`, `libswscale`, `libavdevice`)
- .NET 10.0+

## Установка

```bash
dotnet add package Escorp.Atom.Media
```

## Быстрый старт

### Конвертация видео

```csharp
using Atom.Media;
using System.Drawing;

// Конвертация MP4 в AVI
using var stream = new VideoStream("input.mp4", "output.avi");
stream.WaitForEnding();
```

### Конвертация с указанием разрешения

```csharp
using var stream = new VideoStream("input.mp4", "output.mkv", new Size(1920, 1080));
stream.WaitForEnding();
```

### Запись в V4L2 устройство (Linux)

```csharp
using var stream = new VideoStream("input.mp4", "/dev/video2");
stream.IsMuted = true; // Отключить аудио для V4L2
stream.WaitForEnding();
```

### Асинхронная обработка

```csharp
using var stream = new VideoStream("input.mp4", "output.mp4");
await stream.WaitForEndingAsync(TimeSpan.FromMinutes(5));
```

### Применение фильтров

```csharp
using Atom.Media;
using Atom.Media.Filters.Video;

using var stream = new VideoStream("input.mp4", "output.mp4");
stream.Filters = [
    new ZoomPanFilter(1.0f, 1.5f, TimeSpan.FromSeconds(5)), // Zoom эффект
    new CropFilter { Width = "iw-100", Height = "ih-100" }, // Обрезка
    new TransposeFilter(1) // Поворот на 90°
];
stream.WaitForEnding();
```

### Генерация белого шума

```csharp
// Генерация 10-секундного видео с белым шумом
using var stream = new VideoStream(string.Empty, "noise.mp4", new Size(1280, 720))
{
    FrameRate = 30
};
stream.WaitForEnding(TimeSpan.FromSeconds(10));
```

## API Reference

### VideoStream

Основной класс для работы с медиа потоками.

| Свойство | Тип | Описание |
|----------|-----|----------|
| `Input` | `string` | Путь к входному файлу или URL |
| `Output` | `string` | Путь к выходному файлу или устройству |
| `Resolution` | `Size` | Разрешение выходного видео |
| `FrameRate` | `int` | Частота кадров (по умолчанию 25) |
| `AudioSampleRate` | `int` | Частота аудиосемплов (по умолчанию 44100) |
| `AudioChannels` | `int` | Количество аудиоканалов (по умолчанию 2) |
| `IsLooped` | `bool` | Зацикленное воспроизведение |
| `IsMuted` | `bool` | Отключение аудио |
| `ScaleMode` | `ScaleMode` | Режим масштабирования |
| `Filters` | `IEnumerable<IFilter>` | Применяемые фильтры |
| `IsActive` | `bool` | Состояние активности записи |

| Метод | Описание |
|-------|----------|
| `WaitForEnding()` | Ожидание завершения обработки |
| `WaitForEnding(TimeSpan)` | Ожидание с таймаутом |
| `WaitForEndingAsync()` | Асинхронное ожидание |
| `WaitForEndingAsync(TimeSpan, CancellationToken)` | Асинхронное ожидание с таймаутом и отменой |
| `Seek(long, SeekOrigin)` | Перемотка потока |
| `Close()` | Закрытие потока |

### ScaleMode

Режимы масштабирования видео.

| Значение | Описание |
|----------|----------|
| `Stretch` | Растягивание под выходное разрешение |
| `Fit` | Вписывание с сохранением пропорций (чёрные полосы) |
| `Cover` | Заполнение с обрезкой лишних частей |

### Фильтры

#### ZoomPanFilter

Анимированный zoom-эффект.

```csharp
new ZoomPanFilter(
    from: 1.0f,    // Начальный масштаб
    to: 1.5f,      // Конечный масштаб
    duration: TimeSpan.FromSeconds(5)
)
{
    X = "iw/2-(iw/zoom/2)", // Центрирование по X
    Y = "ih/2-(ih/zoom/2)"  // Центрирование по Y
}
```

#### CropFilter

Обрезка видео с опциональной анимацией.

```csharp
new CropFilter
{
    Width = "iw-100",
    Height = "ih-100",
    X = "50", // Смещение по X
    Y = "50"  // Смещение по Y
}
```

#### TransposeFilter

Поворот видео.

```csharp
new TransposeFilter(1) // 0=90° CCW, 1=90° CW, 2=90° CCW+VFlip, 3=90° CW+VFlip
```

## Поддерживаемые форматы

### Контейнеры

- MP4, AVI, MKV, MOV, FLV, WEBM, WMV, MPEG

### Видеокодеки

- H.264, H.265/HEVC, VP8, VP9, MPEG-1/2/4, MJPEG, Theora, и другие

### Аудиокодеки

- AAC, MP3, Vorbis, Opus, FLAC, AC3, и другие

### Изображения

- JPEG, PNG, WEBP, BMP, GIF

## Архитектура

```text
Atom.Media/
├── VideoStream.cs          # Основной класс потока
├── VideoStreamException.cs # Исключения
├── MediaCodec.cs           # Перечисление кодеков
├── PixelFormat.cs          # Форматы пикселей
├── SampleFormat.cs         # Форматы аудиосемплов
├── ScaleMode.cs            # Режимы масштабирования
├── WhiteNoiseGenerator.cs  # Генератор шума
├── Extensions.cs           # Методы расширения
├── Filters/                # Система фильтров
│   ├── IFilter.cs          # Интерфейс фильтра
│   ├── Filter.cs           # Базовый класс
│   ├── IVideoFilter.cs     # Интерфейс видеофильтра
│   ├── VideoFilter.cs      # Базовый видеофильтр
│   ├── FilterEvalMode.cs   # Режимы оценки
│   └── Video/              # Реализации
│       ├── ZoomPanFilter.cs
│       ├── CropFilter.cs
│       └── TransposeFilter.cs
└── FFmpeg/                 # P/Invoke обёртки
    ├── Codec.cs            # libavcodec
    ├── Format.cs           # libavformat
    ├── Filter.cs           # libavfilter
    ├── SwScale.cs          # libswscale
    ├── Device.cs           # libavdevice
    ├── Util.cs             # libavutil
    └── Структуры...
```

## Потокобезопасность

`VideoStream` использует внутренний `Locker` для синхронизации доступа к FFmpeg контекстам. Однако рекомендуется:

- Не обращаться к свойствам потока из нескольких потоков одновременно
- Использовать один экземпляр `VideoStream` на один медиа поток
- Корректно освобождать ресурсы через `Dispose()` или `using`

## Ограничения

- V4L2 устройства поддерживаются только на Linux
- Некоторые комбинации кодеков/контейнеров могут быть несовместимы
- Для работы требуется установленный FFmpeg с соответствующими библиотеками

## Лицензия

Часть фреймворка Atom. См. корневой LICENSE файл.
