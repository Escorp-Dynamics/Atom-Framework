using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Atom.Buffers;

namespace Atom.Text;

/// <summary>
/// Класс для генерации уникальных комбинаций заданной длины из заданного набора символов.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="UniqueCombinationGenerator"/>.
/// </remarks>
/// <param name="length">Длина комбинаций, которые будут генерироваться.</param>
/// <param name="characters">Набор символов, из которых будут генерироваться комбинации.</param>
public class UniqueCombinationGenerator(int length, string characters)
{
    private readonly ConcurrentQueue<string> generatedCombinations = [];

    /// <summary>
    /// Свойство, представляющее длину комбинаций, которые будут генерироваться.
    /// </summary>
    /// <value>
    /// Длина комбинаций.
    /// </value>
    public int Length { get; protected set; } = length;

    /// <summary>
    /// Свойство, представляющее набор символов, из которых будут генерироваться комбинации.
    /// </summary>
    /// <value>
    /// Набор символов.
    /// </value>
    public string Characters { get; protected set; } = characters;

    /// <summary>
    /// Свойство, представляющее максимальное количество уникальных комбинаций, которое может быть сгенерировано.
    /// </summary>
    /// <value>
    /// Максимальное количество уникальных комбинаций.
    /// </value>
    public int Limit => (int)Math.Pow(Characters.Length, Length);

    /// <summary>
    /// Свойство, представляющее текущее количество сгенерированных уникальных комбинаций в очереди.
    /// </summary>
    /// <value>
    /// Количество сгенерированных уникальных комбинаций.
    /// </value>
    public int Size => generatedCombinations.Count;

    /// <summary>
    /// Свойство, представляющее коллекцию сгенерированных уникальных комбинаций.
    /// </summary>
    /// <value>
    /// Коллекция сгенерированных уникальных комбинаций.
    /// </value>
    public IEnumerable<string> GeneratedCombinations => generatedCombinations;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="UniqueCombinationGenerator"/>.
    /// </summary>
    /// <param name="length">Длина комбинаций, которые будут генерироваться.</param>
    public UniqueCombinationGenerator(int length) : this(length, "abcdefghijklmnopqrstuvwxyz") { }

    /// <summary>
    /// Метод для генерации следующей уникальной комбинации.
    /// </summary>
    /// <returns>
    /// Следующая уникальная комбинация или null, если достигнут лимит комбинаций.
    /// </returns>
    public string? Next()
    {
        if (generatedCombinations.Count >= Limit) return default;
        var randomBytes = SpanPool<byte>.Shared.Rent(Length);
        var sb = ObjectPool<StringBuilder>.Shared.Rent();
        string? combination;

        do
        {
            RandomNumberGenerator.Fill(randomBytes);
            sb.Clear();

            for (var i = 0; i < Length; ++i)
            {
                var index = randomBytes[i] % Characters.Length;
                sb.Append(Characters[index]);
            }

            combination = sb.ToString();
        }
        while (generatedCombinations.Contains(combination));

        generatedCombinations.Enqueue(combination);
        ObjectPool<StringBuilder>.Shared.Return(sb);

        return combination;
    }

    /// <summary>
    /// Асинхронно заполняет очередь сгенерированными уникальными комбинациями.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены для отмены операции.</param>
    /// <returns>Задача, представляющая асинхронную операцию.</returns>
    public async ValueTask FillAsync(CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool>();

        _ = Task.Run(() =>
        {
            var combination = string.Empty;

            do
            {
                combination = Next();
            }
            while (!string.IsNullOrEmpty(combination));

            tcs.TrySetResult(true);
        }, cancellationToken);

        await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// Асинхронно заполняет очередь сгенерированными уникальными комбинациями.
    /// </summary>
    /// <returns>Задача, представляющая асинхронную операцию.</returns>
    public ValueTask FillAsync() => FillAsync(CancellationToken.None);

    /// <summary>
    /// Метод для получения следующей сгенерированной уникальной комбинации из очереди.
    /// </summary>
    /// <returns>
    /// Следующая сгенерированная уникальная комбинация или null, если очередь пуста.
    /// </returns>
    public string? FromGenerated() => generatedCombinations.TryDequeue(out var combination) ? combination : default;
}