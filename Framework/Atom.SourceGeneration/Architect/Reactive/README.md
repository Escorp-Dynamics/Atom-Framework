# Reactive

Генератор реактивных свойств с поддержкой `INotifyPropertyChanging` и
`INotifyPropertyChanged`. Автоматизирует создание свойств с уведомлениями
об изменениях из приватных полей.

## Атрибут ReactivelyAttribute

Помечает поле для генерации реактивного свойства:

```csharp
public partial class ViewModel
{
    [Reactively]
    private string _title = string.Empty;

    [Reactively(PropertyName = "FullName")]
    private string _name;

    [Reactively(AccessModifier = AccessModifier.Internal, IsVirtual = true)]
    private int _count;
}
```

### Параметры атрибута

| Параметр | Тип | По умолчанию | Описание |
|----------|-----|--------------|----------|
| `PropertyName` | `string?` | `null` | Имя генерируемого свойства (по умолчанию — имя поля без `_` с заглавной буквы) |
| `AccessModifier` | `AccessModifier` | `Public` | Модификатор доступа свойства |
| `IsVirtual` | `bool` | `false` | Генерировать виртуальное свойство |

## Генерируемый код

`ReactivelyFieldSyntaxProvider` для каждого поля создаёт:

### Базовую инфраструктуру (один раз на класс)

- Реализацию `IReactively` (наследует `INotifyPropertyChanging`, `INotifyPropertyChanged`)
- События `PropertyChanging` и `PropertyChanged`
- Метод `SetProperty<T>(ref T, T, string?)` — установка значения с уведомлением
- Методы `OnPropertyChanging/OnPropertyChanged` — виртуальные хуки

### Для каждого поля

- Публичное свойство с геттером и сеттером
- Событие `<PropertyName>Changing` — проксирует к `PropertyChanging`
- Событие `<PropertyName>Changed` — проксирует к `PropertyChanged`

## Пример

### Исходный код

```csharp
public partial class PersonViewModel
{
    /// <summary>
    /// Имя человека.
    /// </summary>
    [Reactively]
    private string _firstName = string.Empty;

    [Reactively]
    private string _lastName = string.Empty;

    [Reactively(PropertyName = "Age", IsVirtual = true)]
    private int _age;
}
```

### Сгенерированный код (упрощённо)

```csharp
public partial class PersonViewModel : IReactively
{
    public event PropertyChangingEventHandler? PropertyChanging;
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Имя человека.
    /// </summary>
    public string FirstName
    {
        get => _firstName;
        set => SetProperty(ref _firstName, value);
    }

    /// <summary>
    /// Происходит в момент изменения свойства <see cref="FirstName"/>.
    /// </summary>
    public event PropertyChangingEventHandler? FirstNameChanging
    {
        add => PropertyChanging += value;
        remove => PropertyChanging -= value;
    }

    /// <summary>
    /// Происходит после изменения свойства <see cref="FirstName"/>.
    /// </summary>
    public event PropertyChangedEventHandler? FirstNameChanged
    {
        add => PropertyChanged += value;
        remove => PropertyChanged -= value;
    }

    // Аналогично для LastName и Age...

    protected virtual bool SetProperty<T>(ref T storage, T value,
        [CallerMemberName] string? propertyName = default)
    {
        if (Equals(storage, value)) return false;

        OnPropertyChanging(storage, value, propertyName);
        storage = value;
        OnPropertyChanged(propertyName);

        return true;
    }

    protected virtual void OnPropertyChanging(object? oldValue, object? newValue,
        [CallerMemberName] string? propertyName = default)
        => PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(propertyName));

    protected virtual void OnPropertyChanged(
        [CallerMemberName] string? propertyName = default)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
```

## Sealed-типы

Для `sealed` классов:

- Защищённые методы становятся приватными
- Флаг `IsVirtual` игнорируется

```csharp
public sealed partial class SettingsViewModel
{
    [Reactively(IsVirtual = true)] // IsVirtual будет проигнорирован
    private bool _darkMode;

    // SetProperty, OnPropertyChanging, OnPropertyChanged будут private
}
```

## Преобразование имён

По умолчанию имя свойства формируется из имени поля:

- Удаляется префикс `_`
- Первая буква становится заглавной

| Поле | Свойство |
|------|----------|
| `_name` | `Name` |
| `_firstName` | `FirstName` |
| `name` | `Name` |
| `_URL` | `URL` |

Для нестандартных имён используйте `PropertyName`:

```csharp
[Reactively(PropertyName = "ID")]
private int _id;
```

## XML-документация

Комментарии автоматически переносятся с поля на свойство:

```csharp
/// <summary>
/// Адрес электронной почты пользователя.
/// </summary>
[Reactively]
private string _email;

// Свойство Email получит тот же комментарий
```

## Интеграция с MVVM

Идеально подходит для паттерна MVVM:

```csharp
public partial class MainViewModel : ViewModelBase
{
    [Reactively]
    private string _searchText = string.Empty;

    [Reactively]
    private ObservableCollection<Item> _items = new();

    [Reactively]
    private bool _isLoading;

    public ICommand SearchCommand => new RelayCommand(async () =>
    {
        IsLoading = true;
        Items = await _service.SearchAsync(SearchText);
        IsLoading = false;
    });
}
```

## Диагностика

| ID | Severity | Описание |
|----|----------|----------|
| `A0001` | Hidden | Обнаружен атрибут `[Reactively]` |

## Файлы

- `ReactivelyFieldSyntaxProvider.cs` — основной провайдер генерации
- `ReactivelySourceGenerator.cs` — точка входа генератора
- `ReactivelySourceAnalyzer.cs` — анализатор для IDE
- `ReactivelyAttributeAnalyzerSyntaxProvider.cs` — провайдер анализа атрибутов
