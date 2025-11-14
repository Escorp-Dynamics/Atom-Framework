namespace Atom.Algorithms.Text;

/// <summary>
/// Представляет базовый интерфейс для алгоритмов работы с текстом.
/// </summary>
public interface ITextAlgorithm
{
    /// <summary>
    /// Общий экземпляр алгоритма.
    /// </summary>
    static abstract ITextAlgorithm Shared { get; set; }

    /// <summary>
    /// Возвращает число найденных вхождений подстроки.
    /// </summary>
    /// <param name="source">Исходная строка.</param>
    /// <param name="target">Подстрока поиска.</param>
    /// <param name="comparison">Поведение сравнения строк.</param>
    /// <returns>Число найденных вхождений подстроки.</returns>
    int CountOf(ReadOnlySpan<char> source, ReadOnlySpan<char> target, StringComparison comparison);

    /// <summary>
    /// Возвращает число найденных вхождений подстроки.
    /// </summary>
    /// <param name="source">Исходная строка.</param>
    /// <param name="target">Подстрока поиска.</param>
    /// <returns>Число найденных вхождений подстроки.</returns>
    int CountOf(ReadOnlySpan<char> source, ReadOnlySpan<char> target) => CountOf(source, target, default);

    /// <summary>
    /// Возвращает число найденных вхождений подстроки.
    /// </summary>
    /// <param name="source">Исходная строка.</param>
    /// <param name="target">Подстрока поиска.</param>
    /// <param name="comparison">Поведение сравнения строк.</param>
    /// <returns>Число найденных вхождений подстроки.</returns>
    int CountOf(string source, ReadOnlySpan<char> target, StringComparison comparison) => CountOf(source.AsSpan(), target, comparison);

    /// <summary>
    /// Возвращает число найденных вхождений подстроки.
    /// </summary>
    /// <param name="source">Исходная строка.</param>
    /// <param name="target">Подстрока поиска.</param>
    /// <returns>Число найденных вхождений подстроки.</returns>
    int CountOf(string source, ReadOnlySpan<char> target) => CountOf(source, target, default);

    /// <summary>
    /// Возвращает число найденных вхождений подстроки.
    /// </summary>
    /// <param name="source">Исходная строка.</param>
    /// <param name="target">Подстрока поиска.</param>
    /// <param name="comparison">Поведение сравнения строк.</param>
    /// <returns>Число найденных вхождений подстроки.</returns>
    int CountOf(ReadOnlySpan<char> source, string target, StringComparison comparison) => CountOf(source, target.AsSpan(), comparison);

    /// <summary>
    /// Возвращает число найденных вхождений подстроки.
    /// </summary>
    /// <param name="source">Исходная строка.</param>
    /// <param name="target">Подстрока поиска.</param>
    /// <returns>Число найденных вхождений подстроки.</returns>
    int CountOf(ReadOnlySpan<char> source, string target) => CountOf(source, target, default);

    /// <summary>
    /// Возвращает число найденных вхождений подстроки.
    /// </summary>
    /// <param name="source">Исходная строка.</param>
    /// <param name="target">Подстрока поиска.</param>
    /// <param name="comparison">Поведение сравнения строк.</param>
    /// <returns>Число найденных вхождений подстроки.</returns>
    int CountOf(string source, string target, StringComparison comparison) => CountOf(source.AsSpan(), target.AsSpan(), comparison);

    /// <summary>
    /// Возвращает число найденных вхождений подстроки.
    /// </summary>
    /// <param name="source">Исходная строка.</param>
    /// <param name="target">Подстрока поиска.</param>
    /// <returns>Число найденных вхождений подстроки.</returns>
    int CountOf(string source, string target) => CountOf(source, target, default);

    /// <summary>
    /// Определяет, содержит ли исходная строка подстроку.
    /// </summary>
    /// <param name="source">Исходная строка.</param>
    /// <param name="target">Подстрока поиска.</param>
    /// <param name="comparison">Поведение сравнения строк.</param>
    /// <returns><see langword="true"/>, если исходная строка содержит подстроку; в противном случае — <see langword="false"/>.</returns>
    bool Contains(ReadOnlySpan<char> source, ReadOnlySpan<char> target, StringComparison comparison);

    /// <summary>
    /// Определяет, содержит ли исходная строка подстроку.
    /// </summary>
    /// <param name="source">Исходная строка.</param>
    /// <param name="target">Подстрока поиска.</param>
    /// <returns><see langword="true"/>, если исходная строка содержит подстроку; в противном случае — <see langword="false"/>.</returns>
    bool Contains(ReadOnlySpan<char> source, ReadOnlySpan<char> target) => Contains(source, target, default);

    /// <summary>
    /// Определяет, содержит ли исходная строка подстроку.
    /// </summary>
    /// <param name="source">Исходная строка.</param>
    /// <param name="target">Подстрока поиска.</param>
    /// <param name="comparison">Поведение сравнения строк.</param>
    /// <returns><see langword="true"/>, если исходная строка содержит подстроку; в противном случае — <see langword="false"/>.</returns>
    bool Contains(string source, ReadOnlySpan<char> target, StringComparison comparison) => Contains(source.AsSpan(), target, comparison);

    /// <summary>
    /// Определяет, содержит ли исходная строка подстроку.
    /// </summary>
    /// <param name="source">Исходная строка.</param>
    /// <param name="target">Подстрока поиска.</param>
    /// <returns><see langword="true"/>, если исходная строка содержит подстроку; в противном случае — <see langword="false"/>.</returns>
    bool Contains(string source, ReadOnlySpan<char> target) => Contains(source, target, default);

    /// <summary>
    /// Определяет, содержит ли исходная строка подстроку.
    /// </summary>
    /// <param name="source">Исходная строка.</param>
    /// <param name="target">Подстрока поиска.</param>
    /// <param name="comparison">Поведение сравнения строк.</param>
    /// <returns><see langword="true"/>, если исходная строка содержит подстроку; в противном случае — <see langword="false"/>.</returns>
    bool Contains(ReadOnlySpan<char> source, string target, StringComparison comparison) => Contains(source, target.AsSpan(), comparison);

    /// <summary>
    /// Определяет, содержит ли исходная строка подстроку.
    /// </summary>
    /// <param name="source">Исходная строка.</param>
    /// <param name="target">Подстрока поиска.</param>
    /// <returns><see langword="true"/>, если исходная строка содержит подстроку; в противном случае — <see langword="false"/>.</returns>
    bool Contains(ReadOnlySpan<char> source, string target) => Contains(source, target, default);

    /// <summary>
    /// Определяет, содержит ли исходная строка подстроку.
    /// </summary>
    /// <param name="source">Исходная строка.</param>
    /// <param name="target">Подстрока поиска.</param>
    /// <param name="comparison">Поведение сравнения строк.</param>
    /// <returns><see langword="true"/>, если исходная строка содержит подстроку; в противном случае — <see langword="false"/>.</returns>
    bool Contains(string source, string target, StringComparison comparison) => Contains(source.AsSpan(), target.AsSpan(), comparison);

    /// <summary>
    /// Определяет, содержит ли исходная строка подстроку.
    /// </summary>
    /// <param name="source">Исходная строка.</param>
    /// <param name="target">Подстрока поиска.</param>
    /// <returns><see langword="true"/>, если исходная строка содержит подстроку; в противном случае — <see langword="false"/>.</returns>
    bool Contains(string source, string target) => Contains(source, target, default);
}