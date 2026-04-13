# Generators

Общая инфраструктура для инкрементальных сорс-генераторов Atom. Содержит
базовые `SyntaxProvider`-классы, интерфейсы и служебные структуры, которыми
пользуются модули `Architect`, `Buffers`, `Text` и др.

## Архитектура

- `ISyntaxProvider<TSymbol, TSyntaxNode>` — контракт для провайдеров, который
  описывает три шага Roslyn-пайплайна: `Predicate`, `Transform`, `Execute`.
- `SyntaxProvider<TSymbol, TSyntaxNode>` — реализация с поддержкой фильтрации по
  атрибутам, группировки по сущности (`ClassEntity`, `InterfaceEntity` и т. п.) и
  обработкой исключений (диагностика `A1000`).
- Специализации:
  - `TypeSyntaxProvider`
  - `FieldSyntaxProvider`
  - `MethodSyntaxProvider`
  - `PropertySyntaxProvider`
  - `MemberSyntaxProvider`

`SyntaxProviderInfo` вместе с `ISyntaxProviderInfo` несут `SyntaxNode`,
вычисленный `ISymbol` и имя атрибута, удовлетворяющего фильтру.

## Пример: генератор под атрибут

```csharp
public sealed class AuditTypeSyntaxProvider
    : TypeSyntaxProvider
{
    public AuditTypeSyntaxProvider(IncrementalGeneratorInitializationContext ctx)
        : base(ctx) => WithAttribute("Audited");

    protected override void OnExecute(SourceProductionContext context,
        string entityName,
        ImmutableArray<ISyntaxProviderInfo<ITypeSymbol, TypeDeclarationSyntax>> sources)
    {
        // Реализация генерации для всех partial-типов entityName
    }
}

[Generator]
public sealed class AuditGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
        => context.UseProvider(new AuditTypeSyntaxProvider(context));
}
```

## Особенности

- `WithAttribute` добавляет фильтр по имени атрибута (без суффикса `Attribute`).
  Если атрибутов нет, провайдер отрабатывает на каждый узел соответствующего
  `SyntaxKind` (`ClassDeclaration`, `FieldDeclaration`, `MethodDeclaration`).
- `Execute` группирует найденные узлы по родительскому типу, чтобы `OnExecute`
  получал все члены одной сущности за один вызов.
- Любое необработанное исключение репортится в компиляцию как `A1000` и
  приводит к генерации вспомогательного `#error` файла для диагностики.
- `HasBaseImplementation` помогает проверить, реализован ли член в базовом типе
  (учитываются и исходные файлы, и уже собранные сборки).

## `Extensions.UseProvider`

Метод-расширение на `IncrementalGeneratorInitializationContext` оборачивает
создание `SyntaxProvider` и регистрацию `SourceOutput`. Если передать ещё и
`IAnalyzerSyntaxProvider`, генератор будет повторно запускаться на узлах, где
анализатор сработал (см. `Extensions.cs`).
