namespace Atom.Audio.Plugins.CLAP;

/// <summary>
/// Представляет список расширений CLAP.
/// </summary>
internal static class ClapExtension
{
    public const string AudioPorts = "clap.audio-ports";
    public const string CvPorts = "clap.cv-ports";
    public const string EventPorts = "clap.event-ports";
    public const string GUI = "clap.gui";
    public const string Latency = "clap.latency";
    public const string NoteDetection = "clap.note-detection";
    public const string NoteName = "clap.note-name";
    public const string NotePorts = "clap.note-ports";
    public const string Params = "clap.params";
    public const string POSIX = "clap.posix-fd-support";
    public const string Render = "clap.render";
    public const string State = "clap.state";
    public const string StreamTime = "clap.stream-time";
    public const string ThreadCheck = "clap.thread-check";
    public const string ThreadPool = "clap.thread-pool";
    public const string TimerSupport = "clap.timer-support";
    public const string Transport = "clap.transport";
    public const string Tuning = "clap.tuning";
    public const string VoiceInfo = "clap.voice-info";

    public static string Get(Extension extension) => extension switch
    {
        Extension.AudioPorts => AudioPorts,
        Extension.CvPorts => CvPorts,
        Extension.EventPorts => EventPorts,
        Extension.GUI => GUI,
        Extension.Latency => Latency,
        Extension.NoteDetection => NoteDetection,
        Extension.NoteName => NoteName,
        Extension.NotePorts => NotePorts,
        Extension.Params => Params,
        Extension.POSIX => POSIX,
        Extension.Render => Render,
        Extension.State => State,
        Extension.StreamTime => StreamTime,
        Extension.ThreadCheck => ThreadCheck,
        Extension.ThreadPool => ThreadPool,
        Extension.TimerSupport => TimerSupport,
        Extension.Transport => Transport,
        Extension.Tuning => Tuning,
        Extension.VoiceInfo => VoiceInfo,
        _ => throw new InvalidOperationException("Неизвестный идентификатор расширения"),
    };
}

/// <summary>
/// Перечисление расширений CLAP (Cross-Platform Audio Plugin).
/// Каждое расширение представляет определенную функциональность, которую может поддерживать плагин.
/// </summary>
public enum Extension
{
    /// <summary>
    /// Это расширение позволяет плагину запрашивать количество аудио входов и выходов, доступных на хосте,
    /// и запрашивать у хоста подключение плагина к определенным аудио входам и выходам.
    /// </summary>
    AudioPorts,
    /// <summary>
    /// Это расширение позволяет плагину запрашивать количество входов и выходов управляющего напряжения, доступных на хосте,
    /// и запрашивать у хоста подключение плагина к определенным входам и выходам управляющего напряжения.
    /// </summary>
    CvPorts,
    /// <summary>
    /// Это расширение позволяет плагину запрашивать количество входов и выходов событий, доступных на хосте,
    /// и запрашивать у хоста подключение плагина к определенным входам и выходам событий.
    /// </summary>
    EventPorts,
    /// <summary>
    /// Это расширение позволяет плагину создавать графический пользовательский интерфейс (GUI), который может быть отображен хост-приложением.
    /// GUI может быть использован для управления параметрами плагина и отображения информации о состоянии плагина.
    /// </summary>
    GUI,
    /// <summary>
    /// Это расширение позволяет плагину запрашивать задержку аудиообработки, которую он выполняет, и запрашивать у хоста компенсацию задержки.
    /// </summary>
    Latency,
    /// <summary>
    /// Это расширение позволяет плагину обнаруживать ноты, которые играются на клавиатуре, и предоставлять информацию о нотах хост-приложению.
    /// </summary>
    NoteDetection,
    /// <summary>
    /// Это расширение позволяет плагину преобразовывать между номерами нот и именами нот.
    /// </summary>
    NoteName,
    /// <summary>
    /// Это расширение позволяет плагину запрашивать количество входов и выходов нот, доступных на хосте,
    /// и запрашивать у хоста подключение плагина к определенным входам и выходам нот.
    /// </summary>
    NotePorts,
    /// <summary>
    /// Это расширение позволяет плагину определять параметры, которые могут быть управляемыми хост-приложением.
    /// Параметры могут быть использованы для управления поведением плагина и отображения информации о состоянии плагина.
    /// </summary>
    Params,
    /// <summary>
    /// Это расширение позволяет плагину использовать дескрипторы файлов POSIX для взаимодействия с хост-приложением.
    /// </summary>
    POSIX,
    /// <summary>
    /// Это расширение позволяет плагину рендерить аудио в буфер, предоставленный хост-приложением.
    /// </summary>
    Render,
    /// <summary>
    /// Это расширение позволяет плагину сохранять и восстанавливать свое состояние.
    /// Состояние может быть использовано для сохранения настроек плагина между сеансами.
    /// </summary>
    State,
    /// <summary>
    /// Это расширение позволяет плагину запрашивать время потока аудио, которое обрабатывается.
    /// Время потока - это время, прошедшее с начала воспроизведения аудио, и может быть использовано для синхронизации плагина с другими аудиоисточниками.
    /// </summary>
    StreamTime,
    /// <summary>
    /// Это расширение позволяет плагину проверять, вызывается ли он из основного потока хост-приложения.
    /// </summary>
    ThreadCheck,
    /// <summary>
    /// Это расширение позволяет плагину отправлять задачи в пул потоков, управляемый хост-приложением.
    /// Пул потоков может быть использован для выполнения фоновых задач без блокировки основного потока хост-приложения.
    /// </summary>
    ThreadPool,
    /// <summary>
    /// Это расширение позволяет плагину использовать таймеры для планирования задач на выполнение в определенное время.
    /// </summary>
    TimerSupport,
    /// <summary>
    /// Это расширение позволяет плагину управлять транспортом хост-приложения.
    /// Транспорт может быть использован для запуска, остановки и поиска аудио, которое обрабатывается.
    /// </summary>
    Transport,
    /// <summary>
    /// Это расширение позволяет плагину запрашивать настройку хост-приложения.
    /// Настройка может быть использована для корректировки высоты звука, который обрабатывается.
    /// </summary>
    Tuning,
    /// <summary>
    /// Это расширение позволяет плагину запрашивать информацию о голосах, которые обрабатываются.
    /// Информация о голосах может быть использована для управления поведением плагина.
    /// </summary>
    VoiceInfo,
}