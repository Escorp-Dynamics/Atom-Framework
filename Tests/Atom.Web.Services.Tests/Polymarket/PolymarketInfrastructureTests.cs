using System.Net;
using System.Numerics;
using System.Text;

namespace Atom.Web.Services.Polymarket.Tests;

/// <summary>
/// Тесты Rate Limiter, Retry Policy, Keccak-256 и EIP-712 подписи ордеров.
/// </summary>
public class PolymarketInfrastructureTests(ILogger logger) : BenchmarkTests<PolymarketInfrastructureTests>(logger)
{
    public PolymarketInfrastructureTests() : this(ConsoleLogger.Unicode) { }

    #region Keccak-256

    [TestCase(TestName = "Keccak-256: пустая строка")]
    public void Keccak256EmptyStringTest()
    {
        // Известный Keccak-256 хеш пустой строки
        // keccak256("") = c5d2460186f7233c927e7db2dcc703c0e500b653ca82273b7bfad8045d85a470
        var hash = Keccak256.Hash([]);
        var hex = Convert.ToHexStringLower(hash);

        Assert.That(hex, Is.EqualTo("c5d2460186f7233c927e7db2dcc703c0e500b653ca82273b7bfad8045d85a470"));
    }

    [TestCase(TestName = "Keccak-256: 'abc'")]
    public void Keccak256AbcTest()
    {
        // keccak256("abc") = 4e03657aea45a94fc7d47ba826c8d667c0d1e6e33a64a036ec44f58fa12d6c45
        var hash = Keccak256.Hash(Encoding.UTF8.GetBytes("abc"));
        var hex = Convert.ToHexStringLower(hash);

        Assert.That(hex, Is.EqualTo("4e03657aea45a94fc7d47ba826c8d667c0d1e6e33a64a036ec44f58fa12d6c45"));
    }

    [TestCase(TestName = "Keccak-256: 'hello world'")]
    public void Keccak256HelloWorldTest()
    {
        // keccak256("hello world") = 47173285a8d7341e5e972fc677286384f802f8ef42a5ec5f03bbfa254cb01fad
        var hash = Keccak256.Hash(Encoding.UTF8.GetBytes("hello world"));
        var hex = Convert.ToHexStringLower(hash);

        Assert.That(hex, Is.EqualTo("47173285a8d7341e5e972fc677286384f802f8ef42a5ec5f03bbfa254cb01fad"));
    }

    [TestCase(TestName = "Keccak-256: длинные данные (>136 байт, мульти-блок)")]
    public void Keccak256MultiBlockTest()
    {
        // Данные длиной 200 байт (больше rate=136) — тестирует мульти-блочную обработку
        var data = new byte[200];
        for (var i = 0; i < data.Length; i++)
            data[i] = (byte)(i & 0xFF);

        var hash = Keccak256.Hash(data);

        Assert.That(hash, Has.Length.EqualTo(32));

        // Детерминированность
        var hash2 = Keccak256.Hash(data);
        Assert.That(hash, Is.EqualTo(hash2));
    }

    [TestCase(TestName = "Keccak-256: длина ровно 136 байт (один полный блок)")]
    public void Keccak256ExactBlockTest()
    {
        var data = new byte[136];
        data.AsSpan().Fill(0xAB);

        var hash = Keccak256.Hash(data);
        Assert.That(hash, Has.Length.EqualTo(32));
    }

    [TestCase(TestName = "Keccak-256: один байт")]
    public void Keccak256SingleByteTest()
    {
        // keccak256(0x00) — хеш одного нулевого байта
        var hash = Keccak256.Hash([0x00]);
        Assert.That(hash, Has.Length.EqualTo(32));

        // Отличается от пустой строки
        var emptyHash = Keccak256.Hash([]);
        Assert.That(hash, Is.Not.EqualTo(emptyHash));
    }

    [TestCase(TestName = "Keccak-256: запись в буфер")]
    public void Keccak256HashToBufferTest()
    {
        var data = Encoding.UTF8.GetBytes("test");
        var buffer = new byte[32];

        Keccak256.Hash(data, buffer);
        var allocatedHash = Keccak256.Hash(data);

        Assert.That(buffer, Is.EqualTo(allocatedHash));
    }

