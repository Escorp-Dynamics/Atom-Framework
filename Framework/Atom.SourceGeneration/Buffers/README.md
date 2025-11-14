# Buffers

Генерация вспомогательных классов для объектов, работающих с `ObjectPool<T>`.
Модуль ориентирован на атрибут `PooledAttribute` и добавляет типовые API,
ускоряющие аренду и возврат объектов.

## Когда применять

Если класс реализует `IPooled` и имеет метод сброса, пометьте его `partial`
часть атрибутом:

```csharp
public interface IPooled
{
    void Reset();
}

[Pooled]
public partial class PacketBuffer : IPooled
{
    public void Reset()
    {
        // Очистка состояний
    }
}
```

## Что генерируется

`PooledMethodSyntaxProvider` создаёт файл `<TypeName>.Pooled.g.cs`, в котором
описан статический фасад:

- `public static T Rent<T>() where T : IPooled` — универсальный метод аренды.
- `public static PacketBuffer Rent()` — перегрузка, возвращающая конкретный тип
  (для удобства вызовов из кода).
- `public static void Return<T>(T value)` — возврат с вызовом `Reset()`.

Генератор автоматически добавляет ссылки на `System.Diagnostics.CodeAnalysis`
(для `DynamicallyAccessedMembers`) и `Atom.Buffers`. Пример итогового файла
можно увидеть в `Tests/Atom.SourceGeneration.Tests/assets/pooled.reference`.

## Диагностика

`PooledSourceAnalyzer` фиксирует вхождения `[Pooled]` и репортит `A0001` (Hidden
severity). Тесты (`PooledMethodSyntaxProviderTests.AnalyzerTestAsync`) показывают
пример использования `CSharpAnalyzerTest`.

## Особенности и best practices

- **Reset обязателен.** Генератор ожидает, что в типе есть метод `Reset()` — не
  забывайте реализовывать контракт `IPooled` вручную.
- **Partial-классы.** Корневой тип должен быть `partial`, иначе результат не
  будет включён в компиляцию.
- **Пулы с зависимостями.** Если объект требует фабрику, расширяйте сгенерированный
  фасад (идёт в отдельном partial-файле) и добавляйте свои перегрузки `Rent`.
- **Контроль nullable.** Возвращаемый тип — не nullable. Следите, чтобы `Reset`
  оставлял объект в пригодном состоянии для повторного использования.

