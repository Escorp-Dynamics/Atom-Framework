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

    internal bool Matches(string? url)
    {
        if (!Enabled)
            return false;

        if (UrlPatterns is not { Length: > 0 })
            return true;

        if (string.IsNullOrWhiteSpace(url))
            return false;

        foreach (var pattern in UrlPatterns)
        {
            if (UrlPatternMatcher.IsMatch(pattern, url))
                return true;
        }

        return false;
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