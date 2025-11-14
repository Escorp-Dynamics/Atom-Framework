# Architect

Сорс-генераторы для инфраструктуры компонентной модели и реактивных полей в
Atom. Модуль состоит из двух подсистем:

1. **Components** — генерирует обвязку вокруг `ComponentAttribute` и
   `ComponentOwnerAttribute`.
2. **Reactive** — расширяет поля, отмеченные `ReactivelyAttribute`, автоматикой
   уведомлений и инфраструктурой `INotifyPropertyChanged`/`Changing`.

## 1. Components

### Как использовать

```csharp
[Component]
public partial class GameplayComponent : Component
{
    public override void Reset() { /* ... */ }
}

[ComponentOwner]
public partial class GameplayEntity
{
    // будет расширен методами Use/UnUse/Has/TryGet и пр.
}
```

При сборке:
- `ComponentTypeSyntaxProvider` находит `partial`-типы с `[Component]` и
  дописывает обязательные члены (`Owner`, `AttachTo`, `Detach` и т. п.).
- `ComponentOwnerTypeSyntaxProvider` формирует менеджер для владельца, включая
  поле `components`, методы работы с пулом (`Use`, `UnUse`), проверки
  поддерживаемых типов и события отсоединения.
- Анализаторы (`ComponentSourceAnalyzer`, `ComponentOwnerSourceAnalyzer`) дают
  быстрый фидбек в IDE: если атрибут найден, диагностируется `A0001`.

### Особенности

- Генерация происходит только для `partial`-типов. Если класс не `partial`,
  провайдер игнорирует его.
- `ComponentOwner` учитывает наличие специализированных ограничений через
  `ComponentOwnerAttribute.Type` и генерирует проверку в `IsSupported<T>()`.
- Методы `Use` умеют работать с `ObjectPool<T>` и автоматически отбрасывают
  объекты в пул, если компонент помечен `IsAttachedByOwner`.
- Тестовые эталоны: `Tests/Atom.SourceGeneration.Tests/Architect/Components/*.reference`.

## 2. Reactive

### Как использовать

```csharp
public abstract partial class ReactiveViewModel : Component
{
    [Reactively(IsVirtual = true)]
    private string _title = string.Empty;
}
```

`ReactivelyFieldSyntaxProvider` генерирует для поля:
- публичное свойство с геттером/сеттером, вызывающим `SetProperty`;
- события `<Name>Changing` и `<Name>Changed` c проксированием к общим событиям;
- перегрузки `OnPropertyChanging/Changed` с опциональной `virtual`
  модификацией (если тип не `sealed`).

Анализатор `ReactivelySourceAnalyzer` подсвечивает наличие атрибута и помогает
запустить генератор только для изменённых узлов.

### Особенности

- Атрибут поддерживает переименование свойства через `PropertyName` и смену
  модификатора доступа. Для `sealed`-типов защищённые члены автоматически
  понижаются до `private`.
- Комментарии XML переносятся из исходного поля (`TryParseXmlDocumentation`).
- При наличии нескольких переменных в одном объявлении генератор обрабатывает
  каждую по отдельности.
- Эталонный вывод: `Tests/Atom.SourceGeneration.Tests/assets/component*.reference`.

## Продакшн-рекомендации

- Всегда подключайте сборку `Escorp.Atom.dll` в тестах генератора — см. примеры
  в `Tests/Atom.SourceGeneration.Tests/Architect/**`.
- При изменении контрактов (например, добавлении новых методов в компонент)
  обновляйте `ComponentTypeSyntaxProvider` и соответствующие эталонные файлы.
- Используйте `Build(release: true)` после формирования исходника, чтобы
  освобождать объекты-пулы (`SourceBuilder`, `ClassEntity` и др.).

