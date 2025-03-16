using System.Runtime.InteropServices;
using Atom.Audio.Plugins.CLAP.Extensions.Ambisonic;

namespace Atom.Audio.Plugins.CLAP.Extensions;

/// <summary>
/// Возвращает true, если указанная конфигурация поддерживается.
/// [main-thread]
/// </summary>
/// <param name="plugin">Указатель на плагин</param>
/// <param name="config">Конфигурация амбисонического аудио</param>
/// <returns>True, если конфигурация поддерживается, иначе false</returns>
internal delegate bool PluginAmbisonicIsConfigSupported(nint plugin, nint config);

/// <summary>
/// Возвращает true в случае успеха.
/// config_id: идентификатор конфигурации, см. clap_plugin_audio_ports_config.
/// Если config_id равен CLAP_INVALID_ID, то эта функция запрашивает текущую информацию о портах.
/// [main-thread]
/// </summary>
/// <param name="plugin">Указатель на плагин</param>
/// <param name="isInput">Флаг, указывающий, является ли порт входным или выходным</param>
/// <param name="portIndex">Индекс порта</param>
/// <param name="config">Конфигурация амбисонического аудио</param>
/// <returns>True в случае успеха, иначе false</returns>
internal delegate bool PluginAmbisonicGetConfig(nint plugin, bool isInput, uint portIndex, nint config);

/// <summary>
/// Определяет интерфейс для амбисонического аудио в CLAP плагине.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct PluginAmbisonic : IEquatable<PluginAmbisonic>
{
    private nint plugin;

    /// <summary>
    /// Указатель на функцию, которая проверяет, поддерживается ли указанная конфигурация.
    /// </summary>
    internal PluginAmbisonicIsConfigSupported isConfigSupported;

    /// <summary>
    /// Указатель на функцию, которая возвращает конфигурацию амбисонического аудио.
    /// </summary>
    internal PluginAmbisonicGetConfig getConfig;
    
    internal nint Plugin 
    {
        readonly get => plugin;
        set => plugin = value;
    }

    /// <summary>
    /// Определяет, поддерживаются ли заданные настройки.
    /// </summary>
    /// <param name="config">Экземпляр настроек.</param>
    /// <returns>True, если настройки поддерживаются, иначе false.</returns>
    public readonly bool IsConfigSupported(AmbisonicConfig config) => isConfigSupported(plugin, config.AsPointer());

    /// <summary>
    /// Обновляет настройки.
    /// </summary>
    /// <param name="isInput">Указывает, является ли порт входным.</param>
    /// <param name="portIndex">Индекс порта.</param>
    /// <param name="config">Ссылка на экземпляр настроек.</param>
    /// <returns>True, если операция была успешна, иначе false.</returns>
    public readonly bool GetConfig(bool isInput, uint portIndex, ref AmbisonicConfig config) => getConfig(plugin, isInput, portIndex, config.AsPointer());
    
    /// <summary>
    /// Проверяет, равен ли текущий экземпляр другому экземпляру.
    /// </summary>
    /// <param name="other">Другой экземпляр для сравнения.</param>
    /// <returns>true, если текущий экземпляр равен другому экземпляру; в противном случае — false.</returns>
    public readonly bool Equals(PluginAmbisonic other) => other.GetHashCode() == GetHashCode();

    /// <summary>
    /// Проверяет, равен ли текущий экземпляр другому экземпляру.
    /// </summary>
    /// <param name="obj">Другой экземпляр для сравнения.</param>
    /// <returns>true, если текущий экземпляр равен другому экземпляру; в противном случае — false.</returns>
    public override readonly bool Equals(object? obj)
    {
        if (obj is not PluginAmbisonic other) return default;
        return Equals(other);
    }

    /// <summary>
    /// Возвращает хеш-код для текущего экземпляра.
    /// </summary>
    /// <returns>Хеш-код для текущего экземпляра.</returns>
    public override readonly int GetHashCode() => isConfigSupported.GetHashCode() ^ getConfig.GetHashCode();

    /// <summary>
    /// Оператор равенства для сравнения двух дескрипторов плагинов CLAP.
    /// </summary>
    /// <param name="left">Левый дескриптор плагина CLAP.</param>
    /// <param name="right">Правый дескриптор плагина CLAP.</param>
    /// <returns>True, если дескрипторы равны, иначе - false.</returns>
    public static bool operator ==(PluginAmbisonic left, PluginAmbisonic right) => left.Equals(right);

    /// <summary>
    /// Оператор не равенства для сравнения двух дескрипторов плагинов CLAP.
    /// </summary>
    /// <param name="left">Левый дескриптор плагина CLAP.</param>
    /// <param name="right">Правый дескриптор плагина CLAP.</param>
    /// <returns>True, если дескрипторы не равны, иначе - false.</returns>
    public static bool operator !=(PluginAmbisonic left, PluginAmbisonic right) => !(left == right);
}