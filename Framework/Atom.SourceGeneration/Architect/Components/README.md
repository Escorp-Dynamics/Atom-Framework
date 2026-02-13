# Components

Генераторы для компонентной модели Atom. Позволяют автоматически реализовать
паттерн «Entity-Component» с событиями жизненного цикла и типобезопасным
управлением компонентами.

## Атрибуты

### ComponentAttribute

Помечает тип как компонент, который можно присоединять к владельцу.

```csharp
[Component]
public partial class HealthComponent
{
    public int MaxHealth { get; set; } = 100;
    public int CurrentHealth { get; set; }
}
```

### ComponentOwnerAttribute

Помечает тип как владельца компонентов с автоматической генерацией методов
управления.

```csharp
[ComponentOwner]
public partial class Entity
{
    public string Name { get; set; }
}

// Или с ограничением типа компонентов:
[ComponentOwner(typeof(IGameComponent))]
public partial class GameEntity
{
}
```

## Генерируемый код

### Для Component

`ComponentTypeSyntaxProvider` генерирует:

- Свойство `Owner` — ссылка на владельца компонента
- Свойство `IsAttached` — присоединён ли компонент
- Свойство `IsAttachedByOwner` — был ли присоединён владельцем (для пула)
- Событие `Attached` — срабатывает при присоединении
- Событие `Detached` — срабатывает при отсоединении
- Метод `AttachTo(IComponentOwner)` — присоединение к владельцу
- Метод `Detach()` — отсоединение от владельца
- Методы `OnAttached/OnDetached` — виртуальные хуки (для не-sealed типов)

### Для ComponentOwner

`ComponentOwnerTypeSyntaxProvider` генерирует:

- Поле `components` — внутреннее хранилище компонентов
- Метод `Use<T>()` — добавляет/получает компонент типа T
- Метод `UnUse<T>()` — удаляет компонент и возвращает в пул
- Метод `Has<T>()` — проверяет наличие компонента
- Метод `TryGet<T>(out T)` — безопасное получение компонента
- Метод `IsSupported<T>()` — проверка поддержки типа компонента
- Событие `ComponentDetached` — для очистки при уничтожении владельца

## Пример использования

```csharp
[Component]
public partial class TransformComponent
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Rotation { get; set; }
}

[Component]
public sealed partial class RenderComponent
{
    public string Sprite { get; set; }
    public int Layer { get; set; }
}

[ComponentOwner]
public partial class GameObject
{
    public string Name { get; set; }
}

// Использование:
var entity = new GameObject { Name = "Player" };

// Добавление компонентов
var transform = entity.Use<TransformComponent>();
transform.X = 100;
transform.Y = 200;

var render = entity.Use<RenderComponent>();
render.Sprite = "player.png";

// Проверка и получение
if (entity.Has<TransformComponent>())
{
    Console.WriteLine($"Position: {transform.X}, {transform.Y}");
}

// Удаление компонента
entity.UnUse<RenderComponent>();
```

## Sealed-типы

Для `sealed` компонентов генерируются `private` методы `OnAttached/OnDetached`
вместо `protected virtual`:

```csharp
[Component]
public sealed partial class AudioComponent
{
    // OnAttached и OnDetached будут private
}
```

## Интеграция с ObjectPool

Если компонент реализует `IPooled`, владелец автоматически возвращает его в пул
при вызове `UnUse<T>()`:

```csharp
[Component]
[Pooled]
public partial class ParticleComponent : IPooled
{
    private List<Particle> particles = new();

    public void Reset()
    {
        particles.Clear();
    }
}

// При UnUse компонент будет возвращён в ObjectPool
entity.UnUse<ParticleComponent>();
```

## Диагностика

| ID | Severity | Описание |
|----|----------|----------|
| `A0001` | Hidden | Обнаружен атрибут `[Component]` или `[ComponentOwner]` |

## Файлы

- `ComponentTypeSyntaxProvider.cs` — провайдер для компонентов
- `ComponentOwnerTypeSyntaxProvider.cs` — провайдер для владельцев
- `ComponentSourceGenerator.cs` — генератор компонентов
- `ComponentOwnerSourceGenerator.cs` — генератор владельцев
- `*SourceAnalyzer.cs` — анализаторы для IDE-интеграции
