# Json

Генератор типизированных JSON-контекстов для `System.Text.Json`. Автоматически
создаёт методы сериализации/десериализации с поддержкой `JsonTypeInfo<T>` и
обработкой ошибок.

## Атрибут JsonContextAttribute

Помечает тип для генерации JSON-контекста:

```csharp
[JsonContext]
public partial class UserDto
{
    public string Name { get; set; }
    public int Age { get; set; }
}

// С настройками:
[JsonContext(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
public partial class ConfigDto
{
    public string AppName { get; set; }
    public string Version { get; set; }
}
```

### Параметры атрибута

Атрибут принимает стандартные параметры `JsonSerializerOptions`:

| Параметр | Описание |
|----------|----------|
| `PropertyNamingPolicy` | Политика именования свойств |
| `WriteIndented` | Форматированный вывод |
| `DefaultIgnoreCondition` | Условие игнорирования свойств |
| `PropertyNameCaseInsensitive` | Регистронезависимый парсинг |
| и другие... | Все параметры `JsonSerializerOptions` |

## Генерируемый код

`JsonContextTypeSyntaxProvider` создаёт для каждого типа:

### Статические члены

- `TypeInfo` — ленивый `JsonTypeInfo<T>` с настроенными опциями
- `SerializationFailed` — событие при ошибке сериализации

### Методы сериализации

```csharp
// Синхронные
string? Serialize();
void Serialize(Stream utf8json);
void Serialize(Utf8JsonWriter writer);
JsonDocument? SerializeToDocument();
JsonElement? SerializeToElement();
JsonNode? SerializeToNode();
ReadOnlySpan<byte> SerializeToUtf8Bytes();

// Асинхронные
ValueTask SerializeAsync(Stream utf8json, CancellationToken ct = default);
ValueTask SerializeAsync(PipeWriter writer, CancellationToken ct = default);
```

### Статические методы десериализации

```csharp
// Синхронные
static T? Deserialize(JsonDocument json);
static T? Deserialize(JsonElement json);
static T? Deserialize(Stream utf8json);
static T? Deserialize(string json);
static T? Deserialize(JsonNode? node);
static T? Deserialize(ref Utf8JsonReader reader);
static T? Deserialize(ReadOnlySpan<byte> json);
static T? Deserialize(ReadOnlySpan<char> json);

// Асинхронные
static ValueTask<T?> DeserializeAsync(Stream utf8json, CancellationToken ct = default);
static IAsyncEnumerable<T?> DeserializeAsyncEnumerable(Stream utf8json, CancellationToken ct = default);
static IAsyncEnumerable<T?> DeserializeAsyncEnumerable(Stream utf8json, bool topLevelValues, CancellationToken ct = default);
```

## Пример использования

### Определение

```csharp
[JsonContext(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class Person
{
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public int Age { get; set; }
    public List<string> Tags { get; set; } = new();
}
```

### Сериализация

```csharp
var person = new Person
{
    FirstName = "John",
    LastName = "Doe",
    Age = 30,
    Tags = ["developer", "gamer"]
};

// В строку
string json = person.Serialize();
// {"firstName":"John","lastName":"Doe","age":30,"tags":["developer","gamer"]}

// В поток
await using var stream = new MemoryStream();
await person.SerializeAsync(stream);

// В UTF-8 байты
ReadOnlySpan<byte> bytes = person.SerializeToUtf8Bytes();
```

### Десериализация

```csharp
// Из строки
var person = Person.Deserialize(json);

// Из потока
await using var stream = File.OpenRead("person.json");
var person = await Person.DeserializeAsync(stream);

// Из JsonElement (например, при парсинге массива)
using var doc = JsonDocument.Parse(jsonArray);
foreach (var element in doc.RootElement.EnumerateArray())
{
    var person = Person.Deserialize(element);
}

// Потоковая десериализация (для больших массивов)
await foreach (var person in Person.DeserializeAsyncEnumerable(stream))
{
    ProcessPerson(person);
}
```

## Обработка ошибок

Все методы перехватывают `JsonException` и вызывают событие `SerializationFailed`:

```csharp
Person.SerializationFailed += (sender, args) =>
{
    Console.WriteLine($"Ошибка: {args.Exception.Message}");
};

// Некорректный JSON не выбросит исключение
var person = Person.Deserialize("invalid json"); // person == null
```

## Кастомные конвертеры

Поддерживается атрибут `[JsonConverter]`:

```csharp
[JsonContext]
[JsonConverter(typeof(PersonJsonConverter))]
public partial class Person
{
    public string Name { get; set; }
}

public class PersonJsonConverter : JsonConverter<Person>
{
    // Кастомная логика сериализации
}
```

## Наследование

Для производных типов генерируется отдельный контекст:

```csharp
[JsonContext]
public partial class BaseDto
{
    public int Id { get; set; }
}

[JsonContext]
public partial class DerivedDto : BaseDto
{
    public string Name { get; set; }
}
```

## Интерфейс IJsonContext

Все сгенерированные типы реализуют `IJsonContext<T>`:

```csharp
public interface IJsonContext<T> where T : IJsonContext<T>
{
    static abstract JsonTypeInfo<T> TypeInfo { get; }
    static abstract event MutableEventHandler<object, FailedEventArgs>? SerializationFailed;

    string? Serialize();
    static abstract T? Deserialize(string json);
    // ... и другие методы
}
```

Это позволяет создавать обобщённые утилиты:

```csharp
public static async Task SaveAsync<T>(T obj, string path) where T : IJsonContext<T>
{
    await using var stream = File.Create(path);
    await obj.SerializeAsync(stream);
}

var person = new Person { Name = "John" };
await SaveAsync(person, "person.json");
```

## AOT-совместимость

Генератор создаёт код, совместимый с Native AOT:

- Не используется рефлексия
- `JsonTypeInfo<T>` создаётся через source-generated контексты
- Поддерживается trimming

## Диагностика

| ID | Severity | Описание |
|----|----------|----------|
| `A0001` | Hidden | Обнаружен атрибут `[JsonContext]` |

## Файлы

- `JsonContextTypeSyntaxProvider.cs` — основной провайдер генерации
- `JsonContextSourceGenerator.cs` — точка входа генератора
- `JsonContextSourceAnalyzer.cs` — анализатор для IDE
- `JsonContextAttributeAnalyzerSyntaxProvider.cs` — провайдер анализа атрибутов
