# Architect

Набор фундаментальных абстракций, на которых строится высокоуровневая
архитектура Atom. Модуль описывает контракты компонентов, владельцев и
реактивных сущностей, а также вспомогательные билдеры, которые используются
сорс-генераторами `Atom.SourceGeneration`.

## Состав модуля

| Подмодуль | Что содержит | Когда использовать |
|-----------|--------------|--------------------|
| `Components` | `ComponentAttribute`, `ComponentOwnerAttribute`, интерфейсы `IComponent`, `IComponentOwner` | Модульная архитектура, DI, динамическое подключение поведения |
| `Reactive` | `IReactively`, события `PropertyChanging`/`PropertyChanged`, вспомогательные типы | Автоматическое оповещение при изменении полей и свойств |
| `Builders` | Базовый контракт `IBuilder<T>` | Сборка объектов по паттерну Builder |
| `Factories` | Расширения и фабрики для компонентов | Создание компонентов из DI/пула |

## Компонентная модель

```csharp
using Atom.Architect.Components;

[Component]
public partial class HealthComponent : Component
{
    public int Value { get; private set; }

    public void Damage(int amount) => Value = Math.Max(0, Value - amount);
}

[ComponentOwner]
public partial class Player
{
    public Player UseHealth()
        => Use<HealthComponent>().Damage(10);
}
```

После компиляции сорс-генератор добавит в `Player`:

- поле `components` и обвязку с `ObjectPool<HealthComponent>`;
- методы `Use`, `UnUse`, `Has`, `TryGet`, `TryGetAll`, `GetAll`;
- проверку поддерживаемых компонентов через `IsSupported<T>()`.

### Жизненный цикл компонента

`Component` предоставляет события `Attached`/`Detached` и методы `OnAttached`
/`OnDetached`. Эти события проксируются к владельцу, что позволяет централизованно
обрабатывать подключение модулей.

```csharp
public partial class HealthComponent : Component
{
    protected override void OnAttached(IComponentOwner owner)
    {
        base.OnAttached(owner);
        Console.WriteLine($"Component attached to {owner}");
    }
}
```

## Реактивные поля

```csharp
using Atom.Architect.Reactive;

public abstract partial class ViewModel
{
    [Reactively(PropertyName = "Name", IsVirtual = false)]
    private string _name = string.Empty;
}
```

Генератор создаст:

- публичное свойство `Name` с `SetProperty(ref _name, value)`;
- события `NameChanging`/`NameChanged` и общие `PropertyChanging`/`PropertyChanged`;
- виртуальные методы `OnPropertyChanging`/`OnPropertyChanged`.

> Совет: в `sealed` классах защищённые методы автоматически понижаются до `private`,
чтобы не нарушать правила языка.

## Взаимодействие с билдерами и фабриками

Интерфейс `IBuilder<T>` позволяет определить единый способ конструирования
сложных объектов, например компонентов или реактивных моделей, а фабрики
обеспечивают создание и конфигурирование через DI.

```csharp
public interface IBuilder<T>
{
    T Build();
}

public sealed class PlayerBuilder : IBuilder<Player>
{
    public Player Build()
    {
        var player = new Player();
        player.Use<HealthComponent>();
        return player;
    }
}
```

## Практические рекомендации

- **Partial везде:** чтобы генераторы могли дописать код, классы компонентов,
  владельцев и реактивных сущностей должны быть `partial`.
- **Pooling:** методы `Use` и `UnUse` интегрированы с `ObjectPool` — если компонент
  реализует `IPooled`, он автоматически будет возвращён в пул.
- **События:** подключайте обработчики `Attached`/`Detached`, чтобы наблюдать за
  жизненным циклом компонентов.
- **Тестирование:** в модульных тестах создавайте экземпляры компонентов напрямую,
  а генератор подключайте через `Microsoft.CodeAnalysis.CSharp.SourceGenerators` и
  ссылку на `Escorp.Atom.dll`.

