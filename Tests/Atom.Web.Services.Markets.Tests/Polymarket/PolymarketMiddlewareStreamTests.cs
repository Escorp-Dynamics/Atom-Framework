namespace Atom.Web.Services.Polymarket.Tests;

/// <summary>
/// Тесты для Request/Response middleware и Streaming prices API.
/// </summary>
public class PolymarketMiddlewareStreamTests(ILogger logger) : BenchmarkTests<PolymarketMiddlewareStreamTests>(logger)
{
    public PolymarketMiddlewareStreamTests() : this(ConsoleLogger.Unicode) { }

    #region PolymarketLoggingMiddleware

    [TestCase(TestName = "LoggingMiddleware: логирует запрос и ответ")]
    public async Task LoggingMiddlewareLogsTest()
    {
        var messages = new List<string>();
        var middleware = new PolymarketLoggingMiddleware(msg => messages.Add(msg));

        var reqCtx = new PolymarketRequestContext
        {
            Method = "GET",
            Path = "/markets",
            Url = "https://clob.polymarket.com/markets"
        };

        var proceed = await middleware.OnRequestAsync(reqCtx);
        Assert.That(proceed, Is.True);
        Assert.That(messages, Has.Count.EqualTo(1));
        Assert.That(messages[0], Does.Contain("GET /markets"));

        var respCtx = new PolymarketResponseContext
        {
            Request = reqCtx,
            StatusCode = 200,
            ElapsedMs = 42.5
        };

        await middleware.OnResponseAsync(respCtx);
        Assert.That(messages, Has.Count.EqualTo(2));
        Assert.That(messages[1], Does.Contain("200"));
        Assert.That(messages[1], Does.Match(@".*42[\.,]5ms.*"));
    }

    [TestCase(TestName = "LoggingMiddleware: логирует ошибку запроса")]
    public async Task LoggingMiddlewareLogsErrorTest()
    {
        var messages = new List<string>();
        var middleware = new PolymarketLoggingMiddleware(msg => messages.Add(msg));

        var reqCtx = new PolymarketRequestContext
        {
            Method = "POST",
            Path = "/order",
            Url = "https://clob.polymarket.com/order",
            IsAuthenticated = true
        };

        await middleware.OnRequestAsync(reqCtx);

        var respCtx = new PolymarketResponseContext
        {
            Request = reqCtx,
            StatusCode = 500,
            ElapsedMs = 100,
            Exception = new HttpRequestException("Server error")
        };

        await middleware.OnResponseAsync(respCtx);
        Assert.That(messages[1], Does.Contain("Server error"));
    }

    [TestCase(TestName = "LoggingMiddleware: конструктор по умолчанию не кидает")]
    public void LoggingMiddlewareDefaultConstructorTest()
    {
        IPolymarketMiddleware middleware = new PolymarketLoggingMiddleware();
        Assert.That(middleware, Is.Not.Null);
    }

    #endregion

    #region PolymarketMetricsMiddleware

