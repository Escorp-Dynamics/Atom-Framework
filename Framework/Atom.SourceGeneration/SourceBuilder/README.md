# SourceBuilder

Набор вспомогательных строителей для генерации C#-кода в источниках Atom. Модуль предоставляет объекты-пулы и fluent-API для формирования `class`, `interface`, `enum`, а также их членов, оформляя директивы, `using`, комментарии и ограничения типов.

## Быстрый старт

```csharp
var source = SourceBuilder.Create()
    .WithDirective("#pragma warning disable CS0109")
    .WithNamespace("MyProject.Generated")
    .WithUsing("System.Text.Json", "System.Diagnostics.CodeAnalysis")
    .WithClass(
        ClassEntity.Create("SampleContext", AccessModifier.Public)
            .AsPartial()
            .WithGeneric(GenericEntity.Create("T", "class?"))
            .WithField(FieldMember.Create<JsonSerializerOptions>("options")
                .AsReadOnly()
                .WithValue("new(JsonSerializerDefaults.Web)"))
            .WithProperty(PropertyMember.CreateWithGetterOnly<JsonTypeInfo>("TypeInfo", AccessModifier.Public))
            .WithMethod(MethodMember.Create("Serialize", AccessModifier.Public)
                .WithArgument(MethodArgumentMember.Create("value").WithType("T?"))
                .WithType<string?>()
                .WithCode("JsonSerializer.Serialize(value, TypeInfo)")
                .AsStatic()
                .AsAsync())
    )
    .Build(release: true);
```

`Build(release: true)` возвращает построенную строку и сбрасывает все задействованные строители обратно в общий пул.

## Архитектура и жизненный цикл

- `SourceBuilder` управляет директивами, блоком `using`, пространством имён и корневыми сущностями. Экземпляры берутся из `ObjectPool<SourceBuilder>` и по умолчанию подключают базовые `using` (`System`, `System.Collections.*`, `System.Threading.*`, `Atom`).
- Все строители реализуют `IEntity` и используют `SparseArray<T>` для сохранения порядка добавленных элементов.
- После вызова `Build(release: true)` или явного `Release()` объект очищается: скидываются коллекции, комментарии, имена и флаги.
- Если `Build()` вызываетcя без сущностей, возвращается `null`.

> Совет: когда строителя нужно переиспользовать в цикле, берите его через `Create()`, вызывайте `Build(release: true)` и доверяйте пулу повторное использование.

## Работа с `ISourceBuilder`

| Метод | Назначение |
|-------|------------|
| `WithDirective(string)` | Добавляет сырые директивы (`#nullable`, `#pragma`). Порядок сохраняется. |
| `WithUsing(string)` | Подключает `using`. Во время сборки используется для сокращения полных имён типов. |
| `WithNamespace(string?)` | Задаёт `namespace;`. Пустое или `null` пропустит объявление. |
| `WithClass / WithInterface / WithEnum` | Добавляет корневые сущности. Можно вызывать несколько раз. |
| `WithEntity(IEntity)` | Для произвольных сущностей, в том числе вложенных. |
| `Build(bool release = false)` | Возвращает готовый исходник. При `release == true` освобождает экземпляр и все дочерние сущности. |

## Общие возможности сущностей

Каждая сущность предоставляет методы `WithComment`, `WithAttribute`, `WithName` и свойство `IsValid`. Комментарии `"<inheritdoc/>"` вставляются без лишних `<summary>`. Атрибуты выводятся в порядке добавления.

### GenericEntity

- Представляет параметр типа и его ограничения.
- `WithLimitation("struct", "ISerializable")` формирует `where T : struct, ISerializable`.
- Флаги `AsIn()` и `AsOut()` пока только помечают состояние (значение доступно из объекта), фактический вывод модификаторов нужно задавать вручную в `Name` (например, `WithName("in T")`).

### EnumEntity и EnumMember

- `EnumEntity` поддерживает `WithType<T>()`, `AsFlags()` и работу со значениями через `EnumMember`.
- Значение `EnumMember` по умолчанию `-1` — в этом случае компилятор подставит автоматическую нумерацию.
- Атрибут `Flags` добавляется хелпером `AsFlags()` (`[Flags]`).

### InterfaceEntity

- Поддерживает родителей (`WithParent`), параметры типов (`WithGeneric`), свойства, события, методы и вложенные сущности (`WithOther`).
- Методы для интерфейса автоматически получают `IsInterface = true`, поэтому при отсутствии тела завершаются `;`.
- Флаги `AsPartial()` и `AsUnsafe()` управляют сигнатурой.

### ClassEntity

- Наследует все возможности `InterfaceEntity` и добавляет поля (`WithField`), флаги `AsStatic`, `AsSealed`, `AsUnsafe`.
- `WithOther` позволяет вкладывать любые `IEntity` (например, вспомогательные `enum`).
- Коллекции находятся в `SparseArray`, порядок вывода соответствует порядку вызовов `With*`.
- Пустой класс без членов завершается `;`; при наличии членов выводится тело с корректными отступами.

