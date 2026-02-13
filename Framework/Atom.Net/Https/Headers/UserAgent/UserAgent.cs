using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Atom.Text;

namespace Atom.Net.Https.Headers;

/// <summary>
/// Представляет информацию об агенте пользователя.
/// </summary>
public readonly struct UserAgent : IParsable<UserAgent>, IEquatable<UserAgent>
{
    /// <summary>
    /// Устаревший токен.
    /// </summary>
    public Versioned LegacyToken { get; init; }

    /// <summary>
    /// Информация об операционной системе.
    /// </summary>
    public OsInfo OperatingSystem { get; init; }

    /// <summary>
    /// Браузерный движок отрисовки.
    /// </summary>
    public string RenderingEngine { get; init; } = string.Empty;

    /// <summary>
    /// Информация о совместимости (например, "KHTML, like Gecko").
    /// </summary>
    public string Compatibility { get; init; } = string.Empty;

    /// <summary>
    /// Основной клиент (например, Chrome/134.0.0.0).
    /// </summary>
    public Versioned PrimaryClient { get; init; }

    /// <summary>
    /// Дополнительные клиенты в цепочке (например, Safari/537.36, Edg/134.0.0.0).
    /// </summary>
    public IEnumerable<Versioned> ClientChain { get; init; } = [];

    /// <summary>
    /// Дополнительные токены без версии (например, Mobile).
    /// </summary>
    public IEnumerable<string> Tokens { get; init; } = [];

    /// <summary>
    /// Возвращает пустой User-Agent.
    /// </summary>
    public static UserAgent Empty { get; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="UserAgent"/>.
    /// </summary>
    /// <param name="header">Заголовок агента пользователя.</param>
    /// <param name="provider">Параметры форматирования.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public UserAgent(string header, IFormatProvider? provider)
    {
        if (string.IsNullOrEmpty(header)) return;

        var ua = Parse(header, provider);
        LegacyToken = ua.LegacyToken;
        OperatingSystem = ua.OperatingSystem;
        RenderingEngine = ua.RenderingEngine;
        Compatibility = ua.Compatibility;
        PrimaryClient = ua.PrimaryClient;
        ClientChain = ua.ClientChain;
        Tokens = ua.Tokens;
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="UserAgent"/>.
    /// </summary>
    /// <param name="header">Заголовок агента пользователя.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public UserAgent(string header) : this(header, default) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="UserAgent"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public UserAgent() : this(string.Empty) { }

    /// <inheritdoc/>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(default)] out UserAgent result)
    {
        result = default;
        if (string.IsNullOrEmpty(s)) return default;

        var span = s.AsSpan().Trim();
        if (span.IsEmpty) return default;

        var parser = new Parser(span, provider);
        return parser.TryBuild(out result);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static UserAgent Parse(string s, IFormatProvider? provider)
    {
        if (!TryParse(s, provider, out var ua)) throw new InvalidOperationException("Входная строка не является агентом пользователя");
        return ua;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode()
    {
        var clientsHash = 0;
        foreach (var client in ClientChain ?? []) clientsHash = HashCode.Combine(clientsHash, client);

        var tokensHash = 0;
        foreach (var token in Tokens ?? []) tokensHash = HashCode.Combine(tokensHash, token);

        return HashCode.Combine
        (
            LegacyToken,
            OperatingSystem,
            RenderingEngine,
            Compatibility,
            PrimaryClient,
            clientsHash,
            tokensHash
        );
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(UserAgent other)
        => LegacyToken.Equals(other.LegacyToken)
        && OperatingSystem.Equals(other.OperatingSystem)
        && string.Equals(RenderingEngine, other.RenderingEngine, StringComparison.Ordinal)
        && string.Equals(Compatibility, other.Compatibility, StringComparison.Ordinal)
        && PrimaryClient.Equals(other.PrimaryClient)
        && SequenceEquals(ClientChain, other.ClientChain, EqualityComparer<Versioned>.Default)
        && SequenceEquals(Tokens, other.Tokens, StringComparer.Ordinal);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj is UserAgent other && Equals(other);

    /// <inheritdoc/>
    public override string ToString()
    {
        var builder = new ValueStringBuilder(128);

        void AppendSeparator(ref ValueStringBuilder sb)
        {
            if (sb.Length > 0) sb.Append(' ');
        }

        void AppendVersioned(ref ValueStringBuilder sb, in Versioned versioned)
        {
            var name = versioned.Name;
            if (string.IsNullOrEmpty(name)) return;

            AppendSeparator(ref sb);
            sb.Append(name);
            sb.Append('/');
            sb.Append(versioned.Version);
        }

        void AppendToken(ref ValueStringBuilder sb, string? token)
        {
            if (string.IsNullOrEmpty(token)) return;

            AppendSeparator(ref sb);
            sb.Append(token);
        }

        AppendVersioned(ref builder, LegacyToken);

        var os = OperatingSystem.ToString();

        if (!string.IsNullOrEmpty(os))
        {
            AppendSeparator(ref builder);
            builder.Append('(');
            builder.Append(os);
            builder.Append(')');
        }

        AppendToken(ref builder, RenderingEngine);

        if (!string.IsNullOrEmpty(Compatibility))
        {
            AppendSeparator(ref builder);
            builder.Append('(');
            builder.Append(Compatibility);
            builder.Append(')');
        }

        AppendVersioned(ref builder, PrimaryClient);

        if (ClientChain is not null)
        {
            foreach (var client in ClientChain)
            {
                AppendVersioned(ref builder, client);
            }
        }

        if (Tokens is not null)
        {
            foreach (var token in Tokens)
            {
                AppendToken(ref builder, token);
            }
        }

        var result = builder.ToString();
        builder.Dispose();

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool SequenceEquals<T>(IEnumerable<T>? first, IEnumerable<T>? second, IEqualityComparer<T> comparer)
    {
        if (ReferenceEquals(first, second)) return true;
        if (first is null || second is null) return default;

        if (first is ICollection<T> firstCollection && second is ICollection<T> secondCollection)
        {
            var count = firstCollection.Count;
            if (count != secondCollection.Count) return default;
            if (count is 0) return true;
        }

        using var firstEnumerator = first.GetEnumerator();
        using var secondEnumerator = second.GetEnumerator();

        while (firstEnumerator.MoveNext())
        {
            if (!secondEnumerator.MoveNext()) return default;
            if (!comparer.Equals(firstEnumerator.Current, secondEnumerator.Current)) return default;
        }

        return !secondEnumerator.MoveNext();
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(UserAgent left, UserAgent right) => left.Equals(right);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(UserAgent left, UserAgent right) => !(left == right);
}

file ref struct Parser
{
    private readonly ReadOnlySpan<char> span;
    private readonly IFormatProvider? provider;
    private readonly int length;
    private int index;

    private OsInfo operatingSystem;
    private List<Versioned>? clientChain;
    private List<string>? tokens;
    private Versioned primaryClient;
    private bool hasPrimaryClient;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Parser(ReadOnlySpan<char> span, IFormatProvider? provider)
    {
        this.span = span;
        this.provider = provider;
        length = span.Length;
        index = 0;
        operatingSystem = default;
        clientChain = null;
        tokens = null;
        primaryClient = default;
        hasPrimaryClient = false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryBuild(out UserAgent result)
    {
        result = default;

        if (!TryReadVersioned(stopOnParenthesis: true, out var legacyToken))
            return default;

        SkipWhitespace();
        ParseOperatingSystem();
        SkipWhitespace();

        var renderingEngine = ReadToken();

        SkipWhitespace();
        var compatibility = ReadParenthesized();

        SkipWhitespace();
        ParseClients();

        result = new UserAgent
        {
            LegacyToken = legacyToken,
            OperatingSystem = operatingSystem,
            RenderingEngine = renderingEngine,
            Compatibility = compatibility,
            PrimaryClient = primaryClient,
            ClientChain = clientChain is null or { Count: 0 } ? [] : clientChain.ToArray(),
            Tokens = tokens is null or { Count: 0 } ? [] : tokens.ToArray(),
        };

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ParseOperatingSystem()
    {
        if (index >= length || span[index] != '(') return;

        var osContent = ReadParenthesized();
        if (string.IsNullOrEmpty(osContent)) return;

        if (OsInfo.TryParse(osContent, provider, out operatingSystem)) return;

        var parsedTokens = SplitTokens(osContent.AsSpan());
        if (parsedTokens.Length > 0) operatingSystem = new OsInfo { Tokens = parsedTokens };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ParseClients()
    {
        while (index < length)
        {
            SkipWhitespace();
            if (index >= length) break;

            var tokenSpan = ReadTokenSpan();
            if (tokenSpan.IsEmpty) continue;

            if (TryConvertVersioned(tokenSpan, out var versioned))
            {
                if (!hasPrimaryClient)
                {
                    hasPrimaryClient = true;
                    primaryClient = versioned;
                }
                else
                {
                    (clientChain ??= new List<Versioned>(4)).Add(versioned);
                }
            }
            else
            {
                (tokens ??= new List<string>(4)).Add(tokenSpan.ToString());
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SkipWhitespace()
    {
        while (index < length && char.IsWhiteSpace(span[index])) index++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryReadVersioned(bool stopOnParenthesis, out Versioned versioned)
    {
        versioned = default;
        var start = index;
        var slashIndex = -1;

        while (index < length)
        {
            var ch = span[index];

            if (ch is '/')
                slashIndex = index;
            else if (char.IsWhiteSpace(ch) || (stopOnParenthesis && ch is '('))
                break;

            index++;
        }

        if (index == start || slashIndex <= start || slashIndex >= index) return default;

        var nameSpan = span[start..slashIndex];
        var versionSpan = span[(slashIndex + 1)..index];

        var versionSlice = TrimVersion(versionSpan);
        if (versionSlice.IsEmpty) return default;

        var versionString = versionSlice.ToString();
        if (!Version.TryParse(versionString, out _)) return default;

        versioned = new Versioned(nameSpan.ToString(), versionString);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string ReadToken()
    {
        var tokenSpan = ReadTokenSpan();
        return tokenSpan.Length is 0 ? string.Empty : tokenSpan.ToString();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ReadOnlySpan<char> ReadTokenSpan()
    {
        var start = index;
        while (index < length && !char.IsWhiteSpace(span[index]) && span[index] is not '(' and not ')') index++;
        return span[start..index];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string ReadParenthesized()
    {
        if (index >= length || span[index] is not '(') return string.Empty;

        index++;
        var start = index;
        var depth = 1;

        while (index < length && depth > 0)
        {
            var ch = span[index];

            if (ch is '(')
            {
                depth++;
            }
            else if (ch is ')')
            {
                depth--;
                if (depth is 0) break;
            }

            index++;
        }

        if (depth is not 0) return string.Empty;

        var content = span[start..index];
        index++;
        return content.Length is 0 ? string.Empty : content.ToString();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryConvertVersioned(ReadOnlySpan<char> token, out Versioned versioned)
    {
        versioned = default;
        var slashIndex = token.IndexOf('/');
        if (slashIndex <= 0 || slashIndex >= token.Length - 1) return default;

        var nameSpan = token[..slashIndex];
        var versionSpan = token[(slashIndex + 1)..];
        var versionSlice = TrimVersion(versionSpan);
        if (versionSlice.IsEmpty) return default;

        var versionString = versionSlice.ToString();
        if (!Version.TryParse(versionString, out _)) return default;

        versioned = new Versioned(nameSpan.ToString(), versionString);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string[] SplitTokens(ReadOnlySpan<char> span)
    {
        if (span.IsEmpty) return [];

        var list = new List<string>(4);
        var start = 0;

        while (start < span.Length)
        {
            while (start < span.Length && char.IsWhiteSpace(span[start])) start++;
            if (start >= span.Length) break;

            var end = start;
            while (end < span.Length && span[end] != ';') end++;

            var tokenSpan = span[start..end].Trim();
            if (!tokenSpan.IsEmpty) list.Add(tokenSpan.ToString());

            start = end + 1;
        }

        return list.Count is 0 ? [] : [.. list];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlySpan<char> TrimVersion(ReadOnlySpan<char> span)
    {
        var start = 0;
        var end = span.Length;

        while (start < end && char.IsWhiteSpace(span[start])) start++;
        while (end > start && char.IsWhiteSpace(span[end - 1])) end--;

        while (end > start)
        {
            var last = span[end - 1];

            if (last is '.' or ';' or ',' or ')')
            {
                end--;
                continue;
            }

            break;
        }

        return start >= end ? [] : span[start..end];
    }
}
