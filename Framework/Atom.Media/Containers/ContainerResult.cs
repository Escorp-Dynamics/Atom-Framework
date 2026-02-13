#pragma warning disable CA1028

namespace Atom.Media;

/// <summary>
/// Результат операции контейнера.
/// </summary>
public enum ContainerResult : byte
{
    /// <summary>Успех.</summary>
    Success = 0,

    /// <summary>Конец файла/потока.</summary>
    EndOfFile = 1,

    /// <summary>Нужно больше данных.</summary>
    NeedMoreData = 2,

    /// <summary>Файл не найден.</summary>
    FileNotFound = 3,

    /// <summary>Неподдерживаемый формат.</summary>
    UnsupportedFormat = 4,

    /// <summary>Данные повреждены.</summary>
    CorruptData = 5,

    /// <summary>Ошибка ввода-вывода.</summary>
    IoError = 6,

    /// <summary>Контейнер не открыт.</summary>
    NotOpen = 7,

    /// <summary>Поток не найден.</summary>
    StreamNotFound = 8,

    /// <summary>Ошибка.</summary>
    Error = 255,
}
