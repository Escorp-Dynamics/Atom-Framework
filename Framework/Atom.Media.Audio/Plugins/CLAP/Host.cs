using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Media.Audio.Plugins.CLAP;


[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate nint GetExtension(nint host, string extensionId);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void RequestRestart(nint host);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void RequestProcess(nint host);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void RequestCallback(nint host);

/// <summary>
/// Структура, представляющая хост-приложение CLAP (Cross-Platform Audio Plugin).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct Host : IEquatable<Host>
{
    internal readonly nint clapVersion;

    internal readonly nint reserved;

    internal readonly nint name;
    internal readonly nint vendor;
    internal readonly nint url;
    internal readonly nint version;

    internal readonly GetExtension getExtension;
    internal readonly RequestRestart requestRestart;
    internal readonly RequestProcess requestProcess;
    internal readonly RequestCallback requestCallback;

    /// <summary>
    /// Возвращает версию CLAP.
    /// </summary>
    public Version ClapVersion => clapVersion.AsVersion();

    /// <summary>
    /// Название хоста.
    /// </summary>
    public string Name => name.AsString() ?? string.Empty;

    /// <summary>
    /// Производитель хоста.
    /// </summary>
    public string Vendor => name.AsString() ?? string.Empty;

    /// <summary>
    /// Ссылка на дистрибутив хоста.
    /// </summary>
    public Uri? Url
    {
        get
        {
            var tmp = url.AsString();
            return string.IsNullOrEmpty(tmp) ? default : new Uri(tmp);
        }
    }

    /// <summary>
    /// Версия хоста.
    /// </summary>
    public string Version => name.AsString() ?? string.Empty;

    /// <summary>
    /// Аудиопорты плагина CLAP.
    /// </summary>
    public readonly AudioPorts AudioPorts => GetAudioPorts();

    internal readonly nint GetExtension(string extensionId) => getExtension(this.AsPointer(), extensionId);

    internal readonly unsafe AudioPorts GetAudioPorts()
    {
        var extensionId = ClapExtension.Get(Extension.AudioPorts);
        var extensionPtr = GetExtension(extensionId);
        var audioPorts = Unsafe.Read<AudioPorts>(extensionPtr.ToPointer());
        //audioPorts.
        return default;  // TODO: Дописать.
    }

    /// <summary>
    /// Проверяет, равен ли текущий экземпляр другому экземпляру.
    /// </summary>
    /// <param name="other">Другой экземпляр для сравнения.</param>
    /// <returns>true, если текущий экземпляр равен другому экземпляру; в противном случае — false.</returns>
    public readonly bool Equals(Host other) => other.GetHashCode() == GetHashCode();

    /// <summary>
    /// Проверяет, равен ли текущий экземпляр другому экземпляру.
    /// </summary>
    /// <param name="obj">Другой экземпляр для сравнения.</param>
    /// <returns>true, если текущий экземпляр равен другому экземпляру; в противном случае — false.</returns>
    public override readonly bool Equals(object? obj)
    {
        if (obj is not Host other) return default;
        return Equals(other);
    }

    /// <summary>
    /// Возвращает хеш-код для текущего экземпляра.
    /// </summary>
    /// <returns>Хеш-код для текущего экземпляра.</returns>
    public override readonly int GetHashCode()
    {
        var hashCode = ClapVersion.GetHashCode();

        hashCode ^= Name.GetHashCode();
        hashCode ^= Vendor.GetHashCode();

        if (Url is not null) hashCode ^= Url.GetHashCode();

        hashCode ^= Version.GetHashCode();

        return hashCode;
    }

    /// <summary>
    /// Оператор равенства для сравнения двух дескрипторов плагинов CLAP.
    /// </summary>
    /// <param name="left">Левый дескриптор плагина CLAP.</param>
    /// <param name="right">Правый дескриптор плагина CLAP.</param>
    /// <returns>True, если дескрипторы равны, иначе - false.</returns>
    public static bool operator ==(Host left, Host right) => left.Equals(right);

    /// <summary>
    /// Оператор не равенства для сравнения двух дескрипторов плагинов CLAP.
    /// </summary>
    /// <param name="left">Левый дескриптор плагина CLAP.</param>
    /// <param name="right">Правый дескриптор плагина CLAP.</param>
    /// <returns>True, если дескрипторы не равны, иначе - false.</returns>
    public static bool operator !=(Host left, Host right) => !(left == right);
}