    [TestCase(TestName = "Keccak-256: маленький буфер вызывает исключение")]
    public void Keccak256SmallBufferThrowsTest()
    {
        var data = Encoding.UTF8.GetBytes("test");
        var buffer = new byte[16]; // меньше 32

        Assert.Throws<ArgumentOutOfRangeException>(() => Keccak256.Hash(data, buffer));
    }

    [TestCase(TestName = "Keccak-256: EIP-712 domain type hash")]
    public void Keccak256Eip712DomainTypeHashTest()
    {
        // Проверка типового хеша EIP712Domain
        var typeString = "EIP712Domain(string name,string version,uint256 chainId,address verifyingContract)";
        var hash = Keccak256.Hash(Encoding.UTF8.GetBytes(typeString));

        Assert.That(hash, Has.Length.EqualTo(32));
        // Известное значение
        var hex = Convert.ToHexStringLower(hash);
        Assert.That(hex, Is.EqualTo("8b73c3c69bb8fe3d512ecc4cf759cc79239f7b179b0ffacaa9a75d522b39400f"));
    }

    #endregion

    #region PolymarketRateLimiter

    [TestCase(TestName = "RateLimiter: конструктор по умолчанию")]
    public void RateLimiterDefaultConstructorTest()
    {
        var limiter = new PolymarketRateLimiter();

        Assert.That(limiter.MaxTokens, Is.EqualTo(100));
        Assert.That(limiter.AvailableTokens, Is.EqualTo(100).Within(1));
    }

    [TestCase(TestName = "RateLimiter: кастомные параметры")]
    public void RateLimiterCustomParamsTest()
    {
        var limiter = new PolymarketRateLimiter(maxTokens: 50, refillPeriodSeconds: 5);

        Assert.That(limiter.MaxTokens, Is.EqualTo(50));
    }

    [TestCase(TestName = "RateLimiter: невалидные параметры вызывают исключение")]
    public void RateLimiterInvalidParamsThrowsTest()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PolymarketRateLimiter(maxTokens: 0, refillPeriodSeconds: 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PolymarketRateLimiter(maxTokens: -1, refillPeriodSeconds: 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PolymarketRateLimiter(maxTokens: 10, refillPeriodSeconds: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PolymarketRateLimiter(maxTokens: 10, refillPeriodSeconds: -1));
        }
    }

    [TestCase(TestName = "RateLimiter: TryAcquire потребляет токен")]
    public void RateLimiterTryAcquireTest()
    {
        var limiter = new PolymarketRateLimiter(maxTokens: 3, refillPeriodSeconds: 100);

        Assert.That(limiter.TryAcquire(), Is.True);
        Assert.That(limiter.TryAcquire(), Is.True);
        Assert.That(limiter.TryAcquire(), Is.True);
        // Все токены израсходованы
        Assert.That(limiter.TryAcquire(), Is.False);
    }

    [TestCase(TestName = "RateLimiter: WaitAsync завершается при наличии токенов")]
    public async Task RateLimiterWaitAsyncTest()
    {
        var limiter = new PolymarketRateLimiter(maxTokens: 5, refillPeriodSeconds: 10);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await limiter.WaitAsync(cts.Token);

        // Должен потребить один токен
        Assert.That(limiter.AvailableTokens, Is.LessThan(5));
    }

    [TestCase(TestName = "RateLimiter: WaitAsync отменяется через CancellationToken")]
    public void RateLimiterWaitAsyncCancellationTest()
    {
        var limiter = new PolymarketRateLimiter(maxTokens: 1, refillPeriodSeconds: 1000);
        limiter.TryAcquire(); // Израсходовать единственный токен

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(
            async () => await limiter.WaitAsync(cts.Token));
    }

    #endregion

    #region PolymarketRetryPolicy

