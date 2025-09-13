using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Atom.Distribution;
using ILogger = BenchmarkDotNet.Loggers.ILogger;

namespace Atom.Net.Http.Tests;

public class HttpsClientHandlerTests(ILogger logger) : BenchmarkTests<HttpsClientHandlerTests>(logger)
{
    private const string EchoUrl = "https://echo.free.beeceptor.com/";
    private const string Url = "https://visa.vfsglobal.com/rus/en/fra/login";
    private const string FetchUrl = "https://lift-api.vfsglobal.com/configuration/fields/fra/rus";

    private static string? pem = string.Empty;

    public HttpsClientHandlerTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(EchoUrl)]
    public Task DotnetEchoTest(string url) => EchoTest<HttpClientHandler>(url, BrowserProfile.None, Platform.Linux, RequestContext.Navigation);

    [TestCase(Url)]
    public Task DotnetTest(string url) => Test<HttpClientHandler>(url, BrowserProfile.None, Platform.Linux, RequestContext.Navigation, HttpStatusCode.Forbidden);

    [TestCase(EchoUrl, BrowserProfile.None, Platform.Linux, RequestContext.Navigation)]
    [TestCase(EchoUrl, BrowserProfile.Edge, Platform.Linux, RequestContext.Navigation)]
    [TestCase(EchoUrl, BrowserProfile.Chrome, Platform.Linux, RequestContext.Navigation)]
    [TestCase(EchoUrl, BrowserProfile.Firefox, Platform.Linux, RequestContext.Navigation)]
    //[TestCase(EchoUrl, BrowserProfile.Safari, Platform.Linux, RequestContext.Navigation)]
    [TestCase(EchoUrl, BrowserProfile.None, Platform.Linux, RequestContext.Fetch)]
    [TestCase(EchoUrl, BrowserProfile.Edge, Platform.Linux, RequestContext.Fetch)]
    [TestCase(EchoUrl, BrowserProfile.Chrome, Platform.Linux, RequestContext.Fetch)]
    [TestCase(EchoUrl, BrowserProfile.Firefox, Platform.Linux, RequestContext.Fetch)]
    //[TestCase(EchoUrl, BrowserProfile.Safari, Platform.Linux, RequestContext.Fetch)]
    public Task BrowserEchoTest(string url, BrowserProfile profile, Platform platform, RequestContext context) => EchoTest<HttpsClientHandler>(url, profile, platform, context);

    [TestCase(Url, BrowserProfile.None, Platform.Linux, RequestContext.Navigation, HttpStatusCode.Forbidden)]
    [TestCase(Url, BrowserProfile.Edge, Platform.Linux, RequestContext.Navigation, HttpStatusCode.OK)]
    [TestCase(Url, BrowserProfile.Chrome, Platform.Linux, RequestContext.Navigation, HttpStatusCode.OK)]
    [TestCase(Url, BrowserProfile.Firefox, Platform.Linux, RequestContext.Navigation, HttpStatusCode.OK)]
    //[TestCase(Url, BrowserProfile.Safari, Platform.Linux, RequestContext.Navigation, HttpStatusCode.OK)]
    [TestCase(FetchUrl, BrowserProfile.None, Platform.Linux, RequestContext.Fetch, HttpStatusCode.Forbidden)]
    [TestCase(FetchUrl, BrowserProfile.Edge, Platform.Linux, RequestContext.Fetch, HttpStatusCode.OK)]
    [TestCase(FetchUrl, BrowserProfile.Chrome, Platform.Linux, RequestContext.Fetch, HttpStatusCode.OK)]
    [TestCase(FetchUrl, BrowserProfile.Firefox, Platform.Linux, RequestContext.Fetch, HttpStatusCode.OK)]
    //[TestCase(FetchUrl, BrowserProfile.Safari, Platform.Linux, RequestContext.Fetch, HttpStatusCode.OK)]
    public Task BrowserTest(string url, BrowserProfile profile, Platform platform, RequestContext context, HttpStatusCode successCode) => Test<HttpsClientHandler>(url, profile, platform, context, successCode);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static async Task EchoTest<T>(string url, BrowserProfile profile, Platform platform, RequestContext context) where T : HttpClientHandler, new()
    {
        using var handler = new T();

        if (handler is HttpsClientHandler https)
        {
            https.Profile = profile;
            https.Platform = platform;
            https.RequestContext = context;
        }

        using var client = new HttpClient(handler);

        using var response = await client.GetAsync(url).ConfigureAwait(false);
        Assert.That(response, Is.Not.Null);

        var result = await response.Content.AsJsonAsync(JsonContext.Default.HttpsRequestTestData);

        Assert.That(result, Is.Not.Null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Method, Is.EqualTo("GET"));
            Assert.That(result.Protocol, Is.EqualTo("https"));

            var reference = new Dictionary<string, string>()
            {
                { "Host", "echo.free.beeceptor.com" },
                { "User-Agent", HttpExtensions.GetUserAgentHeader(profile, platform) },
                { "Accept", HttpExtensions.GetAcceptHeader(profile, context) },
                { "Accept-Encoding", "gzip, deflate, br, zstd" },
                { "Accept-Language", HttpExtensions.GetAcceptLanguageHeader(profile, context) },
                { "Cache-Control", context is RequestContext.Navigation ? "max-age=0" : "no-cache" },
                { "Dnt", "1" },
                { "Priority", "u=0, i" },
            };

            if (profile is BrowserProfile.Firefox && context is RequestContext.Fetch) reference["Priority"] = "u=4";

            if (profile is BrowserProfile.Edge or BrowserProfile.Chrome)
            {
                if (context is RequestContext.Fetch) reference.Add("Pragma", "no-cache");

                reference.Add("Sec-Ch-Ua", profile is BrowserProfile.Edge ? "\"Microsoft Edge\";v=\"135\", \"Not-A.Brand\";v=\"8\", \"Chromium\";v=\"135\"" : "\"Google Chrome\";v=\"135\", \"Not-A.Brand\";v=\"8\", \"Chromium\";v=\"135\"");
                reference.Add("Sec-Ch-Ua-Mobile", "?0");
                reference.Add("Sec-Ch-Ua-Platform", "\"Linux\"");
            }

            reference.Add("Sec-Fetch-Dest", context is RequestContext.Navigation ? "document" : "empty");
            reference.Add("Sec-Fetch-Mode", context is RequestContext.Navigation ? "navigate" : "cors");

            var site = "none";
            if (context is RequestContext.Fetch) site = profile is BrowserProfile.Firefox ? "cross-site" : "same-origin";

            reference.Add("Sec-Fetch-Site", site);

            if (context is RequestContext.Navigation) reference.Add("Sec-Fetch-User", "?1");
            if (profile is BrowserProfile.Firefox) reference.Add("Te", "trailers");
            if (context is RequestContext.Navigation) reference.Add("Upgrade-Insecure-Requests", "1");

            Assert.That(result.Headers, Is.EquivalentTo(reference));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static async Task Test<T>(string url, BrowserProfile profile, Platform platform, RequestContext context, HttpStatusCode successCode) where T : HttpClientHandler, new()
    {
        using var handler = new T();

        if (handler is HttpsClientHandler https)
        {
            https.Profile = profile;
            https.Platform = platform;
            https.RequestContext = context;
        }

        using var client = new HttpClient(handler);

        using var request = new HttpRequestMessage(HttpMethod.Get, url) { Version = HttpVersion.Version30 };

        if (context is RequestContext.Fetch)
        {
            request.Headers.TryAddWithoutValidation("accept", "application/json, text/plain, */*");
            request.Headers.TryAddWithoutValidation("route", "rus/en/fra");
            request.Headers.TryAddWithoutValidation("origin", "https://visa.vfsglobal.com");
            request.Headers.TryAddWithoutValidation("referer", "https://visa.vfsglobal.com/");

            var clientSource = await EncryptAsync(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.CurrentCulture)).ConfigureAwait(false);
            request.Headers.TryAddWithoutValidation("clientSource", $"GA{clientSource}Z");
        }

        if (handler is not HttpsClientHandler) request.WithHeadersTemplate(BrowserProfile.Edge, platform, context);

        using var response = await client.SendAsync(request).ConfigureAwait(false);
        Assert.That(response, Is.Not.Null);
        Assert.That(response.StatusCode, Is.EqualTo(successCode));

        var json = await response.Content.ReadAsStringAsync();
        Assert.That(json, Is.Not.Empty);
    }

    private static async ValueTask<string> EncryptAsync(string data, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(pem))
        {
            if (!File.Exists("assets/liftApi.pem")) throw new FileNotFoundException("assets/liftApi.pem");
            pem = await File.ReadAllTextAsync("assets/liftApi.pem", Encoding.ASCII, cancellationToken).ConfigureAwait(false);
        }

        using var rsa = RSA.Create();
        rsa.ImportFromPem(pem);

        var tmp = rsa.Encrypt(Encoding.UTF8.GetBytes(data), RSAEncryptionPadding.Pkcs1);
        return Convert.ToBase64String(tmp);
    }

    private static ValueTask<string> EncryptAsync(string data) => EncryptAsync(data, CancellationToken.None);
}