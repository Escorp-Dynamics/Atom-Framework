# Algorithms

Библиотека алгоритмов обработки текста, оптимизированная для сценариев с высокой
нагрузкой и работы со span-структурами. Внутри модуля «Text» собраны классические
стратегии поиска подстрок, доступные через единый интерфейс `ITextAlgorithm`.

## Когда использовать

- Поиск простых или множественных шаблонов в больших текстовых потоках.
- Анализ логов и сетевых пакетов без выделений памяти (`ReadOnlySpan<char>`).
- Подготовка кода генераторов, которые должны выбирать алгоритм в рантайме.

## Структура

| Класс | Назначение | Сценарий |
|-------|------------|----------|
| `TextAlgorithm` | Базовый класс с перегрузками `Contains`/`CountOf` для `string` и `Span` | Используйте как стартовую точку для собственного алгоритма |
| `KmpAlgorithm` | Префикс-функция Кнута-Морриса-Пратта | Линейный поиск с несколькими совпадениями |
| `RabinKarpAlgorithm` | Rolling hash | Быстрый грубый поиск с проверкой хэшей |
| `BoyerMooreAlgorithm` | Вычисление сдвигов по плохим символам | Длинные шаблоны на большом тексте |
| `ZAlgorithm` | Z-функция | Поиск всех вхождений и построение суффиксных массивов |
| `AhoCorasickAlgorithm` | Автомат для нескольких шаблонов | Поиск множества токенов за один проход |

Все реализации доступны напрямую, а также могут быть зарегистрированы в
`TextAlgorithm.Shared` — глобальной точке доступа к выбранной стратегии.

## Быстрый старт

```csharp
using Atom.Algorithms.Text;

// 1. Выбираем реализацию и публикуем её глобально
TextAlgorithm.Shared = new BoyerMooreAlgorithm();

// 2. Работаем через общий интерфейс: span-версии не создают временные строки
ReadOnlySpan<char> text = "GET /api/user HTTP/1.1";
ReadOnlySpan<char> pattern = "api/user";

bool contains = TextAlgorithm.Shared.Contains(text, pattern, StringComparison.Ordinal);
Console.WriteLine($"Подстрока найдена: {contains}");
```

## Подробные примеры

### 1. Поиск совпадений с подсчётом

```csharp
var algorithm = new KmpAlgorithm();
var source = "abaAbaba";
var target = "aba";

// Регистронезависимый поиск — сравнение передаём явно
int hits = algorithm.CountOf(source, target, StringComparison.OrdinalIgnoreCase);
Console.WriteLine($"Количество совпадений: {hits}");
```

### 2. Множественные шаблоны (Aho–Corasick)

```csharp
var keywords = new[] { "error", "fail", "timeout" };
var matcher = new AhoCorasickAlgorithm(keywords);

var log = "[WARN] request timeout; retry failed";
if (matcher.Contains(log, "timeout"))
{
    Console.WriteLine("Найдена проблемная запись");
}

int total = matcher.CountOf(log, ReadOnlySpan<char>.Empty); // количество найденных ключей
```

### 3. Работа со `ReadOnlySpan<char>` без выделений

```csharp
ReadOnlySpan<char> payload = stackalloc char[] { 'a', 'b', 'c', 'a', 'b', 'a' };
ReadOnlySpan<char> needle = "aba";

var rk = new RabinKarpAlgorithm();
bool match = rk.Contains(payload, needle, StringComparison.Ordinal);
```

## Производительность и рекомендации

- При последовательных вызовах используйте один экземпляр алгоритма — он
  переиспользует внутренние буферы (`SpanPool`).
- Для коротких шаблонов (`< 8 символов`) `KmpAlgorithm` даёт стабильный линейный
  результат. `BoyerMooreAlgorithm` раскрывает потенциал на длинных шаблонах.
- `AhoCorasickAlgorithm` строит автомат при создании — выгодно, когда нужно искать
  набор ключей много раз.

## Расширение модуля

Создать собственный алгоритм можно, унаследовавшись от `TextAlgorithm` и
реализовав два метода:

```csharp
public sealed class RegexAlgorithm : TextAlgorithm
{
    public override int CountOf(ReadOnlySpan<char> source, ReadOnlySpan<char> target, StringComparison comparison)
    {
        var regex = new Regex(target.ToString(), RegexOptions.Compiled);
        return regex.Matches(source.ToString()).Count;
    }

    public override bool Contains(ReadOnlySpan<char> source, ReadOnlySpan<char> target, StringComparison comparison)
        => CountOf(source, target, comparison) > 0;
}
```

