using Microsoft.Extensions.Logging;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Atom.Debug.Logging.Tests;

public static partial class LoggerExtensions
{
    [LoggerMessage(1, LogLevel.Information, "Test Information", SkipEnabledCheck = true)]
    public static partial void TestInformation(this ILogger logger);

    [LoggerMessage(2, LogLevel.Information, "Test Information 2", SkipEnabledCheck = true)]
    public static partial void TestInformation2(this ILogger logger);

    [LoggerMessage(3, LogLevel.Information, "Test Information with scope", SkipEnabledCheck = true)]
    public static partial void TestInformationWithScope(this ILogger logger);

    [LoggerMessage(4, LogLevel.Debug, "Starting transaction processing", SkipEnabledCheck = true)]
    public static partial void StartingTransactionProcessing(this ILogger logger);

    [LoggerMessage(5, LogLevel.Information, "Processing transaction step 1 (subtask)", SkipEnabledCheck = true)]
    public static partial void SubtaskTransactionStep1(this ILogger logger);

    [LoggerMessage(6, LogLevel.Information, "Processing transaction step 2 (subthread)", SkipEnabledCheck = true)]
    public static partial void SubtaskTransactionStep2(this ILogger logger);

    [LoggerMessage(7, LogLevel.Information, "Processing transaction step 3 (subtask)", SkipEnabledCheck = true)]
    public static partial void SubtaskTransactionStep3(this ILogger logger);

    [LoggerMessage(8, LogLevel.Information, "Transaction processing completed", SkipEnabledCheck = true)]
    public static partial void TransactionProcessingCompleted(this ILogger logger);

    [LoggerMessage(9, LogLevel.Critical, "Async test 1: {I}", SkipEnabledCheck = true)]
    public static partial void AsyncTest1(this ILogger logger, int i);

    [LoggerMessage(10, LogLevel.Information, "Async test 2: {I}", SkipEnabledCheck = true)]
    public static partial void AsyncTest2(this ILogger logger, int i);

    [LoggerMessage(11, LogLevel.Error, "Thread test 1: {I}", SkipEnabledCheck = true)]
    public static partial void ThreadTest1(this ILogger logger, int i);

    [LoggerMessage(12, LogLevel.Warning, "Thread test 2: {I}", SkipEnabledCheck = true)]
    public static partial void ThreadTest2(this ILogger logger, int i);

    [LoggerMessage(13, LogLevel.Information, "IAsyncEnumerable test 1: {I}", SkipEnabledCheck = true)]
    public static partial void IAsyncEnumerableTest1(this ILogger logger, int i);

    [LoggerMessage(14, LogLevel.Trace, "IAsyncEnumerable test 2: {I}", SkipEnabledCheck = true)]
    public static partial void IAsyncEnumerableTest2(this ILogger logger, int i);
}