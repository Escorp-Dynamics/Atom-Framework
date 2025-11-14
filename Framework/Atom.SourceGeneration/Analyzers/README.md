# Analyzers

Инфраструктура Roslyn-анализаторов, используемая сорс-генераторами Atom. Набор
базовых типов позволяет быстро поднять анализатор, связанный c генератором кода,
и переиспользует одинаковую Diagnostics-политику.

## Быстрый старт

```csharp
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SampleAnalyzer : SourceAnalyzer<MyAttributeAnalyzer>
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => [AttributeAnalyzerSyntaxProvider.DefaultRule];
}

public sealed class MyAttributeAnalyzer : AttributeAnalyzerSyntaxProvider
{
    public override string Id => "SampleAnalyzer";
    public override string Attribute => "MyMarker";
}
```

Такой анализатор:
1. Включает конкурентный анализ и учитывает сгенерированный код (`SourceAnalyzer`).
2. Следит за вхождениями `[MyMarker]` и репортит диагностическое правило `A0001`.

## Ключевые элементы

- `IAnalyzerSyntaxProvider` — контракт тесно связанный с `AnalysisContext`.
- `AnalyzerSyntaxProvider` — базовый адаптер без привязки к конкретному синтаксису.
- `AttributeAnalyzerSyntaxProvider` — специализация, фильтрующая атрибуты и
  репортящая `DefaultRule` с Id `A0001` (severity Hidden).
- `SourceAnalyzer<TProvider>` — тонкая обёртка над `DiagnosticAnalyzer`, которая
  автоматически регистрирует провайдер через `AnalysisContext.UseProvider`.

## Связка с генераторами

Используйте `Extensions.UseProvider` и передайте туда существующий
`IAnalyzerSyntaxProvider`. В генераторах это выглядит так:

```csharp
public void Initialize(IncrementalGeneratorInitializationContext context)
    => context.UseProvider(new MyTypeSyntaxProvider(context),
        new MyAttributeAnalyzer());
```

Диагностическое событие пересобирает источник для затронутого узла: после того
как анализатор репортит `A0001`, `UseProvider` повторно вызывает `Execute` у
соответствующего генератора только для проблемного узла.

## Продакшн-нюансы

- **Идентификаторы.** Следите, чтобы `Id` провайдера было уникальным для всей
  сборки, иначе расширение не сможет сопоставить диагностики с генератором.
- **Наборы SyntaxKind.** Внутри `AnalyzerSyntaxProvider.SyntaxKinds` ограничьте
  событие максимально узким набором (атрибуты по умолчанию работают с
  `SyntaxKind.Attribute`). Это снижает нагрузку на компилятор.
- **Severity.** Базовое правило скрыто (`Hidden`). Введите своё `DiagnosticDescriptor`,
  если нужно предупреждать или ошибку, переопределив `Rule`.
- **Тесты.** Примеры интеграции расположены в
  `Tests/Atom.SourceGeneration.Tests/**/AnalyzerTestAsync`. Они показывают, как
  использовать `CSharpAnalyzerTest` вместе с `Escorp.Atom.dll`.

