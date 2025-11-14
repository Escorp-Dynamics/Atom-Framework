using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;

namespace Atom.Net.Https.Headers;

/// <summary>
/// Представляет данные об операционной системе.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="OsInfo"/>.
/// </remarks>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public readonly struct OsInfo() : IParsable<OsInfo>, IEquatable<OsInfo>
{
    /// <summary>
    /// Название платформы (например, Windows NT или Mac OS X).
    /// </summary>
    public string Platform { get; init; } = string.Empty;

    /// <summary>
    /// Версия платформы (например, 10.0 или 10_15_7).
    /// </summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>
    /// Устройство или класс устройства (например, Macintosh или Pixel 5).
    /// </summary>
    public string Device { get; init; } = string.Empty;

    /// <summary>
    /// Региональная локаль (например, en-US).
    /// </summary>
    public string Locale { get; init; } = string.Empty;

    /// <summary>
    /// Маркер уровня безопасности (например, Win64).
    /// </summary>
    public string Security { get; init; } = string.Empty;

    /// <summary>
    /// Архитектура CPU (например, x86_64, x64, Intel).
    /// </summary>
    public string Architecture { get; init; } = string.Empty;

    /// <summary>
    /// Расположение архитектуры относительно остальных токенов.
    /// </summary>
    public ArchitectureTokenPlacement ArchitecturePlacement { get; init; } = ArchitectureTokenPlacement.Auto;

    /// <summary>
    /// Определяет, следует ли выводить устройство до платформы при форматировании.
    /// </summary>
    public bool IsDeviceFirst { get; init; }

    /// <summary>
    /// Нормализованный набор токенов операционной системы в исходном порядке.
    /// </summary>
    public IEnumerable<string> Tokens { get; init; } = [];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string BuildPlatformToken(ArchitectureTokenPlacement placement)
    {
        var hasPlatform = !string.IsNullOrEmpty(Platform);
        var hasVersion = !string.IsNullOrEmpty(Version);
        var hasArchitecture = !string.IsNullOrEmpty(Architecture);

        if (!hasPlatform && !hasVersion && !(hasArchitecture && placement is ArchitectureTokenPlacement.Prefix or ArchitectureTokenPlacement.Suffix))
            return string.Empty;

        var builder = new StringBuilder(Platform.Length + Version.Length + Architecture.Length + 2);

        if (placement is ArchitectureTokenPlacement.Prefix && hasArchitecture)
        {
            builder.Append(Architecture);
            if (hasPlatform || hasVersion) builder.Append(' ');
        }

        if (hasPlatform) builder.Append(Platform);

        if (hasVersion)
        {
            if (builder.Length > 0) builder.Append(' ');
            builder.Append(Version);
        }

        if (placement is ArchitectureTokenPlacement.Suffix && hasArchitecture)
        {
            if (builder.Length > 0) builder.Append(' ');
            builder.Append(Architecture);
        }

        return builder.ToString();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ArchitectureTokenPlacement ResolvePlacement() =>
        string.IsNullOrEmpty(Architecture)
            ? ArchitectureTokenPlacement.None
            : ArchitecturePlacement switch
            {
                ArchitectureTokenPlacement.Auto => InferPlacement(),
                _ => ArchitecturePlacement,
            };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ArchitectureTokenPlacement InferPlacement()
    {
        if (string.IsNullOrEmpty(Architecture)) return ArchitectureTokenPlacement.None;

        if (Architecture.Equals("Intel", StringComparison.OrdinalIgnoreCase)
            || Architecture.Equals("PPC", StringComparison.OrdinalIgnoreCase))
            return ArchitectureTokenPlacement.Prefix;

        if (Security.Length is 0 && Device.Length is 0 && !Platform.Equals("Windows NT", StringComparison.OrdinalIgnoreCase))
            return ArchitectureTokenPlacement.Suffix;

        return ArchitectureTokenPlacement.Separate;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Platform, StringComparer.Ordinal);
        hash.Add(Version, StringComparer.Ordinal);
        hash.Add(Device, StringComparer.Ordinal);
        hash.Add(Locale, StringComparer.Ordinal);
        hash.Add(Security, StringComparer.Ordinal);
        hash.Add(Architecture, StringComparer.Ordinal);
        hash.Add(IsDeviceFirst);
        hash.Add(ArchitecturePlacement);

        if (Tokens.Any())
        {
            foreach (var token in Tokens) hash.Add(token, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(OsInfo other)
        => Platform.Equals(other.Platform, StringComparison.Ordinal)
        && Version.Equals(other.Version, StringComparison.Ordinal)
        && Device.Equals(other.Device, StringComparison.Ordinal)
        && Locale.Equals(other.Locale, StringComparison.Ordinal)
        && Security.Equals(other.Security, StringComparison.Ordinal)
        && Architecture.Equals(other.Architecture, StringComparison.Ordinal)
        && ArchitecturePlacement.Equals(other.ArchitecturePlacement)
        && IsDeviceFirst.Equals(other.IsDeviceFirst)
        && TokensEquals(Tokens, other.Tokens);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj is OsInfo other && Equals(other);

    /// <inheritdoc/>
    public override string ToString()
    {
        if (Tokens.Any()) return string.Join("; ", Tokens);

        var placement = ResolvePlacement();
        var platformToken = BuildPlatformToken(placement);

        var estimatedLength =
            platformToken.Length
            + (string.IsNullOrEmpty(Device) ? 0 : Device.Length)
            + (string.IsNullOrEmpty(Security) ? 0 : Security.Length)
            + (string.IsNullOrEmpty(Locale) ? 0 : Locale.Length)
            + (placement is ArchitectureTokenPlacement.Separate && !string.IsNullOrEmpty(Architecture) ? Architecture.Length : 0);

        var builder = new StringBuilder(Math.Max(estimatedLength + 10, 16));

        void AppendToken(string? token)
        {
            if (string.IsNullOrEmpty(token)) return;
            if (builder.Length > 0) builder.Append("; ");
            builder.Append(token);
        }

        if (IsDeviceFirst) AppendToken(Device);
        AppendToken(platformToken);
        if (!IsDeviceFirst) AppendToken(Device);
        AppendToken(Security);
        if (placement is ArchitectureTokenPlacement.Separate) AppendToken(Architecture);
        AppendToken(Locale);

        return builder.ToString();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TokensEquals(IEnumerable<string> first, IEnumerable<string> second)
    {
        if (ReferenceEquals(first, second)) return true;
        return first.SequenceEqual(second, StringComparer.Ordinal);
    }

    /// <inheritdoc/>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(default)] out OsInfo result)
    {
        result = default;
        if (string.IsNullOrEmpty(s)) return default;

        var parser = new Parser(s.AsSpan());
        return parser.TryBuild(out result);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static OsInfo Parse(string s, IFormatProvider? provider)
    {
        if (!TryParse(s, provider, out var info)) throw new InvalidOperationException("Входная строка не является информацией об операционной системе");
        return info;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(OsInfo left, OsInfo right) => left.Equals(right);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(OsInfo left, OsInfo right) => !left.Equals(right);
}

file ref struct Parser
{
    private readonly ReadOnlySpan<char> source;
    private int offset;
    private int index;
    private readonly List<string> tokens;

    private string platform;
    private string version;
    private string device;
    private string locale;
    private string security;
    private string architecture;
    private ArchitectureTokenPlacement architecturePlacement;

    private int platformIndex;
    private int deviceIndex;

    private bool platformSet;
    private bool versionSet;
    private bool deviceSet;
    private bool localeSet;
    private bool securitySet;
    private bool architectureSet;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Parser(ReadOnlySpan<char> source)
    {
        this.source = source;
        offset = 0;
        index = 0;
        tokens = new List<string>(4);

        platform = string.Empty;
        version = string.Empty;
        device = string.Empty;
        locale = string.Empty;
        security = string.Empty;
        architecture = string.Empty;
        architecturePlacement = ArchitectureTokenPlacement.Auto;

        platformIndex = -1;
        deviceIndex = -1;

        platformSet = false;
        versionSet = false;
        deviceSet = false;
        localeSet = false;
        securitySet = false;
        architectureSet = false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryBuild(out OsInfo result)
    {
        while (TryReadNext(out var token))
        {
            ProcessToken(token);
            index++;
        }

        ApplyFallbacks();

        result = new OsInfo
        {
            Platform = platform,
            Version = version,
            Device = device,
            Locale = locale,
            Security = security,
            Architecture = architecture,
            ArchitecturePlacement = architecturePlacement,
            IsDeviceFirst = deviceIndex >= 0 && (platformIndex < 0 || deviceIndex < platformIndex),
            Tokens = tokens.ToArray(),
        };

        return tokens.Count > 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ProcessToken(ReadOnlySpan<char> token)
    {
        var trimmed = Trim(token);

        if (trimmed.IsEmpty)
        {
            tokens.Add(string.Empty);
            return;
        }

        var tokenString = trimmed.ToString();
        tokens.Add(tokenString);

        if (TryHandleSecurity(trimmed)) return;
        if (TryHandleLocale(trimmed)) return;

        var content = StripArchitecture(trimmed, ref architecture, ref architectureSet, ref architecturePlacement);

        if (content.IsEmpty)
        {
            if (architectureSet) return;
            return;
        }

        if (TryHandlePlatform(content))
        {
            platformIndex = index;
            return;
        }

        if (TryHandleDevice(content))
        {
            deviceIndex = index;
            return;
        }

        if (TryHandleFallbackPlatform(content))
        {
            platformIndex = index;
            return;
        }

        if (TryHandleVersion(content)) return;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ApplyFallbacks()
    {
        if (!platformSet && deviceSet)
        {
            platform = device;
            platformSet = true;
            platformIndex = deviceIndex;
        }

        if (architectureSet && architecturePlacement is ArchitectureTokenPlacement.Auto) architecturePlacement = ArchitectureTokenPlacement.Separate;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryHandleSecurity(ReadOnlySpan<char> token)
    {
        if (securitySet || !IsSecurityToken(token)) return default;

        security = token.ToString();
        securitySet = true;

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryHandleLocale(ReadOnlySpan<char> token)
    {
        if (localeSet || !IsLocaleToken(token)) return default;

        locale = token.ToString();
        localeSet = true;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryHandlePlatform(scoped ReadOnlySpan<char> token)
    {
        if (platformSet || !LooksLikePlatform(token)) return default;

        SetPlatform(token);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryHandleFallbackPlatform(scoped ReadOnlySpan<char> token)
    {
        if (platformSet) return default;

        SetPlatform(token);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryHandleDevice(scoped ReadOnlySpan<char> token)
    {
        if (deviceSet) return default;

        device = token.ToString();
        deviceSet = true;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryHandleVersion(scoped ReadOnlySpan<char> token)
    {
        if (versionSet) return default;
        if (!TryGetVersion(token, out var parsed)) return default;

        version = parsed;
        versionSet = true;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetPlatform(scoped ReadOnlySpan<char> token)
    {
        var trimmed = Trim(token);
        if (trimmed.IsEmpty) return;

        if (!versionSet && TryTakeVersionFromEnd(trimmed, out var platformSpan, out var versionSpan))
        {
            platform = platformSpan.ToString();
            version = versionSpan.ToString();
            versionSet = true;
        }
        else
        {
            platform = trimmed.ToString();
        }

        platformSet = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryReadNext(out ReadOnlySpan<char> token)
    {
        if (offset >= source.Length)
        {
            token = default;
            return default;
        }

        var start = offset;
        var remaining = source[start..];
        var separatorIndex = remaining.IndexOf(';');
        int end;

        if (separatorIndex < 0)
        {
            end = source.Length;
            offset = source.Length;
        }
        else
        {
            end = start + separatorIndex;
            offset = end + 1;
        }

        token = source[start..end];
        return true;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryTakeVersionFromEnd(ReadOnlySpan<char> token, out ReadOnlySpan<char> platform, out ReadOnlySpan<char> version)
    {
        var lastSpace = token.LastIndexOf(' ');

        if (lastSpace > 0)
        {
            var candidate = Trim(token[(lastSpace + 1)..]);

            if (!candidate.IsEmpty && candidate.IndexOf(':') < 0 && ContainsDigit(candidate))
            {
                platform = Trim(token[..lastSpace]);
                version = candidate;
                return !platform.IsEmpty;
            }
        }

        platform = token;
        version = ReadOnlySpan<char>.Empty;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryGetVersion(ReadOnlySpan<char> token, out string version)
    {
        var trimmed = Trim(token);

        if (trimmed.IsEmpty || trimmed.IndexOf(':') >= 0 || !ContainsDigit(trimmed))
        {
            version = string.Empty;
            return false;
        }

        version = trimmed.ToString();
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlySpan<char> StripArchitecture(ReadOnlySpan<char> token, ref string architecture, ref bool architectureSet, ref ArchitectureTokenPlacement placement)
    {
        var trimmed = Trim(token);
        if (trimmed.IsEmpty) return trimmed;

        var prefix = GetArchitecturePrefix(trimmed);

        if (!prefix.IsEmpty)
        {
            if (!architectureSet)
            {
                architecture = prefix.ToString();
                architectureSet = true;
                placement = ArchitectureTokenPlacement.Prefix;
            }

            return Trim(trimmed[prefix.Length..]);
        }

        var lastSpace = trimmed.LastIndexOf(' ');

        if (lastSpace > 0)
        {
            var suffix = Trim(trimmed[(lastSpace + 1)..]);

            if (!suffix.IsEmpty && TrySetArchitectureFromToken(suffix, ref architecture, ref architectureSet))
            {
                if (placement is ArchitectureTokenPlacement.Auto) placement = ArchitectureTokenPlacement.Suffix;
                return Trim(trimmed[..lastSpace]);
            }
        }

        if (TrySetArchitectureFromToken(trimmed, ref architecture, ref architectureSet))
        {
            if (placement is ArchitectureTokenPlacement.Auto) placement = ArchitectureTokenPlacement.Separate;
            return ReadOnlySpan<char>.Empty;
        }

        return trimmed;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlySpan<char> GetArchitecturePrefix(ReadOnlySpan<char> token)
    {
        if (token.StartsWith("Intel".AsSpan(), StringComparison.OrdinalIgnoreCase)) return token[..5];
        if (token.StartsWith("PPC".AsSpan(), StringComparison.OrdinalIgnoreCase)) return token[..3];
        if (token.StartsWith("ARM".AsSpan(), StringComparison.OrdinalIgnoreCase)) return token[..3];
        return ReadOnlySpan<char>.Empty;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TrySetArchitectureFromToken(ReadOnlySpan<char> token, ref string architecture, ref bool architectureSet)
    {
        if (token.IsEmpty) return default;

        if (IsArchitectureToken(token))
        {
            if (!architectureSet)
            {
                architecture = token.ToString();
                architectureSet = true;
            }

            return true;
        }

        return default;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsArchitectureToken(ReadOnlySpan<char> token)
    {
        if (token.Equals("x64".AsSpan(), StringComparison.OrdinalIgnoreCase)
            || token.Equals("x86".AsSpan(), StringComparison.OrdinalIgnoreCase)
            || token.Equals("amd64".AsSpan(), StringComparison.OrdinalIgnoreCase)
            || token.Equals("arm64".AsSpan(), StringComparison.OrdinalIgnoreCase)
            || token.Equals("armv7l".AsSpan(), StringComparison.OrdinalIgnoreCase)
            || token.Equals("armv8l".AsSpan(), StringComparison.OrdinalIgnoreCase)
            || token.Equals("ia64".AsSpan(), StringComparison.OrdinalIgnoreCase))
            return true;

        if (token.IndexOf("x86_64".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (token.IndexOf("arm64".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0) return true;

        return default;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool LooksLikePlatform(ReadOnlySpan<char> token)
    {
        if (token.IsEmpty) return default;
        if (ContainsDigit(token)) return true;

        return ContainsIgnoreCase(token, "Windows".AsSpan())
            || ContainsIgnoreCase(token, "Linux".AsSpan())
            || ContainsIgnoreCase(token, "Mac OS".AsSpan())
            || ContainsIgnoreCase(token, "Android".AsSpan())
            || ContainsIgnoreCase(token, "iPhone OS".AsSpan())
            || ContainsIgnoreCase(token, "iPad OS".AsSpan());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsLocaleToken(ReadOnlySpan<char> token)
    {
        if (token.Length is not 5 || token[2] is not '-') return default;

        return char.IsLetter(token[0])
            && char.IsLetter(token[1])
            && char.IsLetter(token[3])
            && char.IsLetter(token[4]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsSecurityToken(ReadOnlySpan<char> token)
        => token.Equals("U".AsSpan(), StringComparison.OrdinalIgnoreCase)
        || token.Equals("I".AsSpan(), StringComparison.OrdinalIgnoreCase)
        || token.Equals("N".AsSpan(), StringComparison.OrdinalIgnoreCase)
        || token.Equals("Win64".AsSpan(), StringComparison.OrdinalIgnoreCase)
        || token.Equals("WOW64".AsSpan(), StringComparison.OrdinalIgnoreCase)
        || token.Equals("WOW32".AsSpan(), StringComparison.OrdinalIgnoreCase);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ContainsDigit(ReadOnlySpan<char> value)
    {
        foreach (var c in value)
        {
            if (char.IsDigit(c)) return true;
        }

        return default;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ContainsIgnoreCase(ReadOnlySpan<char> value, ReadOnlySpan<char> match)
        => value.IndexOf(match, StringComparison.OrdinalIgnoreCase) >= 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlySpan<char> Trim(ReadOnlySpan<char> value)
    {
        var start = 0;
        var end = value.Length - 1;

        while (start <= end && char.IsWhiteSpace(value[start])) start++;
        while (end >= start && char.IsWhiteSpace(value[end])) end--;

        return start <= end ? value[start..(end + 1)] : ReadOnlySpan<char>.Empty;
    }
}