## Члены (`Member<T>`) и специализированные строители

### FieldMember

- Флаги: `AsStatic`, `AsReadOnly`, `AsVolatile`, `AsConstant`, `AsRef`.
- `WithValue("SomeFactory()")` принимает готовое выражение, `WithValue(object)` строку обрамляет в кавычки автоматически.
- При `AsConstant()` игнорируются `IsStatic`, `IsReadOnly`, `IsVolatile`, `IsRef`.

### PropertyMember + Accessor/Mutator

- Для авто-свойств достаточно вызвать `WithGetter()` и/или `WithSetter()` без тела — выводится `get;` / `set;`.
- `WithGetter("field")` генерирует `get => field;`. Многострочный код оборачивается в блок с нужными отступами.
- `WithSetter(string body, bool isInit)` позволяет строить `init`-аксессоры, автоматически удаляя завершающий `;`.
- `WithInitialValue("Expression")` добавляет инициализацию без повторного `;`.
- Внутренние `PropertyAccessorMember` и `PropertyMutatorMember` наследуют модификатор доступа от свойства, если явно не задан.

### EventMember + Add/Remove

- Для простого события достаточно `WithAdder()` и `WithRemover()` без тела — выводится авто-событие.
- Можно собирать только подписчик (`CreateWithAdderOnly`) или отписчик (`CreateWithRemoverOnly`).
- `WithAdder(string body, bool isReadOnly)` позволяет получить `readonly add`.

### MethodMember и MethodArgumentMember

- Поддерживают флаги `AsPartial`, `AsVirtual`, `AsOverride`, `AsAsync`, `AsUnsafe`, `AsNew`, `AsRef`/`AsReadOnlyRef`.
- `WithCode` сам добавляет завершающий `;`, если нужно, и оформляет многострочные блоки.
- Аргументы создаются через `MethodArgumentMember.Create<T>("name")` и модифицируются `AsIn/Out/Ref/Params`.
- `WithGeneric("T", "IComponent")` добавляет `where T : IComponent` после сигнатуры.
- Метод не вставляет `this` для аргументов-расширений: вызов `AsExtension()` лишь фиксирует состояние внутри объекта, поэтому modifier придётся добавить вручную (например, через `WithAttribute("this")` или прямую правку `Name`).

## Практические приёмы

- **Управление `using`.** Builder передаёт всем дочерним сущностям агрегированный список пространств имён и `namespace`, отсортированный по длине. Благодаря этому `GetTypeName` удаляет полные квалификаторы (`SourceBuilder.cs`). Добавляйте все используемые `using`, чтобы `List<T>` или `JsonTypeInfo` выводились без `global::`.
- **Пулы.** После `Build(release: true)` дочерние сущности (поля, методы, т. п.) автоматически вызывают `Release()` и возвращаются в `ObjectPool<T>`. Если вы вызываете `Build(release: false)` для повторного использования результата, не забудьте позже вызвать `Release()` вручную, чтобы не копить объекты.
- **Комментарии.** Используйте `WithComment("<inheritdoc/>")`, чтобы сохранить XML-документацию базового члена без обёртки `<summary>`.
- **Вложенные сущности.** Для генерации вложенных типов добавляйте их через `WithOther` (у классов/интерфейсов) или напрямую через `WithEntity` на `SourceBuilder`.
- **Тестовые образцы.** Подготовленные эталонные файлы (`Tests/Atom.SourceGeneration.Tests/SourceBuilder/assets/*.reference`) показывают, как выглядят разные комбинации флагов.

## Известные особенности и ограничения

- `GenericEntity.AsIn()` / `AsOut()` не добавляют модификаторы в итоговую строку — при необходимости включите `in`/`out` прямо в `WithName`.
- `MethodArgumentMember.AsExtension()` не добавляет ключевое слово `this` к параметру. При генерации методов-расширений модификатор придётся вставлять самостоятельно.
- `WithValue(object)` на полях и свойствах строки оборачивает в кавычки — передавайте выражения в строковом виде, чтобы избежать двойных кавычек.
- `WithCode` не вставляет `await` автоматически. При использовании `AsAsync()` убедитесь, что тело метода корректно завершается или содержит возвращаемое значение.
- Для многострочных блоков builder полагается на символы `\n`; убедитесь, что строковые литералы используют Unix-переносы.

## Дополнительные ресурсы

- Юнит-тесты и эталонные выводы: `Tests/Atom.SourceGeneration.Tests/SourceBuilder/**/*`.
- Пример боевого использования в JSON-генераторе: `Framework/Atom.SourceGeneration/Text/Json/JsonContextTypeSyntaxProvider.cs`.