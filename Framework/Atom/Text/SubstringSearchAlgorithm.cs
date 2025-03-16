namespace Atom.Text;

/// <summary>
/// Реализация алгоритма поиска подстроки.
/// </summary>
public enum SubstringSearchAlgorithm
{
    /// <summary>
    /// Алгоритм Бойера-Мура (Boyer-Moore).
    /// </summary>
    BoyerMoore,
    /// <summary>
    /// Алгоритм Рабина-Карпа (Rabin-Karp) 
    /// </summary>
    RabinKarp,
    /// <summary>
    /// Алгоритм Ахо-Корасик (Aho-Corasick).
    /// </summary>
    AhoCorasick,
    /// <summary>
    /// Алгоритм Кнута-Морриса-Пратта (KMP).
    /// </summary>
    KMP,
    /// <summary>
    /// Z-алгоритм.
    /// </summary>
    Z,
}