using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using Atom.Architect.Builders;
using Atom.Buffers;

namespace Atom.Net.Https;

/// <summary>
/// Представляет строителя ссылок.
/// </summary>
public partial class UrlBuilder : IBuilder<Uri, UrlBuilder>
{
    private const string DefaultScheme = "http";
    private const string DefaultHost = "localhost";

    private readonly Dictionary<string, List<string>> parameters = [];
    private readonly List<string> paths = [];
    private string scheme = DefaultScheme;
    private string host = DefaultHost;
    private int port;
    private bool isEndingSlashEnabled;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendQueryParameters(StringBuilder sb)
    {
        if (parameters.Count is 0) return;

        sb.Append('?');
        var isFirstParam = true;

        foreach (var kv in parameters) AppendKeyValuePairs(sb, kv, ref isFirstParam);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendTrailingSlashIfNeeded(StringBuilder sb)
    {
        if (Volatile.Read(ref isEndingSlashEnabled)) sb.Append('/');
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendPaths(StringBuilder sb)
    {
        foreach (var path in paths) sb.Append('/').Append(path);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendPortIfNeeded(StringBuilder sb)
    {
        if (port > 0) sb.Append(':').Append(port);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void BuildSchemeAndHost(StringBuilder sb) => sb.Append(Volatile.Read(ref scheme))
        .Append("://")
        .Append(Volatile.Read(ref host));

    /// <summary>
    /// Добавляет или заменяет схему ссылки.
    /// </summary>
    /// <param name="scheme">Схема.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual UrlBuilder WithScheme(string scheme)
    {
        Volatile.Write(ref this.scheme, scheme);
        return this;
    }

    /// <summary>
    /// Добавляет или заменяет хост ссылки.
    /// </summary>
    /// <param name="host">Хост.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual UrlBuilder WithHost(string host)
    {
        Volatile.Write(ref this.host, host);
        return this;
    }

    /// <summary>
    /// Добавляет или заменяет номер порта ссылки.
    /// </summary>
    /// <param name="port">Номер порта.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual UrlBuilder WithPort(int port)
    {
        Volatile.Write(ref this.port, port);
        return this;
    }

    /// <summary>
    /// Включает завершающий слэш.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual UrlBuilder WithEndingSlash()
    {
        Volatile.Write(ref isEndingSlashEnabled, value: true);
        return this;
    }

    /// <summary>
    /// Добавляет путь.
    /// </summary>
    /// <param name="paths">Значения путей.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual UrlBuilder WithPath([NotNull] params IEnumerable<string> paths)
    {
        foreach (var path in paths) this.paths.Add(path.Trim('/'));
        return this;
    }

    /// <summary>
    /// Добавляет параметр.
    /// </summary>
    /// <param name="name">Имя параметра.</param>
    /// <param name="values">Значения параметра.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual UrlBuilder WithParameter(string name, params IEnumerable<string> values)
    {
        if (!parameters.TryGetValue(name, out var v)) v = [];

        v.AddRange(values);
        parameters.Add(name, v);

        return this;
    }

    /// <inheritdoc/>
    [Pooled]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void Reset()
    {
        parameters.Clear();
        paths.Clear();

        Volatile.Write(ref scheme, DefaultScheme);
        Volatile.Write(ref host, DefaultHost);
        Volatile.Write(ref port, default);
        Volatile.Write(ref isEndingSlashEnabled, default);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual Uri Build()
    {
        var sb = ObjectPool<StringBuilder>.Shared.Rent();

        BuildSchemeAndHost(sb);
        AppendPortIfNeeded(sb);
        AppendPaths(sb);
        AppendTrailingSlashIfNeeded(sb);
        AppendQueryParameters(sb);

        var url = sb.ToString();
        ObjectPool<StringBuilder>.Shared.Return(sb, x => x.Clear());

        return new Uri(url);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override string ToString() => Build().OriginalString;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static UrlBuilder Create() => ObjectPool<UrlBuilder>.Shared.Rent();

    static IBuilder<Uri> IBuilder<Uri>.Create() => Create();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static IBuilder IBuilder.Create() => Create();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AppendParam(StringBuilder sb, string key, string? value, ref bool isFirstParam, bool isMulti = false)
    {
        if (!isFirstParam) sb.Append('&');

        sb.Append(key);

        if (isMulti) sb.Append("[]");
        if (!string.IsNullOrEmpty(value)) sb.Append('=').Append(value);

        isFirstParam = false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AppendKeyValuePairs(StringBuilder sb, KeyValuePair<string, List<string>> kv, ref bool isFirstParam)
    {
        var key = kv.Key;
        var values = kv.Value;

        if (values.Count is 0)
        {
            AppendParam(sb, key, default, ref isFirstParam);
            return;
        }

        foreach (var value in values) AppendParam(sb, key, value, ref isFirstParam, isMulti: values.Count > 1);
    }

    /// <summary>
    /// Преобразует <see cref="Uri"/> в <see cref="UrlBuilder"/>.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static UrlBuilder FromUri([NotNull] Uri url) => Create()
        .WithScheme(url.Scheme)
        .WithHost(url.Host)
        .WithPort(url.Port)
        .WithPath(url.PathAndQuery);

    /// <summary>
    /// Преобразует <see cref="Uri"/> в <see cref="UrlBuilder"/>.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator UrlBuilder(Uri url) => FromUri(url);
}