namespace Atom.Net.Browsing.WebDriver;

internal sealed class RequestInterceptionState
{
    internal RequestInterceptionState(bool enabled, IEnumerable<string>? urlPatterns)
    {
        Enabled = enabled;
        UrlPatterns = enabled && urlPatterns is not null
            ? [.. urlPatterns]
            : null;
    }

    internal bool Enabled { get; }

    internal string[]? UrlPatterns { get; }

    internal bool Matches(string? url) => Matches(url, method: null);

    /// <summary>
    /// Сопоставляет запрос с шаблонами перехвата.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="method">Метод запроса; <see langword="null"/>, когда он неизвестен.</param>
    /// <returns><see langword="true"/>, если запрос подпадает под перехват.</returns>
    /// <remarks>
    /// Шаблон может начинаться с метода: <c lang="text">"POST https://.../c/*"</c>. Так один
    /// адрес удаётся сузить до нужного обмена, не расширяя перехват на соседние запросы того же
    /// пути. Это не косметика: каждый лишний перехваченный запрос идёт раунд-трипом через мост и
    /// переотправляется нашим стеком, а замер показал, что для потока телеметрии Cloudflare такая
    /// задержка означает отказ решения — «Bot behavior detected» на КАЖДОЙ задаче.
    ///
    /// Шаблон без метода ведёт себя как прежде и совпадает с любым.
    /// </remarks>
    internal bool Matches(string? url, string? method)
    {
        if (!Enabled)
            return false;

        if (UrlPatterns is not { Length: > 0 })
            return true;

        if (string.IsNullOrWhiteSpace(url))
            return false;

        foreach (var pattern in UrlPatterns)
        {
            var (patternMethod, urlPattern) = SplitMethodPrefix(pattern);

            if (patternMethod is not null
                && !string.Equals(patternMethod, method, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (UrlPatternMatcher.IsMatch(urlPattern, url))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Отделяет необязательный префикс метода от шаблона адреса.
    /// </summary>
    /// <remarks>
    /// Схема адреса содержит <c lang="text">"://"</c>, а метод — только буквы, поэтому граница
    /// определяется однозначно: пробел до первого двоеточия.
    /// </remarks>
    private static (string? Method, string UrlPattern) SplitMethodPrefix(string pattern)
    {
        var space = pattern.IndexOf(' ', StringComparison.Ordinal);

        if (space <= 0)
            return (null, pattern);

        var head = pattern[..space];

        foreach (var character in head)
        {
            if (!char.IsAsciiLetter(character))
                return (null, pattern);
        }

        return (head, pattern[(space + 1)..]);
    }

    internal static RequestInterceptionState Create(bool enabled, IEnumerable<string>? urlPatterns)
        => new(enabled, urlPatterns);

    internal static bool AreEquivalent(RequestInterceptionState? left, RequestInterceptionState? right)
    {
        if (ReferenceEquals(left, right))
            return true;

        if (left is null || right is null || left.Enabled != right.Enabled)
            return false;

        if (left.UrlPatterns is null || right.UrlPatterns is null)
            return left.UrlPatterns is null && right.UrlPatterns is null;

        if (left.UrlPatterns.Length != right.UrlPatterns.Length)
            return false;

        for (var i = 0; i < left.UrlPatterns.Length; i++)
        {
            if (!string.Equals(left.UrlPatterns[i], right.UrlPatterns[i], StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static class UrlPatternMatcher
    {
        internal static bool IsMatch(string? pattern, string candidate)
        {
            if (string.IsNullOrEmpty(pattern))
                return false;

            var patternIndex = 0;
            var candidateIndex = 0;
            var starPatternIndex = -1;
            var starCandidateIndex = -1;

            while (candidateIndex < candidate.Length)
            {
                if (patternIndex < pattern.Length
                    && (pattern[patternIndex] == '*' || pattern[patternIndex] == candidate[candidateIndex]))
                {
                    if (pattern[patternIndex] == '*')
                    {
                        starPatternIndex = patternIndex++;
                        starCandidateIndex = candidateIndex;
                    }
                    else
                    {
                        patternIndex++;
                        candidateIndex++;
                    }

                    continue;
                }

                if (starPatternIndex >= 0)
                {
                    patternIndex = starPatternIndex + 1;
                    candidateIndex = ++starCandidateIndex;
                    continue;
                }

                return false;
            }

            while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                patternIndex++;
            }

            return patternIndex == pattern.Length;
        }
    }
}