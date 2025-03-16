namespace Atom.Web.Browsing.BiDi.Input;

/// <summary>
/// Представляет модуль Input, который содержит команды и события, связанные с симуляцией пользовательского ввода.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="InputModule"/>.
/// </remarks>
/// <param name="driver">Объект <see cref="BiDiDriver"/>, используемый в командах и событиях модуля.</param>
public sealed class InputModule(BiDiDriver driver) : Module(driver)
{
    /// <summary>
    /// Название модуля Input.
    /// </summary>
    public const string InputModuleName = "input";

    /// <summary>
    /// Название модуля.
    /// </summary>
    public override string ModuleName => InputModuleName;

    /// <summary>
    /// Выполняет набор действий.
    /// </summary>
    /// <param name="commandProperties">Параметры команды.</param>
    /// <returns>Пустой результат команды.</returns>
    public ValueTask<EmptyResult> PerformActionsAsync(PerformActionsCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.PerformActionsCommandParameters, JsonContext.Default.CommandResponseMessageEmptyResult);

    /// <summary>
    /// Освобождает ожидающие действия.
    /// </summary>
    /// <param name="commandProperties">Параметры команды.</param>
    /// <returns>Пустой результат команды.</returns>
    public ValueTask<EmptyResult> ReleaseActionsAsync(ReleaseActionsCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.ReleaseActionsCommandParameters, JsonContext.Default.CommandResponseMessageEmptyResult);

    /// <summary>
    /// Устанавливает файлы для элемента загрузки файлов. Элемент должен быть типа {input type="file"}.
    /// </summary>
    /// <param name="commandProperties">Параметры команды.</param>
    /// <returns>Пустой результат команды.</returns>
    public ValueTask<EmptyResult> SetFilesAsync(SetFilesCommandParameters commandProperties) => Driver.ExecuteCommandAsync(commandProperties, JsonContext.Default.SetFilesCommandParameters, JsonContext.Default.CommandResponseMessageEmptyResult);
}