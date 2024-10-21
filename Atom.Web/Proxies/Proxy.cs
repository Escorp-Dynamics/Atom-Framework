using System.Net;
using System.Text.Json.Serialization;

namespace Atom.Web.Proxies;

/// <summary>
/// Представляет расширенную реализацию <see cref="WebProxy"/>.
/// </summary>
[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
[JsonConverter(typeof(ProxyJsonConverter))]
public class Proxy : WebProxy
{
    private string Scheme => Type switch
    {
        ProxyType.Https => "https",
        ProxyType.Socks4 => "socks",
        ProxyType.Socks5 => "socks5",
        _ => "http",
    };

    /// <summary>
    /// Тип прокси-сервера.
    /// </summary>
    public ProxyType Type { get; set; }

    /// <summary>
    /// Уровень анонимности прокси-сервера.
    /// </summary>
    public AnonymityLevel Anonymity { get; set; }

    /// <summary>
    /// Хост прокси-сервера, если адрес не пустой, иначе пустая строка.
    /// </summary>
    [JsonInclude]
    public string Host
    {
        get => Address?.Host ?? string.Empty;

        set
        {
            if (string.IsNullOrEmpty(value)) return;
            Address = new Uri($"{Scheme}://{value}:{Port}");
        }
    }

    /// <summary>
    /// Порт прокси-сервера, если адрес не пустой, иначе 0.
    /// </summary>
    [JsonInclude]
    public int Port
    {
        get => Address?.Port ?? 0;

        set
        {
            if (value <= 0) return;
            if (string.IsNullOrEmpty(Host)) Host = "localhost";

            Address = new Uri($"{Scheme}://{Host}:{value}");
        }
    }

    /// <summary>
    /// Имя пользователя для аутентификации на прокси-сервере, если есть, иначе пустая строка.
    /// </summary>
    public string UserName
    {
        get => Credentials is NetworkCredential credentials ? credentials.UserName : string.Empty;

        set
        {
            if (string.IsNullOrEmpty(value)) return;

            var credentials = Credentials as NetworkCredential;
            credentials ??= new NetworkCredential();
            credentials.UserName = value;
            Credentials = credentials;
        }
    }

    /// <summary>
    /// Пароль для аутентификации на прокси-сервере, если есть, иначе пустая строка.
    /// </summary>
    public string Password
    {
        get => Credentials is NetworkCredential credentials ? credentials.Password : string.Empty;

        set
        {
            if (string.IsNullOrEmpty(value)) return;

            var credentials = Credentials as NetworkCredential;
            credentials ??= new NetworkCredential();
            credentials.Password = value;
            Credentials = credentials;
        }
    }

    /// <summary>
    /// Определяет, защищен ли прокси-сервер паролем и именем пользователя.
    /// </summary>
    [JsonIgnore]
    public bool IsProtected => !string.IsNullOrEmpty(UserName) && !string.IsNullOrEmpty(Password);

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Proxy"/>.
    /// </summary>
    /// <param name="host">Хост прокси-сервера.</param>
    /// <param name="port">Порт прокси-сервера.</param>
    /// <param name="userName">Имя пользователя для аутентификации на прокси-сервере.</param>
    /// <param name="password">Пароль для аутентификации на прокси-сервере.</param>
    public Proxy(string host, int port, string userName, string password) => (Host, Port, UserName, Password) = (host, port, userName, password);

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Proxy"/>.
    /// </summary>
    /// <param name="host">Хост прокси-сервера.</param>
    /// <param name="port">Порт прокси-сервера.</param>
    public Proxy(string host, int port) => (Host, Port) = (host, port);

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Proxy"/>.
    /// </summary>
    public Proxy() { }
}