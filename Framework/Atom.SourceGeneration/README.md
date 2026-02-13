# Atom.SourceGeneration

Модуль `Atom.SourceGeneration` предоставляет инфраструктуру для создания Roslyn-генераторов исходного кода и анализаторов. Включает универсальные строители кода, базовые классы синтаксических провайдеров и готовые генераторы для часто используемых паттернов.

## Возможности

- **SourceBuilder** — fluent-API для программной генерации C#-кода
- **Analyzers** — инфраструктура Roslyn-анализаторов с унифицированной диагностикой
- **Generators** — базовые классы синтаксических провайдеров для инкрементальных генераторов
- **Готовые генераторы**:
  - `Buffers` — генерация фасадов для объектов пула (`IPooled`)
  - `Architect/Components` — компонентная модель с событиями присоединения/отсоединения
  - `Architect/Reactive` — реактивные свойства с `INotifyPropertyChanged`/`INotifyPropertyChanging`
  - `Text/Json` — типизированные контексты сериализации для `System.Text.Json`

## Установка

```xml
<PackageReference Include="Escorp.Atom.SourceGeneration" Version="*" />
```

## Быстрый старт

### Генерация кода с SourceBuilder

```csharp
var source = SourceBuilder.Create()
    .WithNamespace("MyProject.Generated")
    .WithUsing("System.Text.Json")
    .WithClass(
        ClassEntity.Create("GeneratedService", AccessModifier.Public)
            .AsPartial()
            .WithProperty<string>("Name")
            .WithMethod(MethodMember.Create("Initialize", AccessModifier.Public)
                .WithCode("Console.WriteLine(\"Initialized\");"))
    )
    .Build(release: true);
```

### Создание собственного генератора

```csharp
public sealed class MyTypeSyntaxProvider : TypeSyntaxProvider
{
    public MyTypeSyntaxProvider(IncrementalGeneratorInitializationContext ctx)
        : base(ctx) => WithAttribute("MyMarker");

    protected override void OnExecute(
        SourceProductionContext context,
        string entityName,
        ImmutableArray<ISyntaxProviderInfo<ITypeSymbol, TypeDeclarationSyntax>> sources)
    {
        var src = SourceBuilder.Create()
            .WithNamespace(sources[0].Symbol?.ContainingNamespace.ToDisplayString())
            .WithClass(ClassEntity.Create(entityName).AsPartial()
                .WithMethod(MethodMember.Create("GeneratedMethod")))
            .Build(release: true);

        if (!string.IsNullOrEmpty(src))
            context.AddSource($"{entityName}.g.cs", SourceText.From(src, Encoding.UTF8));
    }
}

[Generator]
public sealed class MyGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
        => context.UseProvider(new MyTypeSyntaxProvider(context));
}
```

### Использование готовых генераторов

#### Pooled (буферизация объектов)

```csharp
[Pooled]
public partial class DataBuffer : IPooled
{
    private byte[] _data = new byte[1024];

    public void Reset() => Array.Clear(_data);
}

// Использование:
var buffer = DataBuffer.Rent();
// ... работа с буфером ...
DataBuffer.Return(buffer);
```

#### Reactive (реактивные свойства)

```csharp
public partial class ViewModel
{
    [Reactively]
    private string _title = string.Empty;

    [Reactively(PropertyName = "FullName", IsVirtual = true)]
    private string _name = string.Empty;
}

// Генерируются свойства Title и FullName с событиями PropertyChanging/PropertyChanged
```

#### Component (компонентная модель)

```csharp
[Component]
public partial class HealthComponent
{
    public int CurrentHealth { get; set; }
}

[ComponentOwner]
public partial class Entity
{
    // Генерируются методы Use<T>(), UnUse<T>(), Has<T>(), TryGet<T>()
}
```

#### JsonContext (типизированная сериализация)

```csharp
[JsonContext(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class UserDto
{
    public string Name { get; set; }
    public int Age { get; set; }
}

// Использование:
var json = user.Serialize();
var restored = UserDto.Deserialize(json);
```

## Архитектура

```text
Atom.SourceGeneration/
├── Analyzers/           # Базовые классы анализаторов
├── Generators/          # Синтаксические провайдеры
├── SourceBuilder/       # Строители кода
│   └── Entities/        # Классы, интерфейсы, методы, свойства и т.д.
├── Architect/           # Генераторы архитектурных паттернов
│   ├── Components/      # Компонентная модель
│   └── Reactive/        # Реактивные свойства
├── Buffers/             # Генератор пулов объектов
└── Text/                # Текстовые генераторы
    └── Json/            # JSON-контексты
```

## Ключевые типы

### SourceBuilder

| Тип | Описание |
|-----|----------|
| `SourceBuilder` | Главный строитель исходного кода |
| `ClassEntity` | Строитель класса |
| `InterfaceEntity` | Строитель интерфейса |
| `EnumEntity` | Строитель перечисления |
| `FieldMember` | Строитель поля |
| `PropertyMember` | Строитель свойства |
| `MethodMember` | Строитель метода |
| `EventMember` | Строитель события |
| `GenericEntity` | Строитель параметра типа |

### Синтаксические провайдеры

| Тип | Описание |
|-----|----------|
| `TypeSyntaxProvider` | Провайдер для типов (class, struct, interface) |
| `FieldSyntaxProvider` | Провайдер для полей |
| `MethodSyntaxProvider` | Провайдер для методов |

### Анализаторы

| Тип | Описание |
|-----|----------|
| `SourceAnalyzer<T>` | Базовый анализатор с автоматической регистрацией |
| `AttributeAnalyzerSyntaxProvider` | Провайдер анализа атрибутов |

## Диагностика

| ID | Severity | Описание |
|----|----------|----------|
| `A0001` | Hidden | Обнаружен маркерный атрибут |
| `A1000` | Error | Необработанное исключение в генераторе |

## Пулы объектов

Все строители используют `ObjectPool<T>` для минимизации аллокаций. Вызывайте `Build(release: true)` для автоматического возврата объектов в пул:

```csharp
// Правильно - объекты возвращаются в пул
var source = SourceBuilder.Create()
    .WithClass(ClassEntity.Create("Test"))
    .Build(release: true);

// Если release: false, вызовите Release() вручную
var builder = SourceBuilder.Create();
var source = builder.Build();
// ... использование source ...
builder.Release();
```

## Тестирование генераторов

```csharp
[Test]
public async Task GeneratorTestAsync()
{
    var test = new CSharpSourceGeneratorTest<MyGenerator, DefaultVerifier>
    {
        TestState =
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            Sources = { sourceCode },
            GeneratedSources = { (typeof(MyGenerator), "Output.g.cs", expectedOutput) },
            AdditionalReferences =
            {
                MetadataReference.CreateFromFile(typeof(MyAttribute).Assembly.Location),
            },
        }
    };

    await test.RunAsync();
}
```

## Ссылки

- [Analyzers](Analyzers/README.md) — инфраструктура анализаторов
- [Generators](Generators/README.md) — синтаксические провайдеры
- [SourceBuilder](SourceBuilder/README.md) — строители кода
- [Buffers](Buffers/README.md) — генератор пулов
- [Architect](Architect/README.md) — архитектурные генераторы
- [Text](Text/README.md) — текстовые генераторы
