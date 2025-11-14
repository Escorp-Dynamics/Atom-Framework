# Text

Генераторы для текстовых подсистем Atom. Сейчас включает JSON-контекст, который
упрощает работу с `System.Text.Json` и обеспечивает строгую типизацию `JsonTypeInfo`.

## JsonContext

### Подготовка

```csharp
[JsonContext(typeof(MyDto))]
public partial class MyDtoJsonContext : JsonSerializerContext
{
}
```

- Тип должен быть `partial` и унаследован от `JsonSerializerContext`.
- Параметры атрибута задают сущности, для которых стоит сгенерировать метаданные.

### Что генерируется

`JsonContextTypeSyntaxProvider` создаёт файл `<EntityName>.Json.g.cs`, добавляя:

- поле `TypeInfo` и кешируемый `JsonTypeInfo` для описываемого типа;
- перегрузки `Serialize`, `Deserialize` (`Sync`, `Async`, `Utf8`, `Node` и т. д);
- события `SerializationFailed`/`OptionsChanged` (через `MutableEventHandler`);
- инициализацию `JsonSerializerOptions` и регистрацию их в конструкторе.

Результат основан на `SourceBuilder` и покрывает большинство сценариев работы
с `JsonSerializer`. Эталон: `Tests/Atom.SourceGeneration.Tests/assets/json.reference`.

### Анализатор и генератор

`JsonContextSourceAnalyzer` замечает `[JsonContext]` и репортит диагностику
`A0001`. Генератор (`JsonContextSourceGenerator`) запускается через
`Extensions.UseProvider`, поэтому диагностика автоматически инициирует повторную
генерацию только для изменённых контекстов.

### Особенности в продакшне

- **Пулы строк.** В генераторе активно используются `ObjectPool<T>` — обязательно
  вызывайте `Build(release: true)` после подготовки исходника в собственных
  расширениях.
- **Usings.** Генератор добавляет набор `using` (`System.Text.Json`, `Nodes`,
  `JsonSerializerOptions` и др.). При расширении учитывайте, что список очищается
  и сортируется в `SourceBuilder`.
- **Проверка base-реализаций.** Перед генерацией некоторых членов выполняется
  `HasBaseImplementation`, поэтому, если вы определите метод вручную, дублирующий
  код не появится.
- **Тесты.** См. `JsonContextTypeSyntaxProviderTests` (анализатор и генератор)
  и эталон `json_derived.reference` для производных контекстов.

