# Text

Инструменты для работы с текстом и JSON: расширения строк, форматирование в
Unix-стиле, а также инфраструктура `JsonContext` с поддержкой генерации кода.

## 1. JSON-контексты

`JsonContext` и связанные абстракции позволяют использовать `System.Text.Json`
вместе с сорс-генератором `JsonContextTypeSyntaxProvider`. Это даёт:

- готовый `JsonTypeInfo<T>` с ленивой инициализацией;
- набор перегрузок `Serialize`/`Deserialize` (строки, потоки, `JsonDocument`,
  `JsonNode`, `Utf8JsonReader`, `PipeWriter` и асинхронные версии);
- события `SerializationFailed` и `OptionsChanged`.

### Пример

```csharp
using Atom.Text.Json;
using System.Text.Json.Serialization;

[JsonContext(typeof(User))]
public partial class UserJsonContext : JsonSerializerContext
{
}

var json = UserJsonContext.Shared.Serialize(new User { Name = "Alice" });
var model = UserJsonContext.Shared.Deserialize(json);
```

### Обработка ошибок сериализации

```csharp
UserJsonContext.Shared.SerializationFailed += (sender, args) =>
{
    Console.WriteLine($"Ошибка JSON: {args.Exception.Message}");
};
```

Генератор создаёт класс с ленивым полем `Lazy<JsonTypeInfo<User>>` и
статическими методами `Serialize*`/`Deserialize*`. При возникновении
`JsonException` событие `SerializationFailed` публикуется автоматом.

## 2. Расширения текста

`TextExtensions` содержит десятки вспомогательных методов:

- `GetUpperChar`, `GetLowerChar` — быстрый доступ к таблицам регистров.
- `NormalizeWhitespace`, `CollapseSpaces`, `SplitLines` — подготовка текста к
  отображению и хранению.
- `ToCamelCase`, `ToPascalCase`, `ToSnakeCase` — конвертация форматов имён.

```csharp
using Atom.Text;

var raw = "   user_name   ";
var camel = raw.NormalizeWhitespace().ToCamelCase();    // userName
var title = UnixStyleFormatter.Highlight("INFO", "Выполнено успешно");
```

## 3. Форматирование в Unix-стиле

`UnixStyleFormatter` помогает строить цветные и выровненные сообщения для
терминала:

```csharp
var output = UnixStyleFormatter.Table(
    headers: ["Status", "Message"],
    rows: new[]
    {
        new[] { "✔", "All systems green" },
        new[] { "✖", "Replication lag" },
    });
Console.WriteLine(output);
```

## 4. Алгоритмы поиска

Перечисление `SubstringSearchAlgorithm` позволяет выбрать реализацию из модуля
`Atom.Algorithms.Text` (KMP, Rabin–Karp, Boyer–Moore и т. д.) при форматировании
или анализе текста.

## Практические советы

- При генерации JSON-контекста указывайте дополнительные настройки сериализации
  через параметры атрибута `[JsonContext(...)]` — они попадут в сгенерированный
  `JsonSerializerOptions`.
- Подключайте события `SerializationFailed`/`OptionsChanged`, чтобы централизованно
  обрабатывать ошибки и обновление конфигурации.
- Методы `TextExtensions` работают со `ReadOnlySpan<char>` и минимизируют
  аллокации — используйте их в горячих участках кода.
- Комбинируйте `UnixStyleFormatter` с цветами (`TextExtensions.TryGetColor`) для
  создания читаемых CLI-клиентов.

