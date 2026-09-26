namespace Atom.Net;

/// <summary>
/// Наблюдение за задачами, результата которых никто не дожидается.
/// </summary>
/// <remarks>
/// ★ Задача, упавшая с исключением, которого никто не наблюдал, не исчезает вместе со ссылкой на
/// неё: сборщик доводит её до финализатора, и тот поднимает
/// <see cref="TaskScheduler.UnobservedTaskException"/>. У приложения, где это настроено бросать
/// (ThrowUnobservedTaskExceptions), процесс на этом и заканчивается — в чужом потоке, со стеком
/// чужой операции и спустя неопределённое время после самого сбоя.
///
/// Бьёт это прежде всего по СЛУЖЕБНЫМ путям: закрытие соединения, кадр отказа, разбор
/// служебного потока. Там ошибка ожидаема и никому не нужна, но именно она и роняет процесс,
/// тогда как настоящие ошибки запросов доходят до вызывающей стороны штатно.
///
/// Стоит наблюдение одного обращения к <see cref="Task.Exception"/>: оно помечает ошибку
/// обработанной для финализатора и НИЧЕГО не меняет для того, кто задачу всё-таки дождётся, —
/// <see langword="await"/> по-прежнему бросит то же самое исключение. Поэтому метод пригоден и
/// для «выстрелил и забыл», и для задач, за которыми кто-то может прийти позже.
/// </remarks>
internal static class BackgroundTask
{
    /// <summary>
    /// Помечает будущую ошибку задачи наблюдённой, не дожидаясь её завершения.
    /// </summary>
    /// <param name="task">Задача, за результатом которой никто не придёт.</param>
    public static void Forget(Task task)
    {
        if (task.IsCompleted)
        {
            Observe(task);
            return;
        }

        _ = task.ContinueWith(
            static completed => Observe(completed),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>
    /// Помечает ошибку уже завершённой задачи наблюдённой.
    /// </summary>
    /// <param name="task">Завершённая задача.</param>
    /// <remarks>
    /// Обращение к свойству и есть всё действие: у незавершённой или успешной задачи оно вернёт
    /// <see langword="null"/> и ничего не изменит.
    /// </remarks>
    public static void Observe(Task task) => _ = task.Exception;
}
