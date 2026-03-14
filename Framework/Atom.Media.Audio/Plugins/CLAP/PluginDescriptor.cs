using System.Runtime.InteropServices;

namespace Atom.Media.Audio.Plugins.CLAP;

/// <summary>
/// Описание дескриптора плагина CLAP.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct PluginDescriptor : IEquatable<PluginDescriptor>
{
    private uint clapVersion;
    private nint id;
    private nint name;
    private nint vendor;
    private nint url;
    private nint manualUrl;
    private nint supportUrl;
    private nint version;
    private nint description;
    private nint features;

    /// <summary>
    /// Версия CLAP, поддерживаемая плагином.
    /// </summary>
    public uint ClapVersion
    {
        readonly get => clapVersion;
        set => clapVersion = value;
    }

    /// <summary>
    /// Уникальный идентификатор плагина.
    /// </summary>
    public string Id
    {
        readonly get => id.AsString() ?? string.Empty;
        set => value.ToPointer(ref id);
    }

    /// <summary>
    /// Название плагина.
    /// </summary>
    public string Name
    {
        readonly get => name.AsString() ?? string.Empty;
        set => value.ToPointer(ref name);
    }

    /// <summary>
    /// Производитель плагина.
    /// </summary>
    public string Vendor
    {
        readonly get => vendor.AsString() ?? string.Empty;
        set => value.ToPointer(ref vendor);
    }

    /// <summary>
    /// URL-адрес веб-сайта производителя плагина.
    /// </summary>
    public Uri? Url
    {
        readonly get
        {
            var tmp = url.AsString();
            if (string.IsNullOrEmpty(tmp)) return default;
            return new Uri(tmp);
        }

        set
        {
            var tmp = value?.AbsoluteUri;
            tmp.ToPointer(ref url);
        }
    }

    /// <summary>
    /// URL-адрес руководства пользователя для плагина.
    /// </summary>
    public Uri? ManualUrl
    {
        readonly get
        {
            var tmp = manualUrl.AsString();
            if (string.IsNullOrEmpty(tmp)) return default;
            return new Uri(tmp);
        }

        set
        {
            var tmp = value?.AbsoluteUri;
            tmp.ToPointer(ref manualUrl);
        }
    }

    /// <summary>
    /// URL-адрес страницы поддержки для плагина.
    /// </summary>
    public Uri? SupportUrl
    {
        readonly get
        {
            var tmp = supportUrl.AsString();
            if (string.IsNullOrEmpty(tmp)) return default;
            return new Uri(tmp);
        }

        set
        {
            var tmp = value?.AbsoluteUri;
            tmp.ToPointer(ref supportUrl);
        }
    }

    /// <summary>
    /// Версия плагина.
    /// </summary>
    public string Version
    {
        readonly get => version.AsString() ?? string.Empty;
        set => value.ToPointer(ref version);
    }

    /// <summary>
    /// Описание плагина.
    /// </summary>
    public string Description
    {
        readonly get => description.AsString() ?? string.Empty;
        set => value.ToPointer(ref description);
    }

    /// <summary>
    /// Список функций, поддерживаемых плагином.
    /// </summary>
    public IEnumerable<string> Features
    {
        readonly get
        {
            foreach (var item in features.AsEnumerable<string>()) yield return item ?? string.Empty;
            yield break;
        }

        set => value.ToPointer(ref features);
    }

    /// <summary>
    /// Проверяет, равен ли текущий экземпляр другому экземпляру.
    /// </summary>
    /// <param name="other">Другой экземпляр для сравнения.</param>
    /// <returns>true, если текущий экземпляр равен другому экземпляру; в противном случае — false.</returns>
    public readonly bool Equals(PluginDescriptor other) => other.GetHashCode() == GetHashCode();

    /// <summary>
    /// Проверяет, равен ли текущий экземпляр другому экземпляру.
    /// </summary>
    /// <param name="obj">Другой экземпляр для сравнения.</param>
    /// <returns>true, если текущий экземпляр равен другому экземпляру; в противном случае — false.</returns>
    public override readonly bool Equals(object? obj)
    {
        if (obj is not PluginDescriptor other) return default;
        return Equals(other);
    }

    /// <summary>
    /// Возвращает хеш-код для текущего экземпляра.
    /// </summary>
    /// <returns>Хеш-код для текущего экземпляра.</returns>
    public override readonly int GetHashCode() => clapVersion.GetHashCode()
        ^ Id.GetHashCode(StringComparison.InvariantCultureIgnoreCase)
        ^ Name.GetHashCode(StringComparison.InvariantCultureIgnoreCase)
        ^ Vendor.GetHashCode(StringComparison.InvariantCultureIgnoreCase)
        ^ Version.GetHashCode(StringComparison.InvariantCultureIgnoreCase)
        ^ Description.GetHashCode(StringComparison.InvariantCultureIgnoreCase)
        ^ Features.GetHashCode();

    /// <summary>
    /// Оператор равенства для сравнения двух дескрипторов плагинов CLAP.
    /// </summary>
    /// <param name="left">Левый дескриптор плагина CLAP.</param>
    /// <param name="right">Правый дескриптор плагина CLAP.</param>
    /// <returns>True, если дескрипторы равны, иначе - false.</returns>
    public static bool operator ==(PluginDescriptor left, PluginDescriptor right) => left.Equals(right);

    /// <summary>
    /// Оператор не равенства для сравнения двух дескрипторов плагинов CLAP.
    /// </summary>
    /// <param name="left">Левый дескриптор плагина CLAP.</param>
    /// <param name="right">Правый дескриптор плагина CLAP.</param>
    /// <returns>True, если дескрипторы не равны, иначе - false.</returns>
    public static bool operator !=(PluginDescriptor left, PluginDescriptor right) => !(left == right);
}