    [TestCase(TestName = "RetryPolicy: конструктор по умолчанию")]
    public void RetryPolicyDefaultConstructorTest()
    {
        var policy = new PolymarketRetryPolicy();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(policy.MaxRetries, Is.EqualTo(3));
            Assert.That(policy.InitialDelay, Is.EqualTo(TimeSpan.FromMilliseconds(500)));
            Assert.That(policy.MaxDelay, Is.EqualTo(TimeSpan.FromSeconds(30)));
        }
    }

    [TestCase(TestName = "RetryPolicy: кастомные параметры")]
    public void RetryPolicyCustomParamsTest()
    {
        var policy = new PolymarketRetryPolicy(5, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(policy.MaxRetries, Is.EqualTo(5));
            Assert.That(policy.InitialDelay, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(policy.MaxDelay, Is.EqualTo(TimeSpan.FromMinutes(1)));
        }
    }

    [TestCase(TestName = "RetryPolicy: невалидные параметры")]
    public void RetryPolicyInvalidParamsThrowsTest()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new PolymarketRetryPolicy(-1, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10)));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new PolymarketRetryPolicy(3, TimeSpan.Zero, TimeSpan.FromSeconds(10)));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new PolymarketRetryPolicy(3, TimeSpan.FromSeconds(1), TimeSpan.Zero));
        }
    }

    [TestCase(TestName = "RetryPolicy: успешная операция без повторов")]
    public async Task RetryPolicySuccessNoRetryTest()
    {
        var policy = new PolymarketRetryPolicy();
        var callCount = 0;

        var result = await policy.ExecuteAsync<string>(ct =>
        {
            callCount++;
            return new ValueTask<string>("success");
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo("success"));
            Assert.That(callCount, Is.EqualTo(1));
        }
    }

    [TestCase(TestName = "RetryPolicy: повтор при 5xx ошибке")]
    public async Task RetryPolicyRetryOn5xxTest()
    {
        var policy = new PolymarketRetryPolicy(2, TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1));
        var callCount = 0;

        var result = await policy.ExecuteAsync<string>(ct =>
        {
            callCount++;
            if (callCount < 2)
                throw new HttpRequestException("server error", null, HttpStatusCode.InternalServerError);
            return new ValueTask<string>("recovered");
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo("recovered"));
            Assert.That(callCount, Is.EqualTo(2));
        }
    }

    [TestCase(TestName = "RetryPolicy: исчерпание попыток кидает исключение")]
    public void RetryPolicyExhaustedThrowsTest()
    {
        var policy = new PolymarketRetryPolicy(2, TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1));

        Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await policy.ExecuteAsync<string>(ct =>
                throw new HttpRequestException("always fails", null, HttpStatusCode.ServiceUnavailable));
        });
    }

    [TestCase(TestName = "RetryPolicy: 400 ошибка не ретрается")]
    public void RetryPolicyNoRetryOn400Test()
    {
        var policy = new PolymarketRetryPolicy(3, TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1));
        var callCount = 0;

        Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await policy.ExecuteAsync<string>(ct =>
            {
                callCount++;
                throw new HttpRequestException("bad request", null, HttpStatusCode.BadRequest);
            });
        });

        Assert.That(callCount, Is.EqualTo(1));
    }

    [TestCase(TestName = "RetryPolicy: 429 ретрается")]
    public async Task RetryPolicyRetryOn429Test()
    {
        var policy = new PolymarketRetryPolicy(2, TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1));
        var callCount = 0;

        var result = await policy.ExecuteAsync<string>(ct =>
        {
            callCount++;
            if (callCount < 2)
                throw new HttpRequestException("rate limited", null, HttpStatusCode.TooManyRequests);
            return new ValueTask<string>("ok");
        });

        Assert.That(result, Is.EqualTo("ok"));
    }

    [TestCase(TestName = "RetryPolicy: IsRetryable — правильные статусы")]
    public void RetryPolicyIsRetryableTest()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(PolymarketRetryPolicy.IsRetryable(new HttpRequestException("", null, HttpStatusCode.TooManyRequests)), Is.True);
            Assert.That(PolymarketRetryPolicy.IsRetryable(new HttpRequestException("", null, HttpStatusCode.InternalServerError)), Is.True);
            Assert.That(PolymarketRetryPolicy.IsRetryable(new HttpRequestException("", null, HttpStatusCode.BadGateway)), Is.True);
            Assert.That(PolymarketRetryPolicy.IsRetryable(new HttpRequestException("", null, HttpStatusCode.ServiceUnavailable)), Is.True);
            Assert.That(PolymarketRetryPolicy.IsRetryable(new HttpRequestException("", null, HttpStatusCode.GatewayTimeout)), Is.True);
            Assert.That(PolymarketRetryPolicy.IsRetryable(new HttpRequestException("network error")), Is.True); // null status = network
            Assert.That(PolymarketRetryPolicy.IsRetryable(new HttpRequestException("", null, HttpStatusCode.BadRequest)), Is.False);
            Assert.That(PolymarketRetryPolicy.IsRetryable(new HttpRequestException("", null, HttpStatusCode.Unauthorized)), Is.False);
            Assert.That(PolymarketRetryPolicy.IsRetryable(new HttpRequestException("", null, HttpStatusCode.Forbidden)), Is.False);
        }
    }

    [TestCase(TestName = "RetryPolicy: null operation кидает исключение")]
    public void RetryPolicyNullOperationThrowsTest()
    {
        var policy = new PolymarketRetryPolicy();
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await policy.ExecuteAsync<string>(null!));
    }

    #endregion

    #region PolymarketRestClient — RateLimiter и RetryPolicy

    [TestCase(TestName = "RestClient: RateLimiter установка и чтение")]
    public void RestClientRateLimiterPropertyTest()
    {
        using var client = new PolymarketRestClient();
        Assert.That(client.RateLimiter, Is.Null);

        var limiter = new PolymarketRateLimiter();
        client.RateLimiter = limiter;
        Assert.That(client.RateLimiter, Is.SameAs(limiter));
    }

    [TestCase(TestName = "RestClient: RetryPolicy установка и чтение")]
    public void RestClientRetryPolicyPropertyTest()
    {
        using var client = new PolymarketRestClient();
        Assert.That(client.RetryPolicy, Is.Null);

        var policy = new PolymarketRetryPolicy();
        client.RetryPolicy = policy;
        Assert.That(client.RetryPolicy, Is.SameAs(policy));
    }

    #endregion

    #region EIP-712 — ComputeDomainSeparator и ComputeOrderStructHash

    [TestCase(TestName = "EIP-712: domain separator детерминирован")]
    public void Eip712DomainSeparatorDeterministicTest()
    {
        var ds1 = PolymarketOrderSigner.ComputeDomainSeparator("Exchange", "1", 137, "0x4bFb41d5B3570DeFd03C39a9A4D8dE6Bd8B8982E");
        var ds2 = PolymarketOrderSigner.ComputeDomainSeparator("Exchange", "1", 137, "0x4bFb41d5B3570DeFd03C39a9A4D8dE6Bd8B8982E");

        Assert.That(ds1, Has.Length.EqualTo(32));
        Assert.That(ds1, Is.EqualTo(ds2));
    }

    [TestCase(TestName = "EIP-712: разные chain ID дают разный separator")]
    public void Eip712DifferentChainIdTest()
    {
        var ds137 = PolymarketOrderSigner.ComputeDomainSeparator("Exchange", "1", 137, "0x4bFb41d5B3570DeFd03C39a9A4D8dE6Bd8B8982E");
        var ds1 = PolymarketOrderSigner.ComputeDomainSeparator("Exchange", "1", 1, "0x4bFb41d5B3570DeFd03C39a9A4D8dE6Bd8B8982E");

        Assert.That(ds137, Is.Not.EqualTo(ds1));
    }

    [TestCase(TestName = "EIP-712: разные контракты дают разный separator")]
    public void Eip712DifferentContractTest()
    {
        var ds1 = PolymarketOrderSigner.ComputeDomainSeparator("Exchange", "1", 137, "0x4bFb41d5B3570DeFd03C39a9A4D8dE6Bd8B8982E");
        var ds2 = PolymarketOrderSigner.ComputeDomainSeparator("Exchange", "1", 137, "0xC5d563A36AE78145C45a50134d48A1215220f80a");

        Assert.That(ds1, Is.Not.EqualTo(ds2));
    }

    [TestCase(TestName = "EIP-712: struct hash ордера детерминирован")]
    public void Eip712OrderStructHashDeterministicTest()
    {
        var order = CreateTestOrder();

        var hash1 = PolymarketOrderSigner.ComputeOrderStructHash(order);
        var hash2 = PolymarketOrderSigner.ComputeOrderStructHash(order);

        Assert.That(hash1, Has.Length.EqualTo(32));
        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [TestCase(TestName = "EIP-712: разные ордера дают разный struct hash")]
    public void Eip712DifferentOrdersTest()
    {
        var order1 = CreateTestOrder();
        var order2 = CreateTestOrder();
        order2.Salt = "99999";

        var hash1 = PolymarketOrderSigner.ComputeOrderStructHash(order1);
        var hash2 = PolymarketOrderSigner.ComputeOrderStructHash(order2);

        Assert.That(hash1, Is.Not.EqualTo(hash2));
    }

    [TestCase(TestName = "EIP-712: digest вычисляется корректно")]
    public void Eip712DigestTest()
    {
        var domainSep = new byte[32];
        domainSep.AsSpan().Fill(0xAA);
        var structHash = new byte[32];
        structHash.AsSpan().Fill(0xBB);

        var digest = PolymarketOrderSigner.ComputeEip712Digest(domainSep, structHash);

        Assert.That(digest, Has.Length.EqualTo(32));

        // Проверка: digest = keccak256("\x19\x01" + domainSep + structHash)
        var message = new byte[66];
        message[0] = 0x19;
        message[1] = 0x01;
        Buffer.BlockCopy(domainSep, 0, message, 2, 32);
        Buffer.BlockCopy(structHash, 0, message, 34, 32);
        var expected = Keccak256.Hash(message);

        Assert.That(digest, Is.EqualTo(expected));
    }

    [TestCase(TestName = "EIP-712: ComputeOrderDigest возвращает 32 байта")]
    public void Eip712ComputeOrderDigestTest()
    {
        var order = CreateTestOrder();

        var digest = PolymarketOrderSigner.ComputeOrderDigest(order);
        Assert.That(digest, Has.Length.EqualTo(32));

        // neg-risk даёт другой digest
        var negRiskDigest = PolymarketOrderSigner.ComputeOrderDigest(order, negRisk: true);
        Assert.That(digest, Is.Not.EqualTo(negRiskDigest));
    }

    [TestCase(TestName = "EIP-712: BUY и SELL дают разные хеши")]
    public void Eip712BuySellDifferentHashTest()
    {
        var buyOrder = CreateTestOrder();
        buyOrder.Side = PolymarketSide.Buy;

        var sellOrder = CreateTestOrder();
        sellOrder.Side = PolymarketSide.Sell;

        var buyHash = PolymarketOrderSigner.ComputeOrderStructHash(buyOrder);
        var sellHash = PolymarketOrderSigner.ComputeOrderStructHash(sellOrder);

        Assert.That(buyHash, Is.Not.EqualTo(sellHash));
    }

    #endregion

    #region Вспомогательные методы

    private static PolymarketSignedOrder CreateTestOrder() => new()
    {
        Salt = "12345",
        Maker = "0x4bFb41d5B3570DeFd03C39a9A4D8dE6Bd8B8982E",
        Signer = "0x4bFb41d5B3570DeFd03C39a9A4D8dE6Bd8B8982E",
        Taker = "0x0000000000000000000000000000000000000000",
        TokenId = "71321045679252212594626385532706912750332728571942532289631379312455583992563",
        MakerAmount = "100000000",
        TakerAmount = "50000000",
        Expiration = "0",
        Nonce = "0",
        FeeRateBps = "0",
        Side = PolymarketSide.Buy,
        SignatureType = 0,
        Signature = "0x"
    };

    #endregion
}