    [TestCase(TestName = "MetricsMiddleware: начальные значения")]
    public void MetricsMiddlewareInitialValuesTest()
    {
        var metrics = new PolymarketMetricsMiddleware();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(metrics.TotalRequests, Is.EqualTo(0));
            Assert.That(metrics.FailedRequests, Is.EqualTo(0));
            Assert.That(metrics.AverageResponseTimeMs, Is.EqualTo(0));
        }
    }

    [TestCase(TestName = "MetricsMiddleware: подсчёт успешного запроса")]
    public async Task MetricsMiddlewareSuccessCountTest()
    {
        var metrics = new PolymarketMetricsMiddleware();

        var respCtx = new PolymarketResponseContext
        {
            Request = new PolymarketRequestContext { Method = "GET", Path = "/", Url = "/" },
            StatusCode = 200,
            ElapsedMs = 50
        };

        await metrics.OnResponseAsync(respCtx);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(metrics.TotalRequests, Is.EqualTo(1));
            Assert.That(metrics.FailedRequests, Is.EqualTo(0));
            Assert.That(metrics.StatusCodeStats[200], Is.EqualTo(1));
        }
    }

    [TestCase(TestName = "MetricsMiddleware: подсчёт ошибки")]
    public async Task MetricsMiddlewareFailureCountTest()
    {
        var metrics = new PolymarketMetricsMiddleware();

        var respCtx = new PolymarketResponseContext
        {
            Request = new PolymarketRequestContext { Method = "GET", Path = "/", Url = "/" },
            StatusCode = 500,
            ElapsedMs = 100,
            Exception = new HttpRequestException("fail")
        };

        await metrics.OnResponseAsync(respCtx);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(metrics.TotalRequests, Is.EqualTo(1));
            Assert.That(metrics.FailedRequests, Is.EqualTo(1));
            Assert.That(metrics.StatusCodeStats[500], Is.EqualTo(1));
        }
    }

    [TestCase(TestName = "MetricsMiddleware: среднее время ответа")]
    public async Task MetricsMiddlewareAverageTimeTest()
    {
        var metrics = new PolymarketMetricsMiddleware();

        for (var i = 1; i <= 3; i++)
        {
            var respCtx = new PolymarketResponseContext
            {
                Request = new PolymarketRequestContext { Method = "GET", Path = "/", Url = "/" },
                StatusCode = 200,
                ElapsedMs = i * 100 // 100, 200, 300
            };
            await metrics.OnResponseAsync(respCtx);
        }

        Assert.That(metrics.TotalRequests, Is.EqualTo(3));
        Assert.That(metrics.AverageResponseTimeMs, Is.EqualTo(200).Within(10));
    }

    [TestCase(TestName = "MetricsMiddleware: Reset сбрасывает метрики")]
    public async Task MetricsMiddlewareResetTest()
    {
        var metrics = new PolymarketMetricsMiddleware();

        await metrics.OnResponseAsync(new PolymarketResponseContext
        {
            Request = new PolymarketRequestContext { Method = "GET", Path = "/", Url = "/" },
            StatusCode = 200,
            ElapsedMs = 50
        });

        metrics.Reset();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(metrics.TotalRequests, Is.EqualTo(0));
            Assert.That(metrics.FailedRequests, Is.EqualTo(0));
            Assert.That(metrics.AverageResponseTimeMs, Is.EqualTo(0));
        }
    }

    [TestCase(TestName = "MetricsMiddleware: статистика по кодам")]
    public async Task MetricsMiddlewareStatusCodeStatsTest()
    {
        var metrics = new PolymarketMetricsMiddleware();

        for (var i = 0; i < 5; i++)
            await metrics.OnResponseAsync(MakeResponse(200));
        for (var i = 0; i < 3; i++)
            await metrics.OnResponseAsync(MakeResponse(429));
        await metrics.OnResponseAsync(MakeResponse(500, new HttpRequestException("err")));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(metrics.StatusCodeStats[200], Is.EqualTo(5));
            Assert.That(metrics.StatusCodeStats[429], Is.EqualTo(3));
            Assert.That(metrics.StatusCodeStats[500], Is.EqualTo(1));
            Assert.That(metrics.TotalRequests, Is.EqualTo(9));
        }
    }

    #endregion

    #region PolymarketHeadersMiddleware

    [TestCase(TestName = "HeadersMiddleware: добавление заголовков")]
    public async Task HeadersMiddlewareAddHeadersTest()
    {
        var middleware = new PolymarketHeadersMiddleware()
            .AddHeader("X-Custom", "value1")
            .AddHeader("X-Another", "value2");

        Assert.That(middleware.Headers, Has.Count.EqualTo(2));

        var reqCtx = new PolymarketRequestContext { Method = "GET", Path = "/", Url = "/" };
        var proceed = await middleware.OnRequestAsync(reqCtx);

        Assert.That(proceed, Is.True);
        Assert.That(reqCtx.Properties.ContainsKey("CustomHeaders"), Is.True);
    }

    [TestCase(TestName = "HeadersMiddleware: пустое имя кидает исключение")]
    public void HeadersMiddlewareEmptyNameThrowsTest()
    {
        var middleware = new PolymarketHeadersMiddleware();
        Assert.Throws<ArgumentException>(() => middleware.AddHeader("", "value"));
    }

    #endregion

    #region IPolymarketMiddleware — default implementations

    [TestCase(TestName = "IPolymarketMiddleware: OnRequest по умолчанию возвращает true")]
    public async Task MiddlewareDefaultOnRequestTest()
    {
        IPolymarketMiddleware middleware = new NoOpMiddleware();
        var reqCtx = new PolymarketRequestContext { Method = "GET", Path = "/", Url = "/" };
        var result = await middleware.OnRequestAsync(reqCtx);
        Assert.That(result, Is.True);
    }

    [TestCase(TestName = "IPolymarketMiddleware: OnResponse по умолчанию не кидает")]
    public async Task MiddlewareDefaultOnResponseTest()
    {
        IPolymarketMiddleware middleware = new NoOpMiddleware();
        var respCtx = new PolymarketResponseContext
        {
            Request = new PolymarketRequestContext { Method = "GET", Path = "/", Url = "/" },
            StatusCode = 200,
            ElapsedMs = 10
        };
        await middleware.OnResponseAsync(respCtx);
    }

    /// <summary>
    /// Middleware без переопределений — тестирует default interface implementations.
    /// </summary>
    private sealed class NoOpMiddleware : IPolymarketMiddleware;

    #endregion

    #region PolymarketRequestContext / PolymarketResponseContext

    [TestCase(TestName = "RequestContext: установка всех свойств")]
    public void RequestContextPropertiesTest()
    {
        var ctx = new PolymarketRequestContext
        {
            Method = "POST",
            Path = "/order",
            Url = "https://clob.polymarket.com/order",
            Body = """{"test":true}""",
            IsAuthenticated = true
        };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ctx.Method, Is.EqualTo("POST"));
            Assert.That(ctx.Path, Is.EqualTo("/order"));
            Assert.That(ctx.Body, Is.Not.Null);
            Assert.That(ctx.IsAuthenticated, Is.True);
            Assert.That(ctx.Properties, Is.Empty);
            Assert.That(ctx.TimestampTicks, Is.GreaterThan(0));
        }
    }

    [TestCase(TestName = "ResponseContext: IsSuccess для разных сценариев")]
    public void ResponseContextIsSuccessTest()
    {
        var reqCtx = new PolymarketRequestContext { Method = "GET", Path = "/", Url = "/" };

        var success = new PolymarketResponseContext { Request = reqCtx, StatusCode = 200 };
        var notFound = new PolymarketResponseContext { Request = reqCtx, StatusCode = 404 };
        var error = new PolymarketResponseContext { Request = reqCtx, StatusCode = 200, Exception = new Exception("err") };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(success.IsSuccess, Is.True);
            Assert.That(notFound.IsSuccess, Is.False);
            Assert.That(error.IsSuccess, Is.False);
        }
    }

    #endregion

    #region PolymarketPriceStream

    [TestCase(TestName = "PriceStream: конструктор по умолчанию")]
    public void PriceStreamDefaultConstructorTest()
    {
        using var stream = new PolymarketPriceStream();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stream.TokenCount, Is.EqualTo(0));
            Assert.That(stream.Prices, Is.Empty);
        }
    }

    [TestCase(TestName = "PriceStream: конструктор с существующим клиентом")]
    public void PriceStreamExistingClientTest()
    {
        using var client = new PolymarketClient();
        using var stream = new PolymarketPriceStream(client);

        Assert.That(stream, Is.Not.Null);
    }

    [TestCase(TestName = "PriceStream: null клиент кидает исключение")]
    public void PriceStreamNullClientThrowsTest()
    {
        Assert.Throws<ArgumentNullException>(() => new PolymarketPriceStream(null!));
    }

    [TestCase(TestName = "PriceStream: GetPrice для несуществующего токена")]
    public void PriceStreamGetPriceNotFoundTest()
    {
        using var stream = new PolymarketPriceStream();
        Assert.That(stream.GetPrice("nonexistent"), Is.Null);
    }

    [TestCase(TestName = "PriceStream: Dispose синхронный безопасен")]
    public void PriceStreamSyncDisposeTest()
    {
        var stream = new PolymarketPriceStream();
        stream.Dispose();
        Assert.DoesNotThrow(() => stream.Dispose()); // double dispose
    }

    [TestCase(TestName = "PriceStream: DisposeAsync безопасен")]
    public async Task PriceStreamAsyncDisposeTest()
    {
        var stream = new PolymarketPriceStream();
        await stream.DisposeAsync();
        await stream.DisposeAsync(); // double dispose
    }

    [TestCase(TestName = "PriceStream: ClearCache очищает кэш")]
    public void PriceStreamClearCacheTest()
    {
        using var stream = new PolymarketPriceStream();
        stream.ClearCache(); // Должен работать даже на пустом кэше
        Assert.That(stream.TokenCount, Is.EqualTo(0));
    }

    [TestCase(TestName = "PriceStream: методы после Dispose кидают ObjectDisposedException")]
    public void PriceStreamMethodsAfterDisposeTest()
    {
        var stream = new PolymarketPriceStream();
        stream.Dispose();

        Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await stream.SubscribeAsync(["0xCondition"]));
    }

    #endregion

    #region PolymarketPriceSnapshot

    [TestCase(TestName = "PriceSnapshot: все поля инициализируются")]
    public void PriceSnapshotAllFieldsTest()
    {
        var snapshot = new PolymarketPriceSnapshot
        {
            AssetId = "asset-1",
            Market = "market-1",
            BestBid = "0.50",
            BestAsk = "0.51",
            LastTradePrice = "0.505",
            Midpoint = "0.505",
            TickSize = "0.01",
            LastUpdateTicks = 12345
        };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot.AssetId, Is.EqualTo("asset-1"));
            Assert.That(snapshot.Market, Is.EqualTo("market-1"));
            Assert.That(snapshot.BestBid, Is.EqualTo("0.50"));
            Assert.That(snapshot.BestAsk, Is.EqualTo("0.51"));
            Assert.That(snapshot.LastTradePrice, Is.EqualTo("0.505"));
            Assert.That(snapshot.Midpoint, Is.EqualTo("0.505"));
            Assert.That(snapshot.TickSize, Is.EqualTo("0.01"));
        }
    }

    [TestCase(TestName = "PriceUpdatedEventArgs: содержит снимок")]
    public void PriceUpdatedEventArgsTest()
    {
        var snapshot = new PolymarketPriceSnapshot { AssetId = "a1" };
        var args = new PolymarketPriceUpdatedEventArgs(snapshot);

        Assert.That(args.Snapshot, Is.SameAs(snapshot));
    }

    #endregion

    #region RestClient + Middleware интеграция

    [TestCase(TestName = "RestClient: Middleware свойство по умолчанию пустое")]
    public void RestClientMiddlewareEmptyTest()
    {
        using var client = new PolymarketRestClient();
        Assert.That(client.Middleware, Is.Empty);
    }

    [TestCase(TestName = "RestClient: добавление middleware")]
    public void RestClientAddMiddlewareTest()
    {
        using var client = new PolymarketRestClient();
        client.Middleware.Add(new PolymarketLoggingMiddleware());
        client.Middleware.Add(new PolymarketMetricsMiddleware());

        Assert.That(client.Middleware, Has.Count.EqualTo(2));
    }

    #endregion

    #region Вспомогательные методы

    private static PolymarketResponseContext MakeResponse(int statusCode, Exception? exception = null) => new()
    {
        Request = new PolymarketRequestContext { Method = "GET", Path = "/", Url = "/" },
        StatusCode = statusCode,
        ElapsedMs = 10,
        Exception = exception
    };

    #endregion
}
