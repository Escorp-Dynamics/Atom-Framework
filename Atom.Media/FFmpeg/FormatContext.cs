using System.Runtime.InteropServices;

namespace Atom.Media;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct FormatContext
{
    public nint av_class; // Указатель на структуру AVClass
    public nint iformat; // Указатель на структуру AVInputFormat (для входных контекстов)
    public nint oformat; // Указатель на структуру AVOutputFormat (для выходных контекстов)
    public nint priv_data; // Приватные данные формата
    public IOContext* pb; // Указатель на структуру AVIOContext
    public int ctx_flags; // Флаги контекста
    public int nb_streams; // Количество потоков
    public MediaStream** streams; // Указатель на массив структур AVStream
    public nint filename; // Имя файла (строка)
    public long start_time; // Время начала в тактах
    public long duration; // Продолжительность в тактах
    public long bit_rate; // Битрейт
    public int packet_size; // Размер пакета
    public int max_delay; // Максимальная задержка
    public int flags; // Флаги
    public long probesize; // Размер пробы
    public long max_analyze_duration; // Максимальная продолжительность анализа
    public nint key; // Ключ для шифрования
    public int keylen; // Длина ключа
    public int nb_programs; // Количество программ
    public nint programs; // Указатель на массив структур AVProgram
    public int video_codec_id; // Идентификатор кодека видео
    public int audio_codec_id; // Идентификатор кодека аудио
    public int subtitle_codec_id; // Идентификатор кодека субтитров
    public int max_index_size; // Максимальный размер индекса
    public int max_picture_buffer; // Максимальный размер буфера изображений
    public int nb_chapters; // Количество глав
    public nint chapters; // Указатель на массив структур AVChapter
    public nint metadata; // Метаданные
    public long start_time_realtime; // Время начала в реальном времени
    public int fps_probe_size; // Размер пробы FPS
    public int error_recognition; // Режим распознавания ошибок
    public nint interrupt_callback; // Функция прерывания
    public int debug; // Флаги отладки
    public long max_interleave_delta; // Максимальная разница между пакетами
    public int strict_std_compliance; // Строгость соответствия стандартам
    public int event_flags; // Флаги событий
    public int max_ts_probe; // Максимальное количество проб TS
    public int avoid_negative_ts; // Избегание отрицательных временных меток
    public int ts_id; // Идентификатор временной метки
    public int audio_preload; // Предварительная загрузка аудио
    public int max_chunk_duration; // Максимальная продолжительность чанка
    public int max_chunk_size; // Максимальный размер чанка
    public int use_wallclock_as_timestamps; // Использование времени стены как временных меток
    public int avio_flags; // Флаги AVIO
    public int duration_estimation_method; // Метод оценки продолжительности
    public long skip_initial_bytes; // Пропуск начальных байтов
    public int correct_ts_overflow; // Коррекция переполнения временных меток
    public int seek2any; // Поиск в любом месте
    public int flush_packets; // Сброс пакетов
    public int probe_score; // Оценка пробы
    public int format_probesize; // Размер пробы формата
    public int codec_whitelist; // Белый список кодеков
    public int format_whitelist; // Белый список форматов
    public nint @internal; // Внутренняя структура
    public nint io_repositioned; // Перепозиционирование IO
    public nint video_codec; // Указатель на структуру AVCodec (видео)
    public nint audio_codec; // Указатель на структуру AVCodec (аудио)
    public nint subtitle_codec; // Указатель на структуру AVCodec (субтитры)
    public nint data_codec; // Указатель на структуру AVCodec (данные)
    public int metadata_header_padding; // Заполнение заголовка метаданных
    public nint opaque; // Дополнительные данные
    public nint control_message_cb; // Функция обратного вызова для управления сообщениями
    public long output_ts_offset; // Смещение выходного времени
    public nint dump_separator; // Разделитель дампа
    public int data_codec_id; // Идентификатор кодека данных
    public nint data_codec_tag; // Тег кодека данных
    public nint metadata_conv; // Конвертер метаданных
    public nint streams_metadata_conv; // Конвертер метаданных потоков
    public nint avio_ctx_buffer; // Буфер AVIO контекста
    public int avio_ctx_buffer_size; // Размер буфера AVIO контекста
    public nint protocol_whitelist; // Белый список протоколов
    public nint protocol_blacklist; // Черный список протоколов
    public int max_streams; // Максимальное количество потоков
    public int skip_estimate_duration_from_pts; // Пропуск оценки продолжительности из PTS
}