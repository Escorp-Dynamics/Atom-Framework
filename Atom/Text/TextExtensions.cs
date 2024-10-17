using System.Diagnostics.CodeAnalysis;
using Atom.Algorithms.Text;

namespace Atom.Text;

/// <summary>
/// Представляет методы расширений для работы с текстом.
/// </summary>
public static class TextExtensions
{
    /// <summary>
    /// Возвращает число найденных вхождений подстроки.
    /// </summary>
    /// <param name="source">Исходная строка.</param>
    /// <param name="target">Подстрока поиска.</param>
    /// <param name="comparison">Поведение сравнения строк.</param>
    /// <param name="algorithm">Экземпляр алгоритма.</param>
    /// <returns>Число найденных вхождений подстроки.</returns>
    public static int CountOf(this string source, string target, StringComparison comparison, [NotNull] ITextAlgorithm algorithm)
        => algorithm.CountOf(source, target, comparison);

    /// <summary>
    /// Возвращает число найденных вхождений подстроки.
    /// </summary>
    /// <param name="source">Исходная строка.</param>
    /// <param name="target">Подстрока поиска.</param>
    /// <param name="algorithm">Экземпляр алгоритма.</param>
    /// <returns>Число найденных вхождений подстроки.</returns>
    public static int CountOf(this string source, string target, ITextAlgorithm algorithm)
        => source.CountOf(target, default, algorithm);

    /// <summary>
    /// Возвращает число найденных вхождений подстроки.
    /// </summary>
    /// <param name="source">Исходная строка.</param>
    /// <param name="target">Подстрока поиска.</param>
    /// <param name="comparison">Поведение сравнения строк.</param>
    /// <typeparam name="TAlgorithm">Тип алгоритма поиска.</typeparam>
    /// <returns>Число найденных вхождений подстроки.</returns>
    public static int CountOf<TAlgorithm>(this string source, string target, StringComparison comparison)
        where TAlgorithm : ITextAlgorithm, new()
    {
        TAlgorithm.Shared ??= new TAlgorithm();
        return source.CountOf(target, comparison, TAlgorithm.Shared);
    }

    /// <summary>
    /// Возвращает число найденных вхождений подстроки.
    /// </summary>
    /// <param name="source">Исходная строка.</param>
    /// <param name="target">Подстрока поиска.</param>
    /// <typeparam name="TAlgorithm">Тип алгоритма поиска.</typeparam>
    /// <returns>Число найденных вхождений подстроки.</returns>
    public static int CountOf<TAlgorithm>(this string source, string target)
        where TAlgorithm : ITextAlgorithm, new()
        => source.CountOf<TAlgorithm>(target, default);

    /// <summary>
    /// Возвращает число найденных вхождений подстроки.
    /// </summary>
    /// <param name="source">Исходная строка.</param>
    /// <param name="target">Подстрока поиска.</param>
    /// <param name="comparison">Поведение сравнения строк.</param>
    /// <param name="algorithm">Алгоритм поиска.</param>
    /// <returns>Число найденных вхождений подстроки.</returns>
    public static int CountOf(this string source, string target, StringComparison comparison, SubstringSearchAlgorithm algorithm) => algorithm switch
    {
        SubstringSearchAlgorithm.RabinKarp => source.CountOf<RabinKarpAlgorithm>(target, comparison),
        SubstringSearchAlgorithm.KMP => source.CountOf<KmpAlgorithm>(target, comparison),
        SubstringSearchAlgorithm.BoyerMoore => source.CountOf<BoyerMooreAlgorithm>(target, comparison),
        SubstringSearchAlgorithm.AhoCorasick => source.CountOf<AhoCorasickAlgorithm>(target, comparison),
        SubstringSearchAlgorithm.Z => source.CountOf<ZAlgorithm>(target, comparison),
        _ => throw new NotSupportedException(),
    };

    /// <summary>
    /// Возвращает число найденных вхождений подстроки.
    /// </summary>
    /// <param name="source">Исходная строка.</param>
    /// <param name="target">Подстрока поиска.</param>
    /// <param name="comparison">Поведение сравнения строк.</param>
    /// <returns>Число найденных вхождений подстроки.</returns>
    public static int CountOf(this string source, string target, StringComparison comparison) => source.CountOf<RabinKarpAlgorithm>(target, comparison);

    /// <summary>
    /// Возвращает число найденных вхождений подстроки.
    /// </summary>
    /// <param name="source">Исходная строка.</param>
    /// <param name="target">Подстрока поиска.</param>
    /// <param name="algorithm">Алгоритм поиска.</param>
    /// <returns>Число найденных вхождений подстроки.</returns>
    public static int CountOf(this string source, string target, SubstringSearchAlgorithm algorithm) => source.CountOf(target, default, algorithm);

    /// <summary>
    /// Возвращает число найденных вхождений подстроки.
    /// </summary>
    /// <param name="source">Исходная строка.</param>
    /// <param name="target">Подстрока поиска.</param>
    /// <returns>Число найденных вхождений подстроки.</returns>
    public static int CountOf(this string source, string target) => source.CountOf(target, SubstringSearchAlgorithm.RabinKarp);

    /// <summary>
    /// Возвращает число найденных символов.
    /// </summary>
    /// <param name="source">Исходная строка.</param>
    /// <param name="target">Искомый символ.</param>
    /// <param name="comparison">Поведение сравнения строк.</param>
    /// <returns>Число найденных символов.</returns>
    public static unsafe int CountOf(this string source, char target, StringComparison comparison)
    {
        if (string.IsNullOrEmpty(source) || target is char.MinValue) return default;

        var count = 0;

        fixed (char* ptr = source)
            for (var p = ptr; *p is not char.MinValue; ++p)
                if ((*p).Equals(target, comparison))
                    ++count;

        return count;
    }

    /// <summary>
    /// Возвращает число найденных символов.
    /// </summary>
    /// <param name="source">Исходная строка.</param>
    /// <param name="target">Искомый символ.</param>
    /// <returns>Число найденных символов.</returns>
    public static int CountOf(this string source, char target) => source.CountOf(target, default);

    /// <summary>
    /// Проверяет, содержит ли строка source подстроку target, используя алгоритм поиска, определенный в algorithm.
    /// </summary>
    /// <param name="source">Исходная строка.</param>
    /// <param name="target">Подстрока поиска.</param>
    /// <param name="comparison">Поведение сравнения строк.</param>
    /// <param name="algorithm">Алгоритм поиска.</param>
    /// <returns><c>true</c>, если строка source содержит подстроку target; иначе, <c>false</c>.</returns>
    public static bool Contains(this string source, string target, StringComparison comparison, [NotNull] ITextAlgorithm algorithm)
        => algorithm.Contains(source, target, comparison);

    /// <summary>
    /// Проверяет, содержит ли строка source подстроку target, используя алгоритм поиска, определенный в algorithm.
    /// </summary>
    /// <param name="source">Исходная строка.</param>
    /// <param name="target">Подстрока поиска.</param>
    /// <param name="algorithm">Алгоритм поиска.</param>
    /// <returns><c>true</c>, если строка source содержит подстроку target; иначе, <c>false</c>.</returns>
    public static bool Contains(this string source, string target, ITextAlgorithm algorithm)
        => source.Contains(target, default, algorithm);

    /// <summary>
    /// Проверяет, содержит ли строка source подстроку target, используя алгоритм поиска, определенный в algorithm.
    /// </summary>
    /// <param name="source">Исходная строка.</param>
    /// <param name="target">Подстрока поиска.</param>
    /// <param name="comparison">Поведение сравнения строк.</param>
    /// <returns><c>true</c>, если строка source содержит подстроку target; иначе, <c>false</c>.</returns>
    public static bool Contains<TAlgorithm>(this string source, string target, StringComparison comparison)
        where TAlgorithm : ITextAlgorithm, new()
    {
        TAlgorithm.Shared ??= new TAlgorithm();
        return source.Contains(target, comparison, TAlgorithm.Shared);
    }

    /// <summary>
    /// Проверяет, содержит ли строка source подстроку target, используя алгоритм поиска, определенный в algorithm.
    /// </summary>
    /// <param name="source">Исходная строка.</param>
    /// <param name="target">Подстрока поиска.</param>
    /// <returns><c>true</c>, если строка source содержит подстроку target; иначе, <c>false</c>.</returns>
    public static bool Contains<TAlgorithm>(this string source, string target)
        where TAlgorithm : ITextAlgorithm, new()
        => source.Contains<TAlgorithm>(target, default);

    /// <summary>
    /// Проверяет, содержит ли строка source подстроку target, используя алгоритм поиска, определенный в algorithm.
    /// </summary>
    /// <param name="source">Исходная строка.</param>
    /// <param name="target">Подстрока поиска.</param>
    /// <param name="comparison">Поведение сравнения строк.</param>
    /// <param name="algorithm">Алгоритм поиска.</param>
    /// <returns><c>true</c>, если строка source содержит подстроку target; иначе, <c>false</c>.</returns>
    public static bool Contains(this string source, string target, StringComparison comparison, SubstringSearchAlgorithm algorithm) => algorithm switch
    {
        SubstringSearchAlgorithm.RabinKarp => source.Contains<RabinKarpAlgorithm>(target, comparison),
        SubstringSearchAlgorithm.KMP => source.Contains<KmpAlgorithm>(target, comparison),
        SubstringSearchAlgorithm.BoyerMoore => source.Contains<BoyerMooreAlgorithm>(target, comparison),
        SubstringSearchAlgorithm.AhoCorasick => source.Contains<AhoCorasickAlgorithm>(target, comparison),
        SubstringSearchAlgorithm.Z => source.Contains<ZAlgorithm>(target, comparison),
        _ => throw new NotSupportedException(),
    };

    /// <summary>
    /// Проверяет, содержит ли строка source подстроку target, используя алгоритм поиска, определенный в algorithm.
    /// </summary>
    /// <param name="source">Исходная строка.</param>
    /// <param name="target">Подстрока поиска.</param>
    /// <param name="comparison">Поведение сравнения строк.</param>
    /// <returns><c>true</c>, если строка source содержит подстроку target; иначе, <c>false</c>.</returns>
    public static bool Contains(this string source, string target, StringComparison comparison) => source.Contains<KmpAlgorithm>(target, comparison);

    /// <summary>
    /// Проверяет, содержит ли строка source подстроку target, используя алгоритм поиска, определенный в algorithm.
    /// </summary>
    /// <param name="source">Исходная строка.</param>
    /// <param name="target">Подстрока поиска.</param>
    /// <param name="algorithm">Алгоритм поиска.</param>
    /// <returns><c>true</c>, если строка source содержит подстроку target; иначе, <c>false</c>.</returns>
    public static bool Contains(this string source, string target, SubstringSearchAlgorithm algorithm) => source.Contains(target, default, algorithm);

    /// <summary>
    /// Проверяет, содержит ли строка source подстроку target, используя алгоритм поиска, определенный в algorithm.
    /// </summary>
    /// <param name="source">Исходная строка.</param>
    /// <param name="target">Подстрока поиска.</param>
    /// <returns><c>true</c>, если строка source содержит подстроку target; иначе, <c>false</c>.</returns>
    public static bool Contains(this string source, string target) => source.Contains(target, SubstringSearchAlgorithm.KMP);
}