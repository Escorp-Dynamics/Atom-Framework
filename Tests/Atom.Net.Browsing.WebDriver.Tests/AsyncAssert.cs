namespace Atom.Net.Browsing.WebDriver.Tests;

/// <summary>
/// Проверки исключений асинхронного кода, одинаково работающие в NUnit 4 и 5: в NUnit 5
/// <c lang="text">Assert.ThrowsAsync</c> сам стал асинхронным и сменил тип результата.
/// </summary>
internal static class AsyncAssert
{
    public static async Task<TException> ThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (TException exception) when (exception.GetType() == typeof(TException))
        {
            return exception;
        }
        catch (Exception exception)
        {
            Assert.Fail($"Ожидалось {typeof(TException).Name}, получено {exception.GetType().Name}: {exception.Message}");
        }

        Assert.Fail($"Ожидалось {typeof(TException).Name}, но исключения не было.");
        return null!;
    }

    public static async Task<Exception> CatchAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return exception;
        }

        Assert.Fail("Ожидалось исключение, но его не было.");
        return null!;
    }
}
