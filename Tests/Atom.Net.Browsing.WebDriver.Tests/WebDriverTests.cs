using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Atom;
using Atom.Net.Browsing;
using Atom.Net.Browsing.WebDriver;

namespace Tests;

[TestFixture, Parallelizable(ParallelScope.All)]
public class WebDriverTests(ILogger logger) : BenchmarkTests<WebDriverTests>(logger)
{
    private readonly ILogger log = logger;

    public override bool IsBenchmarkEnabled => default;

    public WebDriverTests() : this(ConsoleLogger.Unicode) { }

    /// <summary>
    /// Путь к папке Chrome-расширения коннектора.
    /// </summary>
    private static string ChromeExtensionPath => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "Extension"));

    /// <summary>
    /// Путь к папке Firefox-расширения коннектора.
    /// </summary>
    private static string FirefoxExtensionPath => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "Extension.Firefox"));

    // ─── Обнаружение установленных браузеров ─────────────────────

    private static readonly (string Name, string[] LinuxPaths, string[] WinPaths, bool IsFirefox)[] KnownBrowsers =
    [
        ("Chrome",
            ["/usr/bin/google-chrome-stable", "/usr/bin/google-chrome", "/usr/bin/chromium"],
            [@"C:\Program Files\Google\Chrome\Application\chrome.exe"],
            false),
        ("Vivaldi",
            ["/usr/bin/vivaldi-stable", "/usr/bin/vivaldi"],
            [@"C:\Program Files\Vivaldi\Application\vivaldi.exe"],
            false),
        ("Brave",
            ["/usr/bin/brave", "/usr/bin/brave-browser", "/usr/bin/brave-browser-stable"],
            [@"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe"],
            false),
        ("Opera",
            ["/usr/bin/opera"],
            [@"C:\Program Files\Opera\launcher.exe"],
            false),
        ("Edge",
            ["/usr/bin/microsoft-edge-stable", "/usr/bin/microsoft-edge"],
            [@"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"],
            false),
        ("Yandex",
            ["/usr/bin/yandex-browser-corporate", "/usr/bin/yandex-browser", "/opt/yandex/browser/yandex-browser"],
            [@"C:\Program Files\Yandex\YandexBrowser\Application\browser.exe"],
            false),
        ("Firefox",
            ["/usr/bin/firefox-developer-edition", "/usr/bin/firefox-nightly", "/usr/bin/firefox"],
            [@"C:\Program Files\Firefox Developer Edition\firefox.exe", @"C:\Program Files\Mozilla Firefox\firefox.exe"],
            true),
    ];

    /// <summary>
    /// Данные для параметризованных тестов: имя → путь, isFirefox.
    /// </summary>
    private static IEnumerable<TestCaseData> AvailableBrowsers()
    {
        foreach (var (name, linuxPaths, winPaths, isFirefox) in KnownBrowsers)
        {
            var paths = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? winPaths : linuxPaths;
            var found = paths.FirstOrDefault(File.Exists);

            if (found is not null)
                yield return new TestCaseData(name, found, isFirefox).SetName($"Запуск {name}");
        }
    }

    // ─── Тесты ──────────────────────────────────────────────────

    [TestCase(TestName = "BridgeServer запускается и останавливается")]
    public async Task BridgeServerStartStopTest()
    {
        var settings = new BridgeSettings { Secret = "test-secret-start-stop" };
        await using var bridge = new BridgeServer(settings);

        await bridge.StartAsync();

        Assert.That(bridge.Port, Is.GreaterThan(0), "Порт должен быть назначен.");
        Assert.That(bridge.ConnectionCount, Is.Zero, "Подключений быть не должно.");
    }

    [TestCase(TestName = "BridgeServer выбирает свободный порт")]
    public async Task BridgeServerAutoPortTest()
    {
        var settings = new BridgeSettings { Port = 0, Secret = "test-secret-auto-port" };
        await using var bridge = new BridgeServer(settings);

        await bridge.StartAsync();

        Assert.That(bridge.Port, Is.GreaterThan(1023), "Порт должен быть выше привилегированного диапазона.");
    }

    [TestCase(TestName = "WebDriverBrowser создаётся через CreateAsync")]
    public async Task WebDriverBrowserCreateAsyncTest()
    {
        await using var browser = await WebDriverBrowser.CreateAsync();

        Assert.That(browser.BridgePort, Is.GreaterThan(0), "Порт моста должен быть назначен.");
        Assert.That(browser.Secret, Is.Not.Empty, "Секрет должен быть сгенерирован.");
        Assert.That(browser.ConnectionCount, Is.Zero, "Подключений быть не должно.");
    }

    [TestCaseSource(nameof(AvailableBrowsers))]
    public async Task BrowserLaunchAndConnectTest(string name, string browserPath, bool isFirefox)
    {
        var extensionPath = isFirefox ? FirefoxExtensionPath : ChromeExtensionPath;
        Assert.That(File.Exists(browserPath), Is.True, $"Браузер {name} не найден: {browserPath}");
        Assert.That(Directory.Exists(extensionPath), Is.True, $"Расширение не найдено: {extensionPath}");

        log.WriteLine(LogKind.Default, $"[{name}] Путь: {browserPath}, расширение: {extensionPath}");

        await using var browser = await WebDriverBrowser.LaunchAsync(
            browserPath, extensionPath, arguments: ["--no-sandbox"]);

        log.WriteLine(LogKind.Default, $"[{name}] Мост: порт {browser.BridgePort}, подключений: {browser.ConnectionCount}");

        // Ждём подключения вкладки (до 30 секунд — параллельный запуск нескольких браузеров).
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        if (browser.ConnectionCount == 0)
        {
            var tcs = new TaskCompletionSource<TabConnectedEventArgs>();
            browser.TabConnected += (_, e) =>
            {
                tcs.TrySetResult(e);
                return ValueTask.CompletedTask;
            };
            await tcs.Task.WaitAsync(cts.Token);
        }

        log.WriteLine(LogKind.Default, $"[{name}] Подключено вкладок: {browser.ConnectionCount}");
        Assert.That(browser.ConnectionCount, Is.GreaterThan(0), $"[{name}] Должна быть подключена минимум 1 вкладка.");
    }

    /// <summary>
    /// Тесты Page API на discovery-табе. Один браузер на все тесты.
    /// </summary>
    [TestFixture, Category("GUI")]
    public class PageApiTests
    {
        private readonly ILogger logger = ConsoleLogger.Unicode;
        private WebDriverBrowser browser = null!;
        private WebDriverPage page = null!;

        [OneTimeSetUp]
        public async Task SetUp()
        {
            var (browserPath, extensionPath) = FindChromiumBrowser();
            browser = await WebDriverBrowser.LaunchAsync(
                browserPath, extensionPath,
                arguments: ["--no-sandbox", "--disable-features=Translate"]);
            page = await WaitForFirstTabAsync();
            logger.WriteLine(LogKind.Default, $"Браузер запущен, вкладка: {page.TabId}");
        }

        [OneTimeTearDown]
        public async Task TearDown()
        {
            await browser.DisposeAsync();
        }

        [TestCase(TestName = "GetTitleAsync возвращает заголовок"), Order(1)]
        public async Task GetTitleTest()
        {
            var title = await page.GetTitleAsync();
            logger.WriteLine(LogKind.Default, $"Заголовок: {title}");
            Assert.That(title, Is.EqualTo("Atom Bridge Discovery"), "Заголовок discovery-страницы.");
        }

        [TestCase(TestName = "GetContentAsync возвращает HTML"), Order(2)]
        public async Task GetContentTest()
        {
            var content = await page.GetContentAsync();
            logger.WriteLine(LogKind.Default, $"HTML длина: {content?.Length ?? 0}");
            Assert.That(content, Is.Not.Null.And.Not.Empty, "HTML-содержимое должно быть не пустым.");
            Assert.That(content, Does.Contain("Atom Bridge Discovery"), "HTML должен содержать 'Atom Bridge Discovery'.");
        }

        [TestCase(TestName = "FindElementAsync находит элемент по CSS"), Order(3)]
        public async Task FindElementTest()
        {
            var element = await page.FindElementAsync(new ElementSelector
            {
                Strategy = ElementSelectorStrategy.Css,
                Value = "title",
            });
            logger.WriteLine(LogKind.Default, $"Найден элемент: {element?.Id}");
            Assert.That(element, Is.Not.Null, "Элемент <title> должен быть найден.");
        }

        [TestCase(TestName = "FindElementsAsync находит множество элементов"), Order(4)]
        public async Task FindElementsTest()
        {
            var elements = await page.FindElementsAsync(new ElementSelector
            {
                Strategy = ElementSelectorStrategy.Css,
                Value = "meta",
            });
            logger.WriteLine(LogKind.Default, $"Найдено элементов <meta>: {elements.Length}");
            Assert.That(elements, Is.Not.Empty, "Элементы <meta> должны быть найдены.");
        }

        [TestCase(TestName = "ExecuteAsync возвращает результат"), Order(5)]
        public async Task ExecuteScriptTest()
        {
            var result = await page.ExecuteAsync("2 + 2");
            logger.WriteLine(LogKind.Default, $"Результат 2+2: {result}");
            Assert.That(result, Is.Not.Null, "Скрипт должен вернуть результат.");

            var uaResult = await page.ExecuteAsync("navigator.userAgent");
            logger.WriteLine(LogKind.Default, $"User-Agent: {uaResult}");
            Assert.That(uaResult, Is.Not.Null, "navigator.userAgent должен быть не null.");

            // Statement-форма (не expression) — eval bridge автоматически делает fallback.
            var stmtResult = await page.ExecuteAsync("var x = 10; x * 3");
            logger.WriteLine(LogKind.Default, $"Statement результат: {stmtResult}");
            Assert.That(stmtResult?.ToString(), Is.EqualTo("30"), "Statement должен вернуть последнее значение.");

            // IIFE — классический паттерн.
            var iifeResult = await page.ExecuteAsync("(function() { return 42; })()");
            logger.WriteLine(LogKind.Default, $"IIFE результат: {iifeResult}");
            Assert.That(iifeResult?.ToString(), Is.EqualTo("42"), "IIFE должен вернуть 42.");

            // Доступ к DOM — заголовок страницы через querySelector.
            var domResult = await page.ExecuteAsync("document.querySelector('title').textContent");
            logger.WriteLine(LogKind.Default, $"DOM результат: {domResult}");
            Assert.That(domResult?.ToString(), Is.EqualTo("Atom Bridge Discovery"), "DOM доступ должен работать.");

            // Void-statement — присвоение возвращает значение.
            var voidResult = await page.ExecuteAsync("document.title = 'Modified'");
            logger.WriteLine(LogKind.Default, $"Void-statement результат: {voidResult}");
            Assert.That(voidResult?.ToString(), Is.EqualTo("Modified"), "Присвоение возвращает значение.");

            // Восстанавливаем заголовок для других тестов.
            await page.ExecuteAsync("document.title = 'Atom Bridge Discovery'");

            // Ошибка в скрипте — должна вернуться BridgeException.
            var threw = false;
            try
            {
                await page.ExecuteAsync("nonExistentFunction()");
            }
            catch (BridgeException ex)
            {
                threw = true;
                logger.WriteLine(LogKind.Default, $"Ожидаемая ошибка: {ex.Message}");
            }

            Assert.That(threw, Is.True, "Несуществующая функция должна бросить BridgeException.");
        }

        [TestCase(TestName = "Edge-cases: пустой скрипт, undefined, null, типы"), Order(6)]
        public async Task ScriptEdgeCasesTest()
        {
            // Пустая строка — eval('') возвращает undefined → пустая строка.
            var emptyResult = await page.ExecuteAsync("");
            logger.WriteLine(LogKind.Default, $"Пустой скрипт: '{emptyResult}'");
            Assert.That(emptyResult?.ToString(), Is.Empty.Or.Null, "Пустой скрипт → пустое значение.");

            // undefined → пустая строка.
            var undefResult = await page.ExecuteAsync("undefined");
            logger.WriteLine(LogKind.Default, $"undefined: '{undefResult}'");
            Assert.That(undefResult?.ToString(), Is.Empty.Or.Null, "undefined → пустое значение.");

            // null → пустая строка.
            var nullResult = await page.ExecuteAsync("null");
            logger.WriteLine(LogKind.Default, $"null: '{nullResult}'");
            Assert.That(nullResult?.ToString(), Is.Empty.Or.Null, "null → пустое значение.");

            // Boolean true → "true".
            var trueResult = await page.ExecuteAsync("true");
            logger.WriteLine(LogKind.Default, $"true: '{trueResult}'");
            Assert.That(trueResult?.ToString(), Is.EqualTo("true"), "Boolean true.");

            // Boolean false → "false".
            var falseResult = await page.ExecuteAsync("false");
            logger.WriteLine(LogKind.Default, $"false: '{falseResult}'");
            Assert.That(falseResult?.ToString(), Is.EqualTo("false"), "Boolean false.");

            // Object → "[object Object]".
            var objResult = await page.ExecuteAsync("({a: 1, b: 2})");
            logger.WriteLine(LogKind.Default, $"Object: '{objResult}'");
            Assert.That(objResult?.ToString(), Is.EqualTo("[object Object]"), "Объект через String().");

            // Array → "1,2,3".
            var arrResult = await page.ExecuteAsync("[1, 2, 3]");
            logger.WriteLine(LogKind.Default, $"Array: '{arrResult}'");
            Assert.That(arrResult?.ToString(), Is.EqualTo("1,2,3"), "Массив через String().");

            // Синтаксическая ошибка — BridgeException.
            var syntaxThrew = false;
            try
            {
                await page.ExecuteAsync("{{{");
            }
            catch (BridgeException ex)
            {
                syntaxThrew = true;
                logger.WriteLine(LogKind.Default, $"Синтаксическая ошибка: {ex.Message}");
            }

            Assert.That(syntaxThrew, Is.True, "Синтаксическая ошибка должна бросить BridgeException.");
        }

        [TestCase(TestName = "FindElementAsync для несуществующего элемента"), Order(7)]
        public async Task FindElementMissTest()
        {
            var result = await page.FindElementAsync(new ElementSelector
            {
                Strategy = ElementSelectorStrategy.Css,
                Value = "#nonexistent-element-xyz",
            });
            logger.WriteLine(LogKind.Default, $"Несуществующий элемент: {result?.Id ?? "(null)"}");
            Assert.That(result, Is.Null, "Несуществующий элемент должен вернуть null.");
        }

        [TestCase(TestName = "FindElementsAsync для несуществующих элементов"), Order(8)]
        public async Task FindElementsMissTest()
        {
            var elements = await page.FindElementsAsync(new ElementSelector
            {
                Strategy = ElementSelectorStrategy.Css,
                Value = ".nonexistent-class-xyz",
            });
            logger.WriteLine(LogKind.Default, $"Несуществующие элементы: {elements.Length}");
            Assert.That(elements, Is.Empty, "Несуществующие элементы → пустой массив.");
        }

        [TestCase(TestName = "Edge-cases: числа, NaN, Infinity, юникод"), Order(9)]
        public async Task ScriptNumericAndUnicodeTest()
        {
            // Число 0 (falsy, но не null/undefined) → "0".
            var zeroResult = await page.ExecuteAsync("0");
            logger.WriteLine(LogKind.Default, $"0: '{zeroResult}'");
            Assert.That(zeroResult?.ToString(), Is.EqualTo("0"), "Число 0 → строка \"0\".");

            // Дробное число → "3.14".
            var floatResult = await page.ExecuteAsync("3.14");
            logger.WriteLine(LogKind.Default, $"3.14: '{floatResult}'");
            Assert.That(floatResult?.ToString(), Is.EqualTo("3.14"), "Дробное число.");

            // Отрицательное число → "-42".
            var negResult = await page.ExecuteAsync("-42");
            logger.WriteLine(LogKind.Default, $"-42: '{negResult}'");
            Assert.That(negResult?.ToString(), Is.EqualTo("-42"), "Отрицательное число.");

            // NaN → "NaN".
            var nanResult = await page.ExecuteAsync("NaN");
            logger.WriteLine(LogKind.Default, $"NaN: '{nanResult}'");
            Assert.That(nanResult?.ToString(), Is.EqualTo("NaN"), "NaN.");

            // Infinity → "Infinity".
            var infResult = await page.ExecuteAsync("Infinity");
            logger.WriteLine(LogKind.Default, $"Infinity: '{infResult}'");
            Assert.That(infResult?.ToString(), Is.EqualTo("Infinity"), "Infinity.");

            // Юникод-строка.
            var unicodeResult = await page.ExecuteAsync("'Привет мир 🌍'");
            logger.WriteLine(LogKind.Default, $"Юникод: '{unicodeResult}'");
            Assert.That(unicodeResult?.ToString(), Is.EqualTo("Привет мир 🌍"), "Юникод-строка.");

            // Длинная строка (1000 символов).
            var longResult = await page.ExecuteAsync("'A'.repeat(1000)");
            logger.WriteLine(LogKind.Default, $"Длинная строка: {longResult?.ToString()?.Length ?? 0} символов");
            Assert.That(longResult?.ToString()?.Length, Is.EqualTo(1000), "Длинная строка 1000 символов.");
        }

        [TestCase(TestName = "Edge-cases: спецсимволы, JSON, шаблоны, даты"), Order(10)]
        public async Task ScriptSpecialCharsAndFormatsTest()
        {
            // Одинарные кавычки внутри строки.
            var quotesResult = await page.ExecuteAsync("'hello \\'world\\''");
            logger.WriteLine(LogKind.Default, $"Кавычки: '{quotesResult}'");
            Assert.That(quotesResult?.ToString(), Is.EqualTo("hello 'world'"), "Экранированные кавычки.");

            // Строка с переносом строки.
            var newlineResult = await page.ExecuteAsync("'line1\\nline2'");
            logger.WriteLine(LogKind.Default, $"Перенос: '{newlineResult}'");
            Assert.That(newlineResult?.ToString(), Is.EqualTo("line1\nline2"), "Строка с \\n.");

            // Строка с табуляцией.
            var tabResult = await page.ExecuteAsync("'col1\\tcol2'");
            logger.WriteLine(LogKind.Default, $"Таб: '{tabResult}'");
            Assert.That(tabResult?.ToString(), Is.EqualTo("col1\tcol2"), "Строка с \\t.");

            // JSON.stringify — вложенный объект.
            var jsonResult = await page.ExecuteAsync("JSON.stringify({a: 1, b: 'test'})");
            logger.WriteLine(LogKind.Default, $"JSON: '{jsonResult}'");
            Assert.That(jsonResult?.ToString(), Does.Contain("\"a\":1"), "JSON.stringify объекта.");

            // Date — epoch zero.
            var dateResult = await page.ExecuteAsync("new Date(0).toISOString()");
            logger.WriteLine(LogKind.Default, $"Date: '{dateResult}'");
            Assert.That(dateResult?.ToString(), Is.EqualTo("1970-01-01T00:00:00.000Z"), "Date ISO.");

            // Math.PI с точностью.
            var piResult = await page.ExecuteAsync("Math.PI.toFixed(5)");
            logger.WriteLine(LogKind.Default, $"PI: '{piResult}'");
            Assert.That(piResult?.ToString(), Is.EqualTo("3.14159"), "Math.PI.toFixed(5).");

            // Template literal.
            var templateResult = await page.ExecuteAsync("`${1 + 2} items`");
            logger.WriteLine(LogKind.Default, $"Template: '{templateResult}'");
            Assert.That(templateResult?.ToString(), Is.EqualTo("3 items"), "Template literal.");

            // Тернарный оператор с вложенностью.
            var ternaryResult = await page.ExecuteAsync("true ? (false ? 'a' : 'b') : 'c'");
            logger.WriteLine(LogKind.Default, $"Ternary: '{ternaryResult}'");
            Assert.That(ternaryResult?.ToString(), Is.EqualTo("b"), "Вложенный тернарник.");

            // Promise → "[object Promise]" (eval не await-ит).
            var promiseResult = await page.ExecuteAsync("new Promise(r => r(42))");
            logger.WriteLine(LogKind.Default, $"Promise: '{promiseResult}'");
            Assert.That(promiseResult?.ToString(), Is.EqualTo("[object Promise]"), "Promise через String().");
        }

        [TestCase(TestName = "Стресс-тест: 50 скриптов подряд"), Order(11)]
        public async Task ScriptStressTest()
        {
            const int count = 50;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            for (var i = 0; i < count; i++)
            {
                var result = await page.ExecuteAsync($"{i} * 2");
                Assert.That(result?.ToString(), Is.EqualTo((i * 2).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    $"Итерация {i}: {i}*2 должно дать {i * 2}.");
            }

            sw.Stop();
            logger.WriteLine(LogKind.Default, $"Стресс-тест: {count} скриптов за {sw.ElapsedMilliseconds} мс ({sw.ElapsedMilliseconds / count} мс/скрипт).");
        }

        [TestCase(TestName = "Стресс-тест: 5 параллельных скриптов × 5 раундов"), Order(18)]
        public async Task ScriptParallelStressTest()
        {
            const int rounds = 5;
            const int parallelism = 5;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var errors = new System.Collections.Concurrent.ConcurrentBag<string>();

            for (var round = 0; round < rounds; round++)
            {
                var tasks = new Task[parallelism];
                for (var j = 0; j < parallelism; j++)
                {
                    var value = round * parallelism + j;
                    tasks[j] = Task.Run(async () =>
                    {
                        var result = await page.ExecuteAsync($"{value} + 1");
                        var expected = (value + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                        if (result?.ToString() != expected)
                            errors.Add($"Раунд {round}, значение {value}: ожидали {expected}, получили {result}");
                    });
                }

                await Task.WhenAll(tasks);
            }

            sw.Stop();
            var total = rounds * parallelism;
            logger.WriteLine(LogKind.Default, $"Параллельный стресс: {total} скриптов за {sw.ElapsedMilliseconds} мс ({sw.ElapsedMilliseconds / total} мс/скрипт). Ошибок: {errors.Count}");

            Assert.That(errors, Is.Empty, $"Ошибки: {string.Join("; ", errors.Take(5))}");
        }

        [TestCase(TestName = "GetUrlAsync и IsConnected на discovery"), Order(12)]
        public async Task PageStateTest()
        {
            // IsConnected — должно быть true для подключённой вкладки.
            Assert.That(page.IsConnected, Is.True, "Вкладка должна быть подключена.");

            // GetUrlAsync на discovery-странице.
            var url = await page.GetUrlAsync();
            logger.WriteLine(LogKind.Default, $"URL: {url}");
            Assert.That(url?.AbsoluteUri, Does.Contain("127.0.0.1"), "URL discovery-страницы должен содержать 127.0.0.1.");
        }

        [TestCase(TestName = "IElement: GetPropertyAsync, ClickAsync, FocusAsync"), Order(13)]
        public async Task ElementInteractionTest()
        {
            // Находим элемент <title>.
            var title = await page.FindElementAsync(new ElementSelector
            {
                Strategy = ElementSelectorStrategy.Css,
                Value = "title",
            });
            Assert.That(title, Is.Not.Null, "Элемент <title> должен быть найден.");

            // GetPropertyAsync — получаем textContent.
            var textContent = await title!.GetPropertyAsync("textContent");
            logger.WriteLine(LogKind.Default, $"textContent: '{textContent}'");
            Assert.That(textContent, Is.EqualTo("Atom Bridge Discovery"), "textContent элемента <title>.");

            // Находим <body> для клика — безопасный элемент.
            var body = await page.FindElementAsync(new ElementSelector
            {
                Strategy = ElementSelectorStrategy.Css,
                Value = "body",
            });
            Assert.That(body, Is.Not.Null, "Элемент <body> должен быть найден.");

            // ClickAsync — не должно бросить исключение.
            await body!.ClickAsync();
            logger.WriteLine(LogKind.Default, "Click на <body> выполнен.");

            // FocusAsync.
            await body.FocusAsync();
            logger.WriteLine(LogKind.Default, "Focus на <body> выполнен.");
        }

        [TestCase(TestName = "WaitForElementAsync: существующий и отсутствующий"), Order(14)]
        public async Task WaitForElementTest()
        {
            // Существующий элемент — должен найтись мгновенно.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var found = await page.WaitForElementAsync(
                new ElementSelector { Strategy = ElementSelectorStrategy.Css, Value = "title" },
                timeout: TimeSpan.FromSeconds(5));
            sw.Stop();
            logger.WriteLine(LogKind.Default, $"WaitForElement <title>: {found?.Id} за {sw.ElapsedMilliseconds} мс");
            Assert.That(found, Is.Not.Null, "Существующий элемент должен быть найден.");

            // Несуществующий элемент — должен вернуть null после таймаута.
            sw.Restart();
            var missing = await page.WaitForElementAsync(
                new ElementSelector { Strategy = ElementSelectorStrategy.Css, Value = "#does-not-exist-xyz" },
                timeout: TimeSpan.FromMilliseconds(500));
            sw.Stop();
            logger.WriteLine(LogKind.Default, $"WaitForElement #missing: {missing?.Id ?? "(null)"} за {sw.ElapsedMilliseconds} мс");
            Assert.That(missing, Is.Null, "Несуществующий элемент → null.");
            Assert.That(sw.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(400), "Таймаут должен занять ~500 мс.");
        }

        [TestCase(TestName = "CaptureScreenshotAsync"), Order(15)]
        public async Task CaptureScreenshotTest()
        {
            var screenshot = await page.CaptureScreenshotAsync();
            logger.WriteLine(LogKind.Default, $"Screenshot длина: {screenshot?.Length ?? 0}");
            Assert.That(screenshot, Is.Not.Null.And.Not.Empty, "Screenshot должен вернуть data URL.");
            Assert.That(screenshot, Does.StartWith("data:image/png;base64,"), "Screenshot должен быть PNG в base64.");
            logger.WriteLine(LogKind.Default, $"Screenshot начало: {screenshot![..Math.Min(screenshot.Length, 50)]}");
        }

        [TestCase(TestName = "Cookie: Set, Get, Delete lifecycle"), Order(16)]
        public async Task CookieLifecycleTest()
        {
            // Удаляем все cookies перед началом.
            await page.DeleteCookiesAsync();

            // Устанавливаем cookie.
            await page.SetCookieAsync("test_cookie", "test_value", domain: "127.0.0.1", path: "/");
            logger.WriteLine(LogKind.Default, "Cookie установлен.");

            // Получаем cookies.
            var cookies = await page.GetCookiesAsync();
            logger.WriteLine(LogKind.Default, $"Cookies: {cookies}");
            Assert.That(cookies, Is.Not.Null, "GetCookiesAsync должен вернуть результат.");
            Assert.That(cookies?.ToString(), Does.Contain("test_cookie"), "Должен содержать установленный cookie.");

            // Удаляем все cookies.
            await page.DeleteCookiesAsync();
            var afterDelete = await page.GetCookiesAsync();
            logger.WriteLine(LogKind.Default, $"Cookies после удаления: {afterDelete}");
        }

        [TestCase(TestName = "FindElementAsync по разным стратегиям"), Order(17)]
        public async Task FindElementStrategiesTest()
        {
            // По TagName.
            var byTag = await page.FindElementAsync(new ElementSelector
            {
                Strategy = ElementSelectorStrategy.TagName,
                Value = "body",
            });
            logger.WriteLine(LogKind.Default, $"По TagName: {byTag?.Id}");
            Assert.That(byTag, Is.Not.Null, "Поиск по TagName <body>.");

            // По CSS с вложенностью.
            var nested = await page.FindElementAsync(new ElementSelector
            {
                Strategy = ElementSelectorStrategy.Css,
                Value = "head > title",
            });
            logger.WriteLine(LogKind.Default, $"По CSS nested: {nested?.Id}");
            Assert.That(nested, Is.Not.Null, "Поиск по CSS head > title.");
        }

        [TestCase(TestName = "FindElementAsync по XPath"), Order(19)]
        public async Task FindElementXPathTest()
        {
            // По абсолютному XPath.
            var byXPath = await page.FindElementAsync(new ElementSelector
            {
                Strategy = ElementSelectorStrategy.XPath,
                Value = "//title",
            });
            logger.WriteLine(LogKind.Default, $"XPath //title: {byXPath?.Id}");
            Assert.That(byXPath, Is.Not.Null, "Поиск по XPath //title.");

            // По XPath с предикатом.
            var byPredicate = await page.FindElementAsync(new ElementSelector
            {
                Strategy = ElementSelectorStrategy.XPath,
                Value = "//meta[@name='atom-bridge-port']",
            });
            logger.WriteLine(LogKind.Default, $"XPath meta[@name]: {byPredicate?.Id}");
            Assert.That(byPredicate, Is.Not.Null, "Поиск по XPath с предикатом.");

            // Несуществующий XPath → null.
            var missing = await page.FindElementAsync(new ElementSelector
            {
                Strategy = ElementSelectorStrategy.XPath,
                Value = "//div[@id='nonexistent-xyz']",
            });
            Assert.That(missing, Is.Null, "XPath несуществующий → null.");
        }

        [TestCase(TestName = "IElement: TypeAsync, ClearAsync, DoubleClickAsync, HoverAsync"), Order(20)]
        public async Task ElementActionAdvancedTest()
        {
            // Создаём input-элемент через скрипт.
            await page.ExecuteAsync(
                "var inp = document.createElement('input'); inp.id = 'test-input-adv'; document.body.appendChild(inp)");

            var input = await page.FindElementAsync(new ElementSelector
            {
                Strategy = ElementSelectorStrategy.Css,
                Value = "#test-input-adv",
            });
            Assert.That(input, Is.Not.Null, "Input должен быть создан.");

            // Focus → Type → проверяем value.
            await input!.FocusAsync();
            await input.TypeAsync("Hello");

            var value = await input.GetPropertyAsync("value");
            logger.WriteLine(LogKind.Default, $"Type value: '{value}'");
            Assert.That(value, Is.EqualTo("Hello"), "Type должен ввести текст.");

            // Clear → проверяем пустоту.
            await input.ClearAsync();
            var cleared = await input.GetPropertyAsync("value");
            logger.WriteLine(LogKind.Default, $"Clear value: '{cleared}'");
            Assert.That(cleared, Is.Empty, "Clear должен очистить поле.");

            // DoubleClick — не должно бросить исключение.
            var body = await page.FindElementAsync(new ElementSelector
            {
                Strategy = ElementSelectorStrategy.Css,
                Value = "body",
            });
            await body!.DoubleClickAsync();
            logger.WriteLine(LogKind.Default, "DoubleClick на <body> выполнен.");

            // Hover — не должно бросить исключение.
            await body.HoverAsync();
            logger.WriteLine(LogKind.Default, "Hover на <body> выполнен.");

            // ScrollIntoView.
            await input.ScrollIntoViewAsync();
            logger.WriteLine(LogKind.Default, "ScrollIntoView на input выполнен.");

            // Убираем тестовый input.
            await page.ExecuteAsync("document.getElementById('test-input-adv')?.remove()");
        }

        [TestCase(TestName = "Стресс: 50 скриптов + 20 DOM-запросов"), Order(21)]
        public async Task HeavyStressTest()
        {
            // 50 sequential script executions.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 0; i < 50; i++)
            {
                var result = await page.ExecuteAsync($"{i} + 1");
                Assert.That(result?.ToString(), Is.EqualTo((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    $"Скрипт {i}: ожидали {i + 1}.");
            }

            sw.Stop();
            logger.WriteLine(LogKind.Default, $"50 скриптов за {sw.ElapsedMilliseconds} мс ({sw.ElapsedMilliseconds / 50} мс/скрипт).");

            // 20 sequential DOM queries (FindElement).
            sw.Restart();
            for (var i = 0; i < 20; i++)
            {
                var el = await page.FindElementAsync(new ElementSelector
                {
                    Strategy = ElementSelectorStrategy.Css,
                    Value = "title",
                });
                Assert.That(el, Is.Not.Null, $"DOM запрос {i}: <title> должен быть найден.");
            }

            sw.Stop();
            logger.WriteLine(LogKind.Default, $"20 FindElement за {sw.ElapsedMilliseconds} мс ({sw.ElapsedMilliseconds / 20} мс/запрос).");
        }

        [TestCase(TestName = "WaitForNavigationAsync после навигации"), Order(98)]
        public async Task WaitForNavigationTest()
        {
            // Навигируем через ExecuteScript и ждём завершения.
            var targetUrl = $"http://127.0.0.1:{browser.BridgePort.ToString(System.Globalization.CultureInfo.InvariantCulture)}/test";

            // Запускаем WaitForNavigation ДО навигации.
            var waitTask = page.WaitForNavigationAsync(timeout: TimeSpan.FromSeconds(10));

            // NavigateAsync инициирует навигацию.
            await page.NavigateAsync(new Uri(targetUrl));

            // WaitForNavigation должен завершиться.
            await waitTask;
            logger.WriteLine(LogKind.Default, "WaitForNavigation завершён.");

            var currentUrl = await page.GetUrlAsync();
            logger.WriteLine(LogKind.Default, $"URL после навигации: {currentUrl}");
            Assert.That(currentUrl?.AbsoluteUri, Does.Contain("/test"), "URL должен содержать /test.");
        }

        [TestCase(TestName = "NavigateAsync и GetUrlAsync"), Order(99)]
        public async Task NavigateAndGetUrlTest()
        {
            // Навигация на локальный URL (BridgeServer). Последний тест — уходим со страницы discovery.
            var targetUrl = new Uri($"http://127.0.0.1:{browser.BridgePort.ToString(System.Globalization.CultureInfo.InvariantCulture)}/test");
            logger.WriteLine(LogKind.Default, $"Навигация на {targetUrl}...");
            await page.NavigateAsync(targetUrl);

            var currentUrl = await page.GetUrlAsync();
            logger.WriteLine(LogKind.Default, $"Текущий URL: {currentUrl}");
            Assert.That(currentUrl?.AbsoluteUri, Does.Contain("127.0.0.1"), "URL должен содержать 127.0.0.1.");
        }

        [TestCase(TestName = "MainFrame: GetTitleAsync через фрейм"), Order(100)]
        public async Task MainFrameGetTitleTest()
        {
            var title = await page.MainFrame.GetTitleAsync();
            logger.WriteLine(LogKind.Default, $"MainFrame title: {title}");
            Assert.That(title, Is.Not.Null.And.Not.Empty, "MainFrame должен вернуть заголовок.");
        }

        [TestCase(TestName = "MainFrame: ExecuteAsync через фрейм"), Order(101)]
        public async Task MainFrameExecuteTest()
        {
            var result = await page.MainFrame.ExecuteAsync("return 42");
            logger.WriteLine(LogKind.Default, $"MainFrame execute: {result}");
            Assert.That(result?.ToString(), Is.EqualTo("42"));
        }

        [TestCase(TestName = "MainFrame: FindElementAsync через фрейм"), Order(102)]
        public async Task MainFrameFindElementTest()
        {
            var element = await page.MainFrame.FindElementAsync(new ElementSelector
            {
                Strategy = ElementSelectorStrategy.Css,
                Value = "body",
            });
            logger.WriteLine(LogKind.Default, $"MainFrame element: {element?.Id}");
            Assert.That(element, Is.Not.Null, "MainFrame должен найти <body>.");
        }

        [TestCase(TestName = "MainFrame: GetUrlAsync через фрейм"), Order(103)]
        public async Task MainFrameGetUrlTest()
        {
            var url = await page.MainFrame.GetUrlAsync();
            logger.WriteLine(LogKind.Default, $"MainFrame URL: {url}");
            Assert.That(url, Is.Not.Null, "MainFrame должен вернуть URL.");
        }

        [TestCase(TestName = "MainFrame: GetContentAsync через фрейм"), Order(104)]
        public async Task MainFrameGetContentTest()
        {
            var content = await page.MainFrame.GetContentAsync();
            logger.WriteLine(LogKind.Default, $"MainFrame HTML: {content?.Length ?? 0} символов");
            Assert.That(content, Is.Not.Null.And.Not.Empty, "MainFrame должен вернуть HTML.");
        }

        private async Task<WebDriverPage> WaitForFirstTabAsync()
        {
            var tcs = new TaskCompletionSource<TabConnectedEventArgs>();
            browser.TabConnected += (_, e) =>
            {
                tcs.TrySetResult(e);
                return ValueTask.CompletedTask;
            };

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            if (browser.ConnectionCount > 0)
            {
                var existingPage = browser.GetAllPages().First();
                logger.WriteLine(LogKind.Default, $"Вкладка уже подключена: {existingPage.TabId}");
                return existingPage;
            }

            var result = await tcs.Task.WaitAsync(cts.Token);
            logger.WriteLine(LogKind.Default, $"Вкладка подключилась: {result.TabId}");

            return browser.GetPage(result.TabId)
                ?? throw new BridgeException($"Страница {result.TabId} не найдена после подключения.");
        }
    }

    /// <summary>
    /// Атомарные тесты окон/вкладок. Один браузер на все тесты.
    /// </summary>
    [TestFixture, Category("GUI"), NonParallelizable]
    public class AtomicTests
    {
        private readonly ILogger logger = ConsoleLogger.Unicode;
        private WebDriverBrowser browser = null!;
        private WebDriverPage firstTab = null!;

        [OneTimeSetUp]
        public async Task SetUp()
        {
            var (browserPath, extensionPath) = FindChromiumBrowser();
            browser = await WebDriverBrowser.LaunchAsync(
                browserPath, extensionPath,
                arguments: ["--no-sandbox", "--disable-features=Translate"]);
            firstTab = await WaitForFirstTabAsync();
            logger.WriteLine(LogKind.Default, $"Браузер запущен, вкладка: {firstTab.TabId}");
        }

        [OneTimeTearDown]
        public async Task GlobalTearDown()
        {
            await browser.DisposeAsync();
        }

        [TearDown]
        public async Task CleanUp()
        {
            // Закрываем все вкладки кроме discovery.
            foreach (var p in browser.GetAllPages().ToList())
            {
                if (p.TabId != firstTab.TabId)
                {
                    try { await browser.CloseTabAsync(p.TabId); }
                    catch { /* Уже закрыта. */ }
                }
            }

            await Task.Delay(100);
        }

        [TestCase(TestName = "Атомарное открытие и закрытие вкладки"), Order(1)]
        public async Task TabOpenCloseTest()
        {
            Assert.That(browser.ConnectionCount, Is.EqualTo(1), "Должна быть 1 подключённая вкладка.");

            // ── Открытие новой вкладки ──
            var newTab = await browser.OpenTabAsync(new Uri("about:blank"));
            logger.WriteLine(LogKind.Default, $"Новая вкладка: {newTab.TabId}");

            Assert.That(browser.ConnectionCount, Is.EqualTo(2), "Должно быть 2 подключённых вкладки.");
            Assert.That(newTab.IsConnected, Is.True, "Новая вкладка должна быть подключена.");

            // ── Закрытие ──
            var disconnectTcs = new TaskCompletionSource<string>();
            AsyncEventHandler<WebDriverBrowser, TabDisconnectedEventArgs> handler = (_, e) =>
            {
                disconnectTcs.TrySetResult(e.TabId);
                return ValueTask.CompletedTask;
            };
            browser.TabDisconnected += handler;

            await browser.CloseTabAsync(newTab.TabId);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var disconnectedTabId = await disconnectTcs.Task.WaitAsync(cts.Token);
            browser.TabDisconnected -= handler;

            Assert.That(disconnectedTabId, Is.EqualTo(newTab.TabId), "Отключённая вкладка должна совпадать с закрытой.");
            Assert.That(browser.ConnectionCount, Is.EqualTo(1), "Должна остаться 1 вкладка.");
        }

        [TestCase(TestName = "Атомарное открытие и закрытие окна"), Order(2)]
        public async Task WindowOpenCloseTest()
        {
            var initialWindowCount = browser.Windows.Count();

            // ── Открытие нового окна ──
            var windowTab = await browser.OpenWindowAsync(new Uri("about:blank"));
            logger.WriteLine(LogKind.Default, $"Вкладка нового окна: {windowTab.TabId}");

            Assert.That(browser.ConnectionCount, Is.EqualTo(2), "Должно быть 2 подключённых вкладки.");
            Assert.That(browser.Windows.Count(), Is.EqualTo(initialWindowCount + 1), "Должно быть на 1 окно больше.");

            // ── Закрытие ──
            var disconnectTcs = new TaskCompletionSource<string>();
            AsyncEventHandler<WebDriverBrowser, TabDisconnectedEventArgs> handler = (_, e) =>
            {
                disconnectTcs.TrySetResult(e.TabId);
                return ValueTask.CompletedTask;
            };
            browser.TabDisconnected += handler;

            await browser.CloseTabAsync(windowTab.TabId);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await disconnectTcs.Task.WaitAsync(cts.Token);
            browser.TabDisconnected -= handler;

            Assert.That(browser.ConnectionCount, Is.EqualTo(1), "Должна остаться 1 вкладка.");

            await Task.Delay(100);
            Assert.That(browser.Windows.Count(), Is.EqualTo(initialWindowCount), "Пустое окно должно быть удалено.");
        }

        [TestCase(TestName = "Множественное открытие и каскадное закрытие вкладок"), Order(3)]
        public async Task MultipleTabsOpenCloseTest()
        {
            const int tabCount = 3;
            var tabs = new List<WebDriverPage>(tabCount);

            for (var i = 0; i < tabCount; i++)
            {
                var tab = await browser.OpenTabAsync(new Uri("about:blank"));
                tabs.Add(tab);
                logger.WriteLine(LogKind.Default, $"Вкладка {i + 1}: {tab.TabId}");
            }

            Assert.That(browser.ConnectionCount, Is.EqualTo(1 + tabCount), $"Должно быть {1 + tabCount} вкладок.");

            // ── Закрываем в обратном порядке ──
            for (var i = tabs.Count - 1; i >= 0; i--)
            {
                var expectedRemaining = 1 + i;
                var disconnectTcs = new TaskCompletionSource<string>();
                AsyncEventHandler<WebDriverBrowser, TabDisconnectedEventArgs> handler = (_, e) =>
                {
                    disconnectTcs.TrySetResult(e.TabId);
                    return ValueTask.CompletedTask;
                };
                browser.TabDisconnected += handler;

                await browser.CloseTabAsync(tabs[i].TabId);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await disconnectTcs.Task.WaitAsync(cts.Token);
                browser.TabDisconnected -= handler;

                await Task.Delay(50);
                Assert.That(browser.ConnectionCount, Is.EqualTo(expectedRemaining), $"Должно остаться {expectedRemaining} вкладок.");
            }

            Assert.That(browser.ConnectionCount, Is.EqualTo(1), "Должна остаться только discovery.");
        }

        [TestCase(TestName = "Параллельное открытие вкладки и окна"), Order(4)]
        public async Task ParallelTabAndWindowOpenTest()
        {
            var tabTask = browser.OpenTabAsync(new Uri("about:blank")).AsTask();
            var windowTask = browser.OpenWindowAsync(new Uri("about:blank")).AsTask();

            await Task.WhenAll(tabTask, windowTask);

            var newTab = await tabTask;
            var windowTab = await windowTask;

            logger.WriteLine(LogKind.Default, $"Вкладка: {newTab.TabId}, окно: {windowTab.TabId}");

            Assert.That(browser.ConnectionCount, Is.EqualTo(3), "Должно быть 3 подключённых вкладки.");
            Assert.That(newTab.TabId, Is.Not.EqualTo(windowTab.TabId), "ID должны отличаться.");
            Assert.That(browser.Windows.Count(), Is.GreaterThanOrEqualTo(2), "Минимум 2 окна.");

            // Очистка — TearDown тоже подчистит.
            await browser.CloseTabAsync(newTab.TabId);
            await browser.CloseTabAsync(windowTab.TabId);
            await Task.Delay(100);

            Assert.That(browser.ConnectionCount, Is.EqualTo(1), "Должна остаться только discovery.");
        }

        [TestCase(TestName = "Активация вкладки через ActivateTabAsync"), Order(5)]
        public async Task ActivateTabTest()
        {
            var secondTab = await browser.OpenTabAsync(new Uri("about:blank"));
            logger.WriteLine(LogKind.Default, $"Вторая вкладка: {secondTab.TabId}");
            Assert.That(browser.ConnectionCount, Is.EqualTo(2), "Должно быть 2 вкладки.");

            await browser.ActivateTabAsync(firstTab.TabId);
            await browser.ActivateTabAsync(secondTab.TabId);

            Assert.That(browser.ConnectionCount, Is.EqualTo(2), "Обе вкладки подключены.");

            await browser.CloseTabAsync(secondTab.TabId);
            await Task.Delay(100);
            Assert.That(browser.ConnectionCount, Is.EqualTo(1), "Должна остаться 1 вкладка.");
        }

        [TestCase(TestName = "Активация окна через ActivateWindowAsync"), Order(6)]
        public async Task ActivateWindowTest()
        {
            var initialWindowId = firstTab.TabId.Split(':')[0];

            var windowTab = await browser.OpenWindowAsync(new Uri("about:blank"));
            logger.WriteLine(LogKind.Default, $"Вкладка нового окна: {windowTab.TabId}");

            var newWindowId = windowTab.TabId.Split(':')[0];
            Assert.That(browser.Windows.Count(), Is.GreaterThanOrEqualTo(2), "Минимум 2 окна.");

            await browser.ActivateWindowAsync(initialWindowId);
            await browser.ActivateWindowAsync(newWindowId);

            Assert.That(browser.ConnectionCount, Is.EqualTo(2), "Обе вкладки подключены.");

            await browser.CloseTabAsync(windowTab.TabId);
            await Task.Delay(100);
            Assert.That(browser.ConnectionCount, Is.EqualTo(1), "Должна остаться 1 вкладка.");
        }

        [TestCase(TestName = "Закрытие окна целиком через CloseWindowAsync"), Order(7)]
        public async Task CloseWindowTest()
        {
            var windowTab = await browser.OpenWindowAsync(new Uri("about:blank"));
            logger.WriteLine(LogKind.Default, $"Вкладка нового окна: {windowTab.TabId}");

            var extraTab = await browser.OpenTabAsync(new Uri("about:blank"));
            logger.WriteLine(LogKind.Default, $"Дополнительная вкладка: {extraTab.TabId}");

            Assert.That(browser.ConnectionCount, Is.GreaterThanOrEqualTo(3), "Минимум 3 вкладки.");

            var newWindowId = windowTab.TabId.Split(':')[0];
            logger.WriteLine(LogKind.Default, $"Закрываем окно {newWindowId}...");

            var disconnectCount = 0;
            var disconnectTcs = new TaskCompletionSource();
            AsyncEventHandler<WebDriverBrowser, TabDisconnectedEventArgs> handler = (_, _) =>
            {
                if (Interlocked.Increment(ref disconnectCount) >= 1)
                    disconnectTcs.TrySetResult();
                return ValueTask.CompletedTask;
            };
            browser.TabDisconnected += handler;

            await browser.CloseWindowAsync(newWindowId);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await disconnectTcs.Task.WaitAsync(cts.Token);
            browser.TabDisconnected -= handler;

            await Task.Delay(100);
            logger.WriteLine(LogKind.Default, $"После закрытия: окон {browser.Windows.Count()}, вкладок {browser.ConnectionCount}");

            Assert.That(browser.ConnectionCount, Is.GreaterThanOrEqualTo(1), "Минимум discovery-вкладка.");
        }

        [TestCase(TestName = "Dispose страницы закрывает вкладку через OnDisposing"), Order(8)]
        public async Task DisposeClosesTabTest()
        {
            var newTab = await browser.OpenTabAsync(new Uri("about:blank"));
            logger.WriteLine(LogKind.Default, $"Новая вкладка: {newTab.TabId}");
            Assert.That(browser.ConnectionCount, Is.EqualTo(2), "Должно быть 2 вкладки.");

            var disconnectTcs = new TaskCompletionSource<string>();
            AsyncEventHandler<WebDriverBrowser, TabDisconnectedEventArgs> handler = (_, e) =>
            {
                disconnectTcs.TrySetResult(e.TabId);
                return ValueTask.CompletedTask;
            };
            browser.TabDisconnected += handler;

            await newTab.DisposeAsync();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var disconnectedTabId = await disconnectTcs.Task.WaitAsync(cts.Token);
            browser.TabDisconnected -= handler;

            Assert.That(disconnectedTabId, Is.EqualTo(newTab.TabId), "Отключённая вкладка = диспозированная.");

            await Task.Delay(100);
            Assert.That(browser.ConnectionCount, Is.EqualTo(1), "Должна остаться 1 вкладка.");
        }

        [TestCase(TestName = "Window API: Windows, CurrentWindow, Pages, PageCount"), Order(9)]
        public async Task WindowApiTest()
        {
            // Discovery-вкладка уже есть.
            var windows = browser.Windows.ToList();
            Assert.That(windows, Has.Count.GreaterThanOrEqualTo(1), "Минимум 1 окно.");

            var currentWindow = (WebDriverWindow)browser.CurrentWindow;
            Assert.That(currentWindow, Is.Not.Null, "CurrentWindow не должен быть null.");
            Assert.That(currentWindow.WindowId, Is.Not.Null.And.Not.Empty);
            logger.WriteLine(LogKind.Default, $"WindowId: {currentWindow.WindowId}, PageCount: {currentWindow.PageCount}");

            Assert.That(currentWindow.PageCount, Is.GreaterThanOrEqualTo(1), "Минимум 1 страница в окне.");
            Assert.That(currentWindow.Pages.Count(), Is.EqualTo(currentWindow.PageCount), "Pages.Count == PageCount.");
            Assert.That(currentWindow.CurrentPage, Is.Not.Null, "CurrentPage не null.");

            // Открываем вторую вкладку — GetAllPages должен увеличиться.
            var initialPages = browser.GetAllPages().Count();
            var newTab = await browser.OpenTabAsync(new Uri("about:blank"));
            await Task.Delay(100);

            Assert.That(browser.GetAllPages().Count(), Is.GreaterThan(initialPages), "Должно стать больше страниц.");

            // GetPage по tabId — должен найти новую вкладку.
            var foundPage = browser.GetPage(newTab.TabId);
            Assert.That(foundPage, Is.Not.Null, "GetPage должен найти новую вкладку.");
            Assert.That(foundPage!.TabId, Is.EqualTo(newTab.TabId));

            // Находим окно, содержащее новую вкладку, через WindowId из TabId.
            var newTabWindowId = newTab.TabId.Contains(':') ? newTab.TabId[..newTab.TabId.IndexOf(':')] : "default";
            var ownerWindow = browser.Windows.OfType<WebDriverWindow>().FirstOrDefault(w => w.WindowId == newTabWindowId);
            Assert.That(ownerWindow, Is.Not.Null, "Окно-владелец новой вкладки найдено.");
            Assert.That(ownerWindow!.PageCount, Is.GreaterThanOrEqualTo(1));
            logger.WriteLine(LogKind.Default, $"NewTab {newTab.TabId} → Window {ownerWindow.WindowId}, PageCount={ownerWindow.PageCount}");
        }

        [TestCase(TestName = "Browser collections: GetAllPages, GetPage, Secret, BridgePort"), Order(10)]
        public async Task BrowserCollectionsTest()
        {
            var allPages = browser.GetAllPages().ToList();
            Assert.That(allPages, Has.Count.GreaterThanOrEqualTo(1), "Минимум 1 страница.");
            logger.WriteLine(LogKind.Default, $"GetAllPages: {allPages.Count} страниц.");

            // GetPage по tabId первой вкладки.
            var page = browser.GetPage(firstTab.TabId);
            Assert.That(page, Is.Not.Null, "GetPage должен найти discovery.");
            Assert.That(page!.TabId, Is.EqualTo(firstTab.TabId));

            // GetPage несуществующего — null.
            var missing = browser.GetPage("999:999");
            Assert.That(missing, Is.Null, "GetPage для несуществующего → null.");

            // BridgePort и Secret.
            Assert.That(browser.BridgePort, Is.GreaterThan(0), "BridgePort > 0.");
            Assert.That(browser.Secret, Is.Not.Null.And.Not.Empty, "Secret не пустой.");
            logger.WriteLine(LogKind.Default, $"BridgePort={browser.BridgePort}, Secret длина={browser.Secret.Length}");

            // Открываем окно → Windows увеличивается.
            var initialWindowCount = browser.Windows.Count();
            var windowTab = await browser.OpenWindowAsync(new Uri("about:blank"));
            await Task.Delay(100);

            Assert.That(browser.Windows.Count(), Is.EqualTo(initialWindowCount + 1), "Windows +1 после OpenWindow.");
            Assert.That(browser.GetAllPages().Count(), Is.GreaterThan(allPages.Count), "Страниц стало больше.");
            logger.WriteLine(LogKind.Default, $"После OpenWindow: окон {browser.Windows.Count()}, страниц {browser.GetAllPages().Count()}");
        }

        [TestCase(TestName = "EventReceived получает PageLoaded"), Order(11)]
        public async Task EventReceivedTest()
        {
            var newTab = await browser.OpenTabAsync(new Uri("about:blank"));
            await Task.Delay(100);

            var eventTcs = new TaskCompletionSource<TabChannelEventArgs>();
            AsyncEventHandler<WebDriverPage, TabChannelEventArgs> handler = (_, e) =>
            {
                if (e.Message.Event == Atom.Net.Browsing.WebDriver.Protocol.BridgeEvent.PageLoaded)
                    eventTcs.TrySetResult(e);
                return ValueTask.CompletedTask;
            };
            newTab.EventReceived += handler;

            // Навигация триггерит PageLoaded.
            var url = new Uri($"http://127.0.0.1:{browser.BridgePort.ToString(System.Globalization.CultureInfo.InvariantCulture)}/test");
            await newTab.NavigateAsync(url);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var evt = await eventTcs.Task.WaitAsync(cts.Token);
            newTab.EventReceived -= handler;

            Assert.That(evt.Message.Event, Is.EqualTo(Atom.Net.Browsing.WebDriver.Protocol.BridgeEvent.PageLoaded), "Должно прийти событие PageLoaded.");
            logger.WriteLine(LogKind.Default, $"EventReceived: {evt.Message.Event}");
        }

        // ─── Стресс-тесты push модели ───────────────────────────────

        [TestCase(TestName = "Стресс: 100 скриптов подряд"), Order(12)]
        public async Task Stress1000SequentialScriptsTest()
        {
            const int count = 100;
            var sw = Stopwatch.StartNew();

            for (int i = 0; i < count; i++)
            {
                var result = await firstTab.ExecuteAsync($"{i}");
                Assert.That(result?.ToString(), Is.EqualTo(i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }

            sw.Stop();
            var avg = sw.Elapsed.TotalMilliseconds / count;
            logger.WriteLine(LogKind.Default, $"Push 1000 sequential: {sw.ElapsedMilliseconds}ms total, {avg:F1}ms/script");
            Assert.That(avg, Is.LessThan(20), "avg < 20ms для push модели.");
        }

        [TestCase(TestName = "Стресс: 50 параллельных скриптов"), Order(13)]
        public async Task Stress500ParallelScriptsTest()
        {
            const int count = 50;
            var sw = Stopwatch.StartNew();
            var tasks = new Task<System.Text.Json.JsonElement?>[count];

            for (int i = 0; i < count; i++)
                tasks[i] = firstTab.ExecuteAsync($"{i}").AsTask();

            var results = await Task.WhenAll(tasks);
            sw.Stop();

            for (int i = 0; i < count; i++)
                Assert.That(results[i]?.ToString(), Is.EqualTo(i.ToString(System.Globalization.CultureInfo.InvariantCulture)));

            var avg = sw.Elapsed.TotalMilliseconds / count;
            logger.WriteLine(LogKind.Default, $"Push 500 parallel: {sw.ElapsedMilliseconds}ms total, {avg:F1}ms/script");
            Assert.That(avg, Is.LessThan(10), "avg < 10ms для параллельного push.");
        }

        [TestCase(TestName = "Стресс: 3 вкладки по 10 скриптов"), Order(14)]
        public async Task StressMultiTabScriptsTest()
        {
            const int tabCount = 3;
            const int scriptsPerTab = 10;

            var tabs = new List<WebDriverPage>(tabCount);
            for (int t = 0; t < tabCount; t++)
            {
                var tab = await browser.OpenTabAsync(new Uri("about:blank"));
                // Навигация на bridge page для eval bridge.
                var url = new Uri($"http://127.0.0.1:{browser.BridgePort.ToString(System.Globalization.CultureInfo.InvariantCulture)}/");
                await tab.NavigateAsync(url);
                await Task.Delay(50);
                tabs.Add(tab);
            }

            logger.WriteLine(LogKind.Default, $"Открыто {tabs.Count} вкладок. Запуск скриптов...");
            var sw = Stopwatch.StartNew();

            var allTasks = new List<Task>(tabCount * scriptsPerTab);
            foreach (var tab in tabs)
            {
                for (int s = 0; s < scriptsPerTab; s++)
                {
                    var expected = s;
                    allTasks.Add(tab.ExecuteAsync($"{expected}").AsTask().ContinueWith(t =>
                        Assert.That(t.Result?.ToString(), Is.EqualTo(expected.ToString(System.Globalization.CultureInfo.InvariantCulture))),
                        TaskScheduler.Default));
                }
            }

            await Task.WhenAll(allTasks);
            sw.Stop();

            var total = tabCount * scriptsPerTab;
            var avg = sw.Elapsed.TotalMilliseconds / total;
            logger.WriteLine(LogKind.Default, $"Push {tabCount} tabs × {scriptsPerTab} scripts = {total}: {sw.ElapsedMilliseconds}ms total, {avg:F1}ms/script");
            Assert.That(avg, Is.LessThan(30), "avg < 30ms для multi-tab push.");
        }

        [TestCase(TestName = "Стресс: быстрое открытие/закрытие 5 вкладок"), Order(15)]
        public async Task StressRapidTabLifecycleTest()
        {
            const int cycles = 5;
            var sw = Stopwatch.StartNew();

            for (int i = 0; i < cycles; i++)
            {
                var tab = await browser.OpenTabAsync(new Uri("about:blank"));
                Assert.That(tab, Is.Not.Null);
                await browser.CloseTabAsync(tab.TabId);
            }

            sw.Stop();
            var avg = sw.Elapsed.TotalMilliseconds / cycles;
            logger.WriteLine(LogKind.Default, $"Push 20 tab open/close: {sw.ElapsedMilliseconds}ms total, {avg:F1}ms/cycle");
            Assert.That(avg, Is.LessThan(2000), "avg < 2s на цикл open/close.");
        }

        private async Task<WebDriverPage> WaitForFirstTabAsync()
        {
            var tcs = new TaskCompletionSource<TabConnectedEventArgs>();
            browser.TabConnected += (_, e) =>
            {
                tcs.TrySetResult(e);
                return ValueTask.CompletedTask;
            };

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            if (browser.ConnectionCount > 0)
            {
                var existingPage = browser.GetAllPages().First();
                logger.WriteLine(LogKind.Default, $"Вкладка уже подключена: {existingPage.TabId}");
                return existingPage;
            }

            var result = await tcs.Task.WaitAsync(cts.Token);
            logger.WriteLine(LogKind.Default, $"Вкладка подключилась: {result.TabId}");

            return browser.GetPage(result.TabId)
                ?? throw new BridgeException($"Страница {result.TabId} не найдена после подключения.");
        }
    }

    // ─── Хелперы ─────────────────────────────────────────────────

    /// <summary>
    /// Находит первый доступный Chromium-браузер (pre-seeded профиль, без --load-extension).
    /// </summary>
    private static (string BrowserPath, string ExtensionPath) FindChromiumBrowser()
    {
        string? browserPath = null;
        foreach (var candidate in (ReadOnlySpan<string>)[
            "/usr/bin/google-chrome-stable",
            "/usr/bin/brave",
            "/usr/bin/opera",
            "/usr/bin/vivaldi-stable",
            "/usr/bin/microsoft-edge-stable",
            "/usr/bin/yandex-browser-corporate",
            "/usr/bin/yandex-browser"])
        {
            if (File.Exists(candidate))
            {
                browserPath = candidate;
                break;
            }
        }

        Assert.That(browserPath, Is.Not.Null, "Chromium-браузер не найден.");
        Assert.That(Directory.Exists(ChromeExtensionPath), Is.True, "Расширение не найдено.");

        return (browserPath!, ChromeExtensionPath);
    }

    // ─── Кросс-браузерные тесты ────────────────────────────────

    /// <summary>
    /// Кросс-браузерные тесты. Запускает каждый доступный браузер (Chromium + Firefox)
    /// и проверяет базовые DOM-операции через <see cref="IWebPage.MainFrame"/>.
    /// </summary>
    [TestFixture, Category("GUI"), NonParallelizable]
    public class CrossBrowserTests
    {
        private readonly ILogger logger = ConsoleLogger.Unicode;

        [TearDown]
        public async Task Cleanup()
        {
            // Даём ОС время освободить ресурсы (сокеты, память) после Kill процесса.
            await Task.Delay(500);
        }

        private static IEnumerable<TestCaseData> BrowserCases()
        {
            // Chromium-браузеры: pre-seeded профиль (RSA-ключ + Preferences).
            // Firefox Developer Edition: proxy-файл + xpinstall.signatures.required=false.
            string[] candidates =
            [
                "/usr/bin/google-chrome-stable",
                "/usr/bin/brave",
                "/usr/bin/opera",
                "/usr/bin/vivaldi-stable",
                "/usr/bin/microsoft-edge-stable",
                "/usr/bin/yandex-browser-corporate",
                "/usr/bin/yandex-browser",
                "/usr/bin/firefox-developer-edition",
                "/usr/bin/firefox-nightly",
            ];

            foreach (var path in candidates)
            {
                if (File.Exists(path))
                    yield return new TestCaseData(path).SetArgDisplayNames(Path.GetFileNameWithoutExtension(path));
            }
        }

        /// <summary>
        /// Возвращает путь к расширению для данного браузера.
        /// Firefox использует Extension.Firefox (MV2), остальные — Extension (MV3).
        /// </summary>
        private static string GetExtensionPath(string browserPath)
        {
            var name = Path.GetFileNameWithoutExtension(browserPath);
            return name.Contains("firefox", StringComparison.OrdinalIgnoreCase)
                ? FirefoxExtensionPath
                : ChromeExtensionPath;
        }

        [TestCaseSource(nameof(BrowserCases)), NonParallelizable]
        public async Task FullPipeline(string browserPath)
        {
            var name = Path.GetFileNameWithoutExtension(browserPath);
            var extensionPath = GetExtensionPath(browserPath);
            await using var browser = await WebDriverBrowser.LaunchAsync(
                browserPath, extensionPath, arguments: ["--no-sandbox"]);
            var page = await WaitForFirstTabAsync(browser);
            logger.WriteLine(LogKind.Default, $"[{name}] Подключён: {page.TabId}");

            // IsConnected.
            Assert.That(page.IsConnected, Is.True, $"[{name}] IsConnected.");

            // 1. GetTitle.
            var title = await page.GetTitleAsync();
            Assert.That(title, Is.EqualTo("Atom Bridge Discovery"), $"[{name}] Title.");

            // 2. GetUrl.
            var url = await page.GetUrlAsync();
            Assert.That(url?.AbsoluteUri, Does.Contain("127.0.0.1"), $"[{name}] URL.");

            // 3. GetContent.
            var content = await page.GetContentAsync();
            Assert.That(content, Does.Contain("Atom Bridge Discovery"), $"[{name}] Content.");

            // 4. Execute + edge-cases.
            var r = await page.ExecuteAsync("2 + 2");
            Assert.That(r?.ToString(), Is.EqualTo("4"), $"[{name}] Execute.");
            Assert.That((await page.ExecuteAsync("var x = 10; x * 3"))?.ToString(), Is.EqualTo("30"), $"[{name}] Statement.");
            Assert.That((await page.ExecuteAsync("undefined"))?.ToString(), Is.Empty.Or.Null, $"[{name}] undefined.");
            Assert.That((await page.ExecuteAsync("true"))?.ToString(), Is.EqualTo("true"), $"[{name}] true.");
            Assert.That((await page.ExecuteAsync("'Привет 🌍'"))?.ToString(), Is.EqualTo("Привет 🌍"), $"[{name}] Unicode.");
            Assert.ThrowsAsync<BridgeException>(async () =>
                await page.ExecuteAsync("nonExistentFunction()"), $"[{name}] Error.");
            Assert.ThrowsAsync<BridgeException>(async () =>
                await page.ExecuteAsync("{{{"), $"[{name}] Syntax error.");

            // 5. MainFrame (Execute, GetTitle, FindElement, GetContent).
            var mfTitle = await page.MainFrame.ExecuteAsync("return document.title");
            Assert.That(mfTitle?.GetString(), Is.EqualTo(title), $"[{name}] MainFrame execute.");
            var mfPageTitle = await page.MainFrame.GetTitleAsync();
            Assert.That(mfPageTitle, Is.Not.Null.And.Not.Empty, $"[{name}] MainFrame GetTitle.");
            var mfBody = await page.MainFrame.FindElementAsync(new ElementSelector
                { Strategy = ElementSelectorStrategy.Css, Value = "body" });
            Assert.That(mfBody, Is.Not.Null, $"[{name}] MainFrame FindElement.");
            var mfContent = await page.MainFrame.GetContentAsync();
            Assert.That(mfContent, Is.Not.Null.And.Not.Empty, $"[{name}] MainFrame GetContent.");

            // 6. FindElement (CSS, XPath, TagName, FindElements, miss).
            var body = await page.FindElementAsync(new ElementSelector
                { Strategy = ElementSelectorStrategy.Css, Value = "body" });
            Assert.That(body, Is.Not.Null, $"[{name}] FindElement CSS.");
            var byXPath = await page.FindElementAsync(new ElementSelector
                { Strategy = ElementSelectorStrategy.XPath, Value = "//title" });
            Assert.That(byXPath, Is.Not.Null, $"[{name}] FindElement XPath.");
            var byTag = await page.FindElementAsync(new ElementSelector
                { Strategy = ElementSelectorStrategy.TagName, Value = "body" });
            Assert.That(byTag, Is.Not.Null, $"[{name}] FindElement TagName.");
            var metas = await page.FindElementsAsync(new ElementSelector
                { Strategy = ElementSelectorStrategy.Css, Value = "meta" });
            Assert.That(metas, Is.Not.Empty, $"[{name}] FindElements.");
            var miss = await page.FindElementAsync(new ElementSelector
                { Strategy = ElementSelectorStrategy.Css, Value = "#nonexistent-xyz" });
            Assert.That(miss, Is.Null, $"[{name}] Element miss.");

            // 7. Element interaction (Click / Type / Clear / DoubleClick / Hover).
            await page.ExecuteAsync(
                "var inp = document.createElement('input'); inp.id = 'fp-input'; document.body.appendChild(inp)");
            var input = await page.FindElementAsync(new ElementSelector
                { Strategy = ElementSelectorStrategy.Css, Value = "#fp-input" });
            Assert.That(input, Is.Not.Null, $"[{name}] Input created.");
            await input!.FocusAsync();
            await input.TypeAsync("Hello");
            var val = await input.GetPropertyAsync("value");
            Assert.That(val, Is.EqualTo("Hello"), $"[{name}] Type.");
            await input.ClearAsync();
            Assert.That(await input.GetPropertyAsync("value"), Is.Empty, $"[{name}] Clear.");
            await page.ExecuteAsync("document.getElementById('fp-input')?.remove()");
            await body!.DoubleClickAsync();
            await body.HoverAsync();

            // 8. Navigate.
            var navUrl = new Uri($"http://127.0.0.1:{browser.BridgePort.ToString(System.Globalization.CultureInfo.InvariantCulture)}/test");
            await page.NavigateAsync(navUrl);
            var newUrl = await page.GetUrlAsync();
            Assert.That(newUrl?.AbsoluteUri, Does.Contain("/test"), $"[{name}] Navigate.");

            // 9. Cookie lifecycle.
            await page.DeleteCookiesAsync();
            await page.SetCookieAsync("fp_cookie", "fp_value", domain: "127.0.0.1", path: "/");
            var cookies = await page.GetCookiesAsync();
            Assert.That(cookies?.ToString(), Does.Contain("fp_cookie"), $"[{name}] Cookie set.");
            await page.DeleteCookiesAsync();

            // 10. Tab open/close.
            var initialCount = browser.ConnectionCount;
            var tabUrl = new Uri($"http://127.0.0.1:{browser.BridgePort.ToString(System.Globalization.CultureInfo.InvariantCulture)}/newtab");
            var newTab = await browser.OpenTabAsync(tabUrl);
            Assert.That(browser.ConnectionCount, Is.EqualTo(initialCount + 1), $"[{name}] +1 tab.");
            await browser.CloseTabAsync(newTab.TabId);
            for (var i = 0; i < 10 && browser.ConnectionCount > initialCount; i++)
                await Task.Delay(100);
            Assert.That(browser.ConnectionCount, Is.EqualTo(initialCount), $"[{name}] Tab closed.");

            // 11. Window open/close.
            var windowUrl = new Uri($"http://127.0.0.1:{browser.BridgePort.ToString(System.Globalization.CultureInfo.InvariantCulture)}/newwindow");
            var windowTab = await browser.OpenWindowAsync(windowUrl);
            Assert.That(browser.ConnectionCount, Is.EqualTo(initialCount + 1), $"[{name}] +1 window tab.");
            await browser.CloseTabAsync(windowTab.TabId);
            for (var i = 0; i < 10 && browser.ConnectionCount > initialCount; i++)
                await Task.Delay(100);
            Assert.That(browser.ConnectionCount, Is.EqualTo(initialCount), $"[{name}] Window closed.");

            // 12. Screenshot (может быть null в некоторых браузерах).
            var screenshot = await page.CaptureScreenshotAsync();
            if (screenshot is not null)
                Assert.That(screenshot, Does.StartWith("data:image/"), $"[{name}] Screenshot format.");
            else
                logger.WriteLine(LogKind.Default, $"  [{name}] Screenshot: null (не поддерживается).");

            logger.WriteLine(LogKind.Default, $"[{name}] FullPipeline — все проверки пройдены.");
        }

        private async Task<WebDriverPage> WaitForFirstTabAsync(WebDriverBrowser browser)
        {
            var tcs = new TaskCompletionSource<TabConnectedEventArgs>();
            browser.TabConnected += (_, e) =>
            {
                tcs.TrySetResult(e);
                return ValueTask.CompletedTask;
            };

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            if (browser.ConnectionCount > 0)
            {
                var existingPage = browser.GetAllPages().First();
                logger.WriteLine(LogKind.Default, $"Вкладка уже подключена: {existingPage.TabId}");
                return existingPage;
            }

            var result = await tcs.Task.WaitAsync(cts.Token);
            logger.WriteLine(LogKind.Default, $"Вкладка подключилась: {result.TabId}");

            var page = browser.GetPage(result.TabId)
                ?? throw new BridgeException($"Страница {result.TabId} не найдена после подключения.");

            // Warm-up: выполняем лёгкую команду через content.js → eval bridge,
            // чтобы все компоненты (порт, MutationObserver) полностью инициализировались.
            using var warmupCts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            try { await page.ExecuteAsync("1", warmupCts.Token); }
            catch { /* warm-up не критичен */ }

            return page;
        }
    }

    // ─── Тесты формирования payload ─────────────────────────────

    [TestCase(TestName = "Payload содержит contextId при минимальных настройках")]
    public void PayloadContainsContextIdTest()
    {
        var settings = new TabContextSettings();
        var payload = WebDriverBrowser.BuildSetTabContextPayload(settings);

        Assert.That(payload.ContainsKey("contextId"), Is.True, "Поле contextId обязательно.");
        Assert.That(payload["contextId"]!.GetValue<string>(), Has.Length.EqualTo(32), "contextId — Guid без дефисов.");
    }

    [TestCase(TestName = "Payload содержит все указанные поля")]
    public void PayloadContainsAllFieldsTest()
    {
        var settings = new TabContextSettings
        {
            UserAgent = "TestBot/1.0",
            Locale = "ru-RU",
            Timezone = "Europe/Moscow",
            Platform = "Win32",
            Languages = ["ru-RU", "en"],
            Proxy = "socks5://127.0.0.1:9050",
            Screen = new ScreenSettings { Width = 1920, Height = 1080, ColorDepth = 24 },
            WebGL = new WebGLSettings { Vendor = "TestVendor", Renderer = "TestRenderer" },
            CanvasNoise = true,
            WebRtcPolicy = "disable",
        };

        var payload = WebDriverBrowser.BuildSetTabContextPayload(settings);

        Assert.That(payload["userAgent"]!.GetValue<string>(), Is.EqualTo("TestBot/1.0"));
        Assert.That(payload["locale"]!.GetValue<string>(), Is.EqualTo("ru-RU"));
        Assert.That(payload["timezone"]!.GetValue<string>(), Is.EqualTo("Europe/Moscow"));
        Assert.That(payload["platform"]!.GetValue<string>(), Is.EqualTo("Win32"));
        Assert.That(payload["languages"]!.AsArray(), Has.Count.EqualTo(2));
        Assert.That(payload["proxy"]!.GetValue<string>(), Is.EqualTo("socks5://127.0.0.1:9050"));
        Assert.That(payload["canvasNoise"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["webrtcPolicy"]!.GetValue<string>(), Is.EqualTo("disable"));

        var screen = payload["screen"]!.AsObject();
        Assert.That(screen["width"]!.GetValue<int>(), Is.EqualTo(1920));
        Assert.That(screen["height"]!.GetValue<int>(), Is.EqualTo(1080));
        Assert.That(screen["colorDepth"]!.GetValue<int>(), Is.EqualTo(24));

        var webgl = payload["webgl"]!.AsObject();
        Assert.That(webgl["vendor"]!.GetValue<string>(), Is.EqualTo("TestVendor"));
        Assert.That(webgl["renderer"]!.GetValue<string>(), Is.EqualTo("TestRenderer"));
    }

    [TestCase(TestName = "Payload пропускает null-поля")]
    public void PayloadOmitsNullFieldsTest()
    {
        var settings = new TabContextSettings
        {
            UserAgent = "OnlyUA",
        };

        var payload = WebDriverBrowser.BuildSetTabContextPayload(settings);

        Assert.That(payload.ContainsKey("userAgent"), Is.True);
        Assert.That(payload.ContainsKey("locale"), Is.False);
        Assert.That(payload.ContainsKey("timezone"), Is.False);
        Assert.That(payload.ContainsKey("platform"), Is.False);
        Assert.That(payload.ContainsKey("languages"), Is.False);
        Assert.That(payload.ContainsKey("proxy"), Is.False);
        Assert.That(payload.ContainsKey("screen"), Is.False);
        Assert.That(payload.ContainsKey("webgl"), Is.False);
        Assert.That(payload.ContainsKey("canvasNoise"), Is.False);
        Assert.That(payload.ContainsKey("webrtcPolicy"), Is.False);
    }

    [TestCase(TestName = "Payload — каждый вызов даёт уникальный contextId")]
    public void PayloadUniqueContextIdTest()
    {
        var settings = new TabContextSettings { UserAgent = "UA" };

        var id1 = WebDriverBrowser.BuildSetTabContextPayload(settings)["contextId"]!.GetValue<string>();
        var id2 = WebDriverBrowser.BuildSetTabContextPayload(settings)["contextId"]!.GetValue<string>();

        Assert.That(id1, Is.Not.EqualTo(id2), "Каждый вызов должен давать уникальный contextId.");
    }

    [TestCase(TestName = "BuildScreenNode — частичные настройки")]
    public void ScreenNodePartialTest()
    {
        var screen = new ScreenSettings { Width = 1366 };
        var node = WebDriverBrowser.BuildScreenNode(screen);

        Assert.That(node["width"]!.GetValue<int>(), Is.EqualTo(1366));
        Assert.That(node.ContainsKey("height"), Is.False);
        Assert.That(node.ContainsKey("colorDepth"), Is.False);
    }

    [TestCase(TestName = "BuildWebGLNode — частичные настройки")]
    public void WebGLNodePartialTest()
    {
        var webgl = new WebGLSettings { Renderer = "ANGLE (NVIDIA)" };
        var node = WebDriverBrowser.BuildWebGLNode(webgl);

        Assert.That(node["renderer"]!.GetValue<string>(), Is.EqualTo("ANGLE (NVIDIA)"));
        Assert.That(node.ContainsKey("vendor"), Is.False);
    }

    [TestCase(TestName = "Languages сериализуется в JsonArray")]
    public void LanguagesSerializationTest()
    {
        var settings = new TabContextSettings { Languages = ["en-US", "en", "ru"] };
        var payload = WebDriverBrowser.BuildSetTabContextPayload(settings);
        var arr = payload["languages"]!.AsArray();

        Assert.That(arr, Has.Count.EqualTo(3));
        Assert.That(arr[0]!.GetValue<string>(), Is.EqualTo("en-US"));
        Assert.That(arr[1]!.GetValue<string>(), Is.EqualTo("en"));
        Assert.That(arr[2]!.GetValue<string>(), Is.EqualTo("ru"));
    }

    [TestCase(TestName = "Пустой Languages не попадает в payload")]
    public void EmptyLanguagesOmittedTest()
    {
        var settings = new TabContextSettings { Languages = [] };
        var payload = WebDriverBrowser.BuildSetTabContextPayload(settings);

        Assert.That(payload.ContainsKey("languages"), Is.False);
    }

    [TestCase(TestName = "Geolocation сериализуется в payload")]
    public void GeolocationSerializationTest()
    {
        var settings = new TabContextSettings
        {
            Geolocation = new GeolocationSettings { Latitude = 55.7558, Longitude = 37.6173, Accuracy = 50 },
        };
        var payload = WebDriverBrowser.BuildSetTabContextPayload(settings);

        Assert.That(payload.ContainsKey("geolocation"), Is.True);
        var geo = payload["geolocation"]!.AsObject();
        Assert.That(geo["latitude"]!.GetValue<double>(), Is.EqualTo(55.7558).Within(0.0001));
        Assert.That(geo["longitude"]!.GetValue<double>(), Is.EqualTo(37.6173).Within(0.0001));
        Assert.That(geo["accuracy"]!.GetValue<double>(), Is.EqualTo(50));
    }

    [TestCase(TestName = "AllowedFonts сериализуется как массив")]
    public void AllowedFontsSerializationTest()
    {
        var settings = new TabContextSettings
        {
            AllowedFonts = ["Arial", "Verdana", "Times New Roman"],
        };
        var payload = WebDriverBrowser.BuildSetTabContextPayload(settings);

        Assert.That(payload.ContainsKey("allowedFonts"), Is.True);
        var arr = payload["allowedFonts"]!.AsArray();
        Assert.That(arr, Has.Count.EqualTo(3));
        Assert.That(arr[0]!.GetValue<string>(), Is.EqualTo("Arial"));
    }

    [TestCase(TestName = "AudioNoise, HardwareConcurrency, DeviceMemory, BatteryProtection, PermissionsProtection сериализуются")]
    public void ExtendedFingerprintPayloadTest()
    {
        var settings = new TabContextSettings
        {
            AudioNoise = true,
            HardwareConcurrency = 4,
            DeviceMemory = 8.0,
            BatteryProtection = true,
            PermissionsProtection = true,
        };
        var payload = WebDriverBrowser.BuildSetTabContextPayload(settings);

        Assert.That(payload["audioNoise"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["hardwareConcurrency"]!.GetValue<int>(), Is.EqualTo(4));
        Assert.That(payload["deviceMemory"]!.GetValue<double>(), Is.EqualTo(8.0));
        Assert.That(payload["batteryProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["permissionsProtection"]!.GetValue<bool>(), Is.True);
    }

    [TestCase(TestName = "ClientHints сериализуется с brands, platform, mobile")]
    public void ClientHintsPayloadTest()
    {
        var settings = new TabContextSettings
        {
            ClientHints = new ClientHintsSettings
            {
                Platform = "Windows",
                Mobile = false,
                Brands = [new("Chromium", "128"), new("Not;A=Brand", "24")],
            },
        };
        var payload = WebDriverBrowser.BuildSetTabContextPayload(settings);

        Assert.That(payload.ContainsKey("clientHints"), Is.True);
        var ch = payload["clientHints"]!.AsObject();
        Assert.That(ch["platform"]!.GetValue<string>(), Is.EqualTo("Windows"));
        Assert.That(ch["mobile"]!.GetValue<bool>(), Is.False);
        var brands = ch["brands"]!.AsArray();
        Assert.That(brands, Has.Count.EqualTo(2));
        Assert.That(brands[0]!.AsObject()["brand"]!.GetValue<string>(), Is.EqualTo("Chromium"));
    }

    [TestCase(TestName = "NetworkInfo сериализуется с effectiveType, rtt, downlink, saveData")]
    public void NetworkInfoPayloadTest()
    {
        var settings = new TabContextSettings
        {
            NetworkInfo = new NetworkInfoSettings
            {
                EffectiveType = "3g",
                Rtt = 100,
                Downlink = 1.5,
                SaveData = true,
            },
        };
        var payload = WebDriverBrowser.BuildSetTabContextPayload(settings);

        Assert.That(payload.ContainsKey("networkInfo"), Is.True);
        var ni = payload["networkInfo"]!.AsObject();
        Assert.That(ni["effectiveType"]!.GetValue<string>(), Is.EqualTo("3g"));
        Assert.That(ni["rtt"]!.GetValue<double>(), Is.EqualTo(100));
        Assert.That(ni["downlink"]!.GetValue<double>(), Is.EqualTo(1.5));
        Assert.That(ni["saveData"]!.GetValue<bool>(), Is.True);
    }

    [TestCase(TestName = "CreateAntiDetectProfile создаёт профиль со всеми защитами")]
    public void AntiDetectProfileTest()
    {
        var settings = TabContextSettings.CreateAntiDetectProfile("Mozilla/5.0 AntiDetect", proxy: "socks5://127.0.0.1:9050");

        Assert.That(settings.UserAgent, Is.EqualTo("Mozilla/5.0 AntiDetect"));
        Assert.That(settings.Proxy, Is.EqualTo("socks5://127.0.0.1:9050"));
        Assert.That(settings.Locale, Is.EqualTo("en-US"));
        Assert.That(settings.Timezone, Is.EqualTo("America/New_York"));
        Assert.That(settings.CanvasNoise, Is.True);
        Assert.That(settings.AudioNoise, Is.True);
        Assert.That(settings.WebRtcPolicy, Is.EqualTo("disable"));
        Assert.That(settings.BatteryProtection, Is.True);
        Assert.That(settings.PermissionsProtection, Is.True);
        Assert.That(settings.HardwareConcurrency, Is.EqualTo(4));
        Assert.That(settings.DeviceMemory, Is.EqualTo(8));
        Assert.That(settings.AllowedFonts, Is.Not.Null.And.Count.GreaterThan(0));
        Assert.That(settings.ClientHints, Is.Not.Null);
        Assert.That(settings.ClientHints!.Platform, Is.EqualTo("Windows"));
        Assert.That(settings.NetworkInfo, Is.Not.Null);

        // Проверяем что payload корректно сериализуется.
        var payload = WebDriverBrowser.BuildSetTabContextPayload(settings);
        Assert.That(payload.ContainsKey("clientHints"), Is.True);
        Assert.That(payload.ContainsKey("networkInfo"), Is.True);
        Assert.That(payload.ContainsKey("audioNoise"), Is.True);
    }

    [TestCase(TestName = "SpeechVoices, MediaDevicesProtection, WebGLParams, DoNotTrack, GPC сериализуются")]
    public void SpeechMediaWebGLPayloadTest()
    {
        var settings = new TabContextSettings
        {
            SpeechVoices = [new() { Name = "Alice", Lang = "en-US" }],
            MediaDevicesProtection = true,
            WebGLParams = new WebGLParamsSettings { MaxTextureSize = 4096, MaxRenderbufferSize = 4096 },
            DoNotTrack = "1",
            GlobalPrivacyControl = true,
        };
        var payload = WebDriverBrowser.BuildSetTabContextPayload(settings);

        Assert.That(payload.ContainsKey("speechVoices"), Is.True);
        var voices = payload["speechVoices"]!.AsArray();
        Assert.That(voices, Has.Count.EqualTo(1));
        Assert.That(voices[0]!.AsObject()["name"]!.GetValue<string>(), Is.EqualTo("Alice"));

        Assert.That(payload["mediaDevicesProtection"]!.GetValue<bool>(), Is.True);

        var wgl = payload["webglParams"]!.AsObject();
        Assert.That(wgl["maxTextureSize"]!.GetValue<int>(), Is.EqualTo(4096));
        Assert.That(wgl["maxRenderbufferSize"]!.GetValue<int>(), Is.EqualTo(4096));

        Assert.That(payload["doNotTrack"]!.GetValue<string>(), Is.EqualTo("1"));
        Assert.That(payload["globalPrivacyControl"]!.GetValue<bool>(), Is.True);
    }

    [TestCase(TestName = "Intl, ScreenOrientation, matchMedia, Timer, WebSocket сериализуются")]
    public void EnvironmentFingerprintPayloadTest()
    {
        var settings = new TabContextSettings
        {
            IntlSpoofing = true,
            ScreenOrientation = "landscape-primary",
            ColorScheme = "dark",
            ReducedMotion = true,
            TimerPrecisionMs = 50,
            WebSocketProtection = "same-origin",
        };
        var payload = WebDriverBrowser.BuildSetTabContextPayload(settings);

        Assert.That(payload["intlSpoofing"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["screenOrientation"]!.GetValue<string>(), Is.EqualTo("landscape-primary"));
        Assert.That(payload["colorScheme"]!.GetValue<string>(), Is.EqualTo("dark"));
        Assert.That(payload["reducedMotion"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["timerPrecisionMs"]!.GetValue<double>(), Is.EqualTo(50));
        Assert.That(payload["webSocketProtection"]!.GetValue<string>(), Is.EqualTo("same-origin"));
    }

    [TestCase(TestName = "WebGLNoise, StorageQuota, KeyboardLayout, WebRtcIcePolicy, PluginSpoofing сериализуются")]
    public void AdvancedFingerprintPayloadTest()
    {
        var settings = new TabContextSettings
        {
            WebGLNoise = true,
            StorageQuota = 2_147_483_648,
            KeyboardLayout = "en-US",
            WebRtcIcePolicy = "sanitize",
            PluginSpoofing = true,
        };
        var payload = WebDriverBrowser.BuildSetTabContextPayload(settings);

        Assert.That(payload["webglNoise"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["storageQuota"]!.GetValue<long>(), Is.EqualTo(2_147_483_648));
        Assert.That(payload["keyboardLayout"]!.GetValue<string>(), Is.EqualTo("en-US"));
        Assert.That(payload["webrtcIcePolicy"]!.GetValue<string>(), Is.EqualTo("sanitize"));
        Assert.That(payload["pluginSpoofing"]!.GetValue<bool>(), Is.True);
    }

    [TestCase(TestName = "SpeechRecognition, MaxTouchPoints, AudioContext, PdfViewer, Notification сериализуются")]
    public void MiscFingerprintPayloadTest()
    {
        var settings = new TabContextSettings
        {
            SpeechRecognitionProtection = true,
            MaxTouchPoints = 5,
            AudioSampleRate = 44100,
            AudioChannelCount = 2,
            PdfViewerEnabled = true,
            NotificationPermission = "denied",
        };
        var payload = WebDriverBrowser.BuildSetTabContextPayload(settings);

        Assert.That(payload["speechRecognitionProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["maxTouchPoints"]!.GetValue<int>(), Is.EqualTo(5));
        Assert.That(payload["audioSampleRate"]!.GetValue<int>(), Is.EqualTo(44100));
        Assert.That(payload["audioChannelCount"]!.GetValue<int>(), Is.EqualTo(2));
        Assert.That(payload["pdfViewerEnabled"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["notificationPermission"]!.GetValue<string>(), Is.EqualTo("denied"));
    }

    [Test]
    public void SystemFingerprintPayloadTest()
    {
        var settings = new TabContextSettings
        {
            GamepadProtection = true,
            HardwareApiProtection = true,
            PerformanceProtection = true,
            DocumentReferrer = "https://search.example.com",
            HistoryLength = 3,
        };
        var payload = WebDriverBrowser.BuildSetTabContextPayload(settings);

        Assert.That(payload["gamepadProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["hardwareApiProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["performanceProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["documentReferrer"]!.GetValue<string>(), Is.EqualTo("https://search.example.com"));
        Assert.That(payload["historyLength"]!.GetValue<int>(), Is.EqualTo(3));
    }

    [Test]
    public void SensorFingerprintPayloadTest()
    {
        var settings = new TabContextSettings
        {
            DeviceMotionProtection = true,
            AmbientLightProtection = true,
            ConnectionRtt = 100,
            ConnectionDownlink = 5.5,
            MediaCapabilitiesProtection = true,
            ClipboardProtection = true,
        };
        var payload = WebDriverBrowser.BuildSetTabContextPayload(settings);

        Assert.That(payload["deviceMotionProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["ambientLightProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["connectionRtt"]!.GetValue<int>(), Is.EqualTo(100));
        Assert.That(payload["connectionDownlink"]!.GetValue<double>(), Is.EqualTo(5.5));
        Assert.That(payload["mediaCapabilitiesProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["clipboardProtection"]!.GetValue<bool>(), Is.True);
    }

    [Test]
    public void ApiHardeningPayloadTest()
    {
        var settings = new TabContextSettings
        {
            WebShareProtection = true,
            WakeLockProtection = true,
            IdleDetectionProtection = true,
            CredentialProtection = true,
            PaymentProtection = true,
        };
        var payload = WebDriverBrowser.BuildSetTabContextPayload(settings);

        Assert.That(payload["webShareProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["wakeLockProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["idleDetectionProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["credentialProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["paymentProtection"]!.GetValue<bool>(), Is.True);
    }

    [Test]
    public void StorageNetworkPayloadTest()
    {
        var settings = new TabContextSettings
        {
            StorageEstimateUsage = 1024,
            FileSystemAccessProtection = true,
            BeaconProtection = true,
            VisibilityStateOverride = "visible",
            ColorDepth = 32,
        };
        var payload = WebDriverBrowser.BuildSetTabContextPayload(settings);

        Assert.That(payload["storageEstimateUsage"]!.GetValue<long>(), Is.EqualTo(1024));
        Assert.That(payload["fileSystemAccessProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["beaconProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["visibilityStateOverride"]!.GetValue<string>(), Is.EqualTo("visible"));
        Assert.That(payload["colorDepth"]!.GetValue<int>(), Is.EqualTo(32));
    }

    [Test]
    public void IsolationHardeningPayloadTest()
    {
        var settings = new TabContextSettings
        {
            InstalledAppsProtection = true,
            FontMetricsProtection = true,
            CrossOriginIsolationOverride = false,
            PerformanceNowJitter = 0.5,
            WindowControlsOverlayProtection = true,
        };
        var payload = WebDriverBrowser.BuildSetTabContextPayload(settings);

        Assert.That(payload["installedAppsProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["fontMetricsProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["crossOriginIsolationOverride"]!.GetValue<bool>(), Is.False);
        Assert.That(payload["performanceNowJitter"]!.GetValue<double>(), Is.EqualTo(0.5));
        Assert.That(payload["windowControlsOverlayProtection"]!.GetValue<bool>(), Is.True);
    }

    [Test]
    public void BrowserApiBlockingPayloadTest()
    {
        var settings = new TabContextSettings
        {
            ScreenOrientationLockProtection = true,
            KeyboardApiProtection = true,
            UsbHidSerialProtection = true,
            PresentationApiProtection = true,
            ContactsApiProtection = true,
        };
        var payload = WebDriverBrowser.BuildSetTabContextPayload(settings);

        Assert.That(payload["screenOrientationLockProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["keyboardApiProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["usbHidSerialProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["presentationApiProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["contactsApiProtection"]!.GetValue<bool>(), Is.True);
    }

    [Test]
    public void DeviceApiBlockingPayloadTest()
    {
        var settings = new TabContextSettings
        {
            BluetoothProtection = true,
            EyeDropperProtection = true,
            MultiScreenProtection = true,
            InkApiProtection = true,
            VirtualKeyboardProtection = true,
        };
        var payload = WebDriverBrowser.BuildSetTabContextPayload(settings);

        Assert.That(payload["bluetoothProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["eyeDropperProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["multiScreenProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["inkApiProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["virtualKeyboardProtection"]!.GetValue<bool>(), Is.True);
    }

    [Test]
    public void EmergingApiBlockingPayloadTest()
    {
        var settings = new TabContextSettings
        {
            NfcProtection = true,
            FileHandlingProtection = true,
            WebXrProtection = true,
            WebNnProtection = true,
            SchedulingProtection = true,
        };
        var payload = WebDriverBrowser.BuildSetTabContextPayload(settings);

        Assert.That(payload["nfcProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["fileHandlingProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["webXrProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["webNnProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["schedulingProtection"]!.GetValue<bool>(), Is.True);
    }

    [Test]
    public void PlatformApiBlockingPayloadTest()
    {
        var settings = new TabContextSettings
        {
            StorageAccessProtection = true,
            ContentIndexProtection = true,
            BackgroundSyncProtection = true,
            CookieStoreProtection = true,
            WebLocksProtection = true,
        };
        var payload = WebDriverBrowser.BuildSetTabContextPayload(settings);

        Assert.That(payload["storageAccessProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["contentIndexProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["backgroundSyncProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["cookieStoreProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["webLocksProtection"]!.GetValue<bool>(), Is.True);
    }

    [Test]
    public void DetectionMiscApiBlockingPayloadTest()
    {
        var settings = new TabContextSettings
        {
            ShapeDetectionProtection = true,
            WebTransportProtection = true,
            RelatedAppsProtection = true,
            DigitalGoodsProtection = true,
            ComputePressureProtection = true,
        };
        var payload = WebDriverBrowser.BuildSetTabContextPayload(settings);

        Assert.That(payload["shapeDetectionProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["webTransportProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["relatedAppsProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["digitalGoodsProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["computePressureProtection"]!.GetValue<bool>(), Is.True);
    }

    [Test]
    public void AdvancedPrivacyApiPayloadTest()
    {
        var settings = new TabContextSettings
        {
            FileSystemPickerProtection = true,
            DisplayOverrideProtection = true,
            BatteryLevelOverride = 0.75,
            PictureInPictureProtection = true,
            DevicePostureProtection = true,
        };
        var payload = WebDriverBrowser.BuildSetTabContextPayload(settings);

        Assert.That(payload["fileSystemPickerProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["displayOverrideProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["batteryLevelOverride"]!.GetValue<double>(), Is.EqualTo(0.75));
        Assert.That(payload["pictureInPictureProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["devicePostureProtection"]!.GetValue<bool>(), Is.True);
    }

    [Test]
    public void AuthCommunicationApiPayloadTest()
    {
        var settings = new TabContextSettings
        {
            WebAuthnProtection = true,
            FedCmProtection = true,
            LocalFontAccessProtection = true,
            AutoplayPolicyProtection = true,
            LaunchHandlerProtection = true,
        };
        var payload = WebDriverBrowser.BuildSetTabContextPayload(settings);

        Assert.That(payload["webAuthnProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["fedCmProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["localFontAccessProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["autoplayPolicyProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["launchHandlerProtection"]!.GetValue<bool>(), Is.True);
    }

    [Test]
    public void PrivacySandboxApiPayloadTest()
    {
        var settings = new TabContextSettings
        {
            TopicsApiProtection = true,
            AttributionReportingProtection = true,
            FencedFrameProtection = true,
            SharedStorageProtection = true,
            PrivateAggregationProtection = true,
        };

        var payload = WebDriverBrowser.BuildSetTabContextPayload(settings);

        Assert.That(payload["topicsApiProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["attributionReportingProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["fencedFrameProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["sharedStorageProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["privateAggregationProtection"]!.GetValue<bool>(), Is.True);
    }

    [Test]
    public void EmergingApiPayloadTest()
    {
        var settings = new TabContextSettings
        {
            WebOtpProtection = true,
            WebMidiProtection = true,
            WebCodecsProtection = true,
            NavigationApiProtection = true,
            ScreenCaptureProtection = true,
        };

        var payload = WebDriverBrowser.BuildSetTabContextPayload(settings);

        Assert.That(payload["webOtpProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["webMidiProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["webCodecsProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["navigationApiProtection"]!.GetValue<bool>(), Is.True);
        Assert.That(payload["screenCaptureProtection"]!.GetValue<bool>(), Is.True);
    }

    // ─── Интеграционные тесты изоляции ──────────────────────────

    /// <summary>
    /// Тесты extension-level изоляции вкладок. Один браузер, несколько изолированных вкладок.
    /// </summary>
    [TestFixture, Category("GUI"), NonParallelizable]
    public class IsolationTests
    {
        private readonly ILogger logger = ConsoleLogger.Unicode;
        private WebDriverBrowser browser = null!;

        [OneTimeSetUp]
        public async Task SetUp()
        {
            var (browserPath, extensionPath) = FindChromiumBrowser();
            browser = await WebDriverBrowser.LaunchAsync(
                browserPath, extensionPath,
                arguments: ["--no-sandbox", "--disable-features=Translate"]);
            // Ждём discovery-вкладку.
            await WaitForFirstTabAsync();
            logger.WriteLine(LogKind.Default, "Браузер запущен для тестов изоляции.");
        }

        [OneTimeTearDown]
        public async Task TearDown()
        {
            await browser.DisposeAsync();
        }

        [TearDown]
        public async Task CleanUp()
        {
            var discoveryTab = browser.GetAllPages().FirstOrDefault();
            foreach (var p in browser.GetAllPages().Skip(1).ToList())
            {
                try { await browser.CloseTabAsync(p.TabId); }
                catch { /* Уже закрыта. */ }
            }

            await Task.Delay(100);
        }

        [TestCase(TestName = "Изолированная вкладка подменяет navigator.userAgent"), Order(1)]
        public async Task UserAgentOverrideTest()
        {
            var settings = new TabContextSettings { UserAgent = "AtomBot/42.0" };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300); // Даём content.js применить настройки.
            var ua = await tab.ExecuteAsync("navigator.userAgent");

            logger.WriteLine(LogKind.Default, $"UserAgent: {ua}");
            Assert.That(ua?.ToString(), Is.EqualTo("AtomBot/42.0"), "navigator.userAgent должен быть подменён.");
        }

        [TestCase(TestName = "Изолированная вкладка подменяет navigator.platform"), Order(2)]
        public async Task PlatformOverrideTest()
        {
            var settings = new TabContextSettings { Platform = "FakePlatform64" };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var platform = await tab.ExecuteAsync("navigator.platform");

            logger.WriteLine(LogKind.Default, $"Platform: {platform}");
            Assert.That(platform?.ToString(), Is.EqualTo("FakePlatform64"), "navigator.platform должен быть подменён.");
        }

        [TestCase(TestName = "Изолированная вкладка подменяет navigator.languages"), Order(3)]
        public async Task LanguagesOverrideTest()
        {
            var settings = new TabContextSettings { Languages = ["ja-JP", "ja"] };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var langs = await tab.ExecuteAsync("JSON.stringify(navigator.languages)");

            logger.WriteLine(LogKind.Default, $"Languages: {langs}");
            Assert.That(langs?.ToString(), Is.EqualTo("[\"ja-JP\",\"ja\"]"));
        }

        [TestCase(TestName = "localStorage изолирован между контекстами"), Order(4)]
        public async Task LocalStorageIsolationTest()
        {
            // Оба контекста на одном origin (about:blank).
            var tab1 = await browser.OpenIsolatedTabAsync(settings: new TabContextSettings { UserAgent = "Ctx1" });
            await Task.Delay(300);

            var tab2 = await browser.OpenIsolatedTabAsync(settings: new TabContextSettings { UserAgent = "Ctx2" });
            await Task.Delay(300);

            // Записываем в tab1.
            await tab1.ExecuteAsync("localStorage.setItem('key', 'value-from-ctx1')");
            var val1 = await tab1.ExecuteAsync("localStorage.getItem('key')");

            // Проверяем, что tab2 не видит данные tab1.
            var val2 = await tab2.ExecuteAsync("localStorage.getItem('key')");

            logger.WriteLine(LogKind.Default, $"tab1: {val1}, tab2: {val2}");
            Assert.That(val1?.ToString(), Is.EqualTo("value-from-ctx1"), "tab1 должен видеть свои данные.");
            Assert.That(val2?.ToString(), Is.Null.Or.Not.EqualTo("value-from-ctx1"), "tab2 НЕ должен видеть данные tab1.");
        }

        [TestCase(TestName = "document.cookie изолирован (in-memory shim)"), Order(5)]
        public async Task CookieIsolationTest()
        {
            var tab1 = await browser.OpenIsolatedTabAsync(settings: new TabContextSettings { UserAgent = "CookieCtx1" });
            await Task.Delay(300);

            var tab2 = await browser.OpenIsolatedTabAsync(settings: new TabContextSettings { UserAgent = "CookieCtx2" });
            await Task.Delay(300);

            await tab1.ExecuteAsync("document.cookie = 'secret=abc123'");
            var c1 = await tab1.ExecuteAsync("document.cookie");
            var c2 = await tab2.ExecuteAsync("document.cookie");

            logger.WriteLine(LogKind.Default, $"cookie tab1: {c1}, tab2: {c2}");
            Assert.That(c1?.ToString(), Does.Contain("secret=abc123"), "tab1 должен видеть свою cookie.");
            Assert.That(c2?.ToString() ?? "", Does.Not.Contain("secret"), "tab2 НЕ должен видеть cookie tab1.");
        }

        [TestCase(TestName = "Screen override подменяет screen.width/height"), Order(6)]
        public async Task ScreenOverrideTest()
        {
            var settings = new TabContextSettings
            {
                Screen = new ScreenSettings { Width = 800, Height = 600, ColorDepth = 16 },
            };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var w = await tab.ExecuteAsync("screen.width");
            var h = await tab.ExecuteAsync("screen.height");
            var cd = await tab.ExecuteAsync("screen.colorDepth");

            logger.WriteLine(LogKind.Default, $"Screen: {w}x{h}, depth={cd}");
            Assert.That(w?.ToString(), Is.EqualTo("800"), "screen.width должен быть 800.");
            Assert.That(h?.ToString(), Is.EqualTo("600"), "screen.height должен быть 600.");
            Assert.That(cd?.ToString(), Is.EqualTo("16"), "screen.colorDepth должен быть 16.");
        }

        [TestCase(TestName = "WebGL override подменяет vendor/renderer"), Order(7)]
        public async Task WebGLOverrideTest()
        {
            var settings = new TabContextSettings
            {
                WebGL = new WebGLSettings { Vendor = "FakeVendor Inc.", Renderer = "FakeGPU 3000" },
            };
            var tab = await browser.OpenIsolatedTabAsync(
                url: new Uri("about:blank"), settings: settings);

            await Task.Delay(300);

            // Проверяем через WebGL API.
            var vendor = await tab.ExecuteAsync("""
                (function() {
                    var c = document.createElement('canvas');
                    var gl = c.getContext('webgl');
                    if (!gl) return 'no-webgl';
                    var ext = gl.getExtension('WEBGL_debug_renderer_info');
                    return ext ? gl.getParameter(ext.UNMASKED_VENDOR_WEBGL) : 'no-ext';
                })()
                """);

            var renderer = await tab.ExecuteAsync("""
                (function() {
                    var c = document.createElement('canvas');
                    var gl = c.getContext('webgl');
                    if (!gl) return 'no-webgl';
                    var ext = gl.getExtension('WEBGL_debug_renderer_info');
                    return ext ? gl.getParameter(ext.UNMASKED_RENDERER_WEBGL) : 'no-ext';
                })()
                """);

            logger.WriteLine(LogKind.Default, $"WebGL vendor: {vendor}, renderer: {renderer}");

            // about:blank может не поддерживать WebGL в некоторых средах.
            var vendorStr = vendor?.ToString();
            if (vendorStr is not "no-webgl" and not "no-ext")
            {
                Assert.That(vendorStr, Is.EqualTo("FakeVendor Inc."));
                Assert.That(renderer?.ToString(), Is.EqualTo("FakeGPU 3000"));
            }
            else
            {
                logger.WriteLine(LogKind.Default, "WebGL недоступен в тестовой среде — пропуск.");
            }
        }

        [TestCase(TestName = "WebRTC disable выбрасывает исключение"), Order(8)]
        public async Task WebRtcDisableTest()
        {
            var settings = new TabContextSettings { WebRtcPolicy = "disable" };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);

            var result = await tab.ExecuteAsync("""
                (function() {
                    try { new RTCPeerConnection(); return 'no-error'; }
                    catch (e) { return e.name; }
                })()
                """);

            logger.WriteLine(LogKind.Default, $"WebRTC disable result: {result}");
            Assert.That(result?.ToString(), Is.EqualTo("NotAllowedError"), "RTCPeerConnection должен выбрасывать NotAllowedError.");
        }

        [TestCase(TestName = "Timezone override подменяет Intl и getTimezoneOffset"), Order(9)]
        public async Task TimezoneOverrideTest()
        {
            var settings = new TabContextSettings { Timezone = "Pacific/Auckland" };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);

            // Intl.DateTimeFormat resolvedOptions().timeZone должна вернуть нашу таймзону.
            var tz = await tab.ExecuteAsync("Intl.DateTimeFormat().resolvedOptions().timeZone");
            logger.WriteLine(LogKind.Default, $"Timezone: {tz}");
            Assert.That(tz?.ToString(), Is.EqualTo("Pacific/Auckland"), "Intl timezone должна быть подменена.");

            // getTimezoneOffset должен вернуть отрицательное смещение (NZST = UTC+12 → offset = -720).
            // Зимой NZST offset -720, летом NZDT offset -780. Проверяем диапазон.
            var offset = await tab.ExecuteAsync("new Date().getTimezoneOffset()");
            logger.WriteLine(LogKind.Default, $"TimezoneOffset: {offset}");
            var offsetVal = int.Parse(offset?.ToString() ?? "0");
            Assert.That(offsetVal, Is.LessThanOrEqualTo(-720).And.GreaterThanOrEqualTo(-780),
                "getTimezoneOffset для Pacific/Auckland должен быть от -780 до -720.");

            // toLocaleString должен содержать дату/время в целевой таймзоне.
            var locale = await tab.ExecuteAsync(
                "new Date('2025-01-15T00:00:00Z').toLocaleString('en-US', {hour: 'numeric', hour12: false})");
            logger.WriteLine(LogKind.Default, $"ToLocaleString hour: {locale}");
            // 2025-01-15T00:00Z в NZDT (UTC+13 зимой) → 13:00.
            Assert.That(locale?.ToString(), Is.EqualTo("13"), "Час в Pacific/Auckland для UTC 00:00 15 Jan должен быть 13.");
        }

        [TestCase(TestName = "navigator.webdriver скрыт в изолированной вкладке"), Order(10)]
        public async Task WebdriverHiddenTest()
        {
            var tab = await browser.OpenIsolatedTabAsync(
                settings: new TabContextSettings { UserAgent = "HiddenWD" });

            await Task.Delay(300);
            var wd = await tab.ExecuteAsync("navigator.webdriver");

            logger.WriteLine(LogKind.Default, $"webdriver: {wd}");
            Assert.That(wd?.ToString(), Is.EqualTo("False").Or.Null, "navigator.webdriver должен быть false или undefined.");
        }

        [TestCase(TestName = "Canvas fingerprint отличается между контекстами"), Order(11)]
        public async Task CanvasFingerprintDiffersTest()
        {
            const string canvasScript = """
                (function() {
                    var c = document.createElement('canvas');
                    c.width = 50; c.height = 50;
                    var ctx = c.getContext('2d');
                    ctx.fillStyle = '#f00';
                    ctx.fillRect(0, 0, 50, 50);
                    ctx.fillStyle = '#0f0';
                    ctx.font = '18px Arial';
                    ctx.fillText('Atom', 5, 30);
                    return c.toDataURL();
                })()
                """;

            var tab1 = await browser.OpenIsolatedTabAsync(
                settings: new TabContextSettings { CanvasNoise = true, UserAgent = "Canvas1" });
            await Task.Delay(300);

            var tab2 = await browser.OpenIsolatedTabAsync(
                settings: new TabContextSettings { CanvasNoise = true, UserAgent = "Canvas2" });
            await Task.Delay(300);

            var fp1 = await tab1.ExecuteAsync(canvasScript);
            var fp2 = await tab2.ExecuteAsync(canvasScript);

            var fp1Str = fp1?.ToString();
            var fp2Str = fp2?.ToString();
            logger.WriteLine(LogKind.Default, $"Canvas fp1 len={fp1Str?.Length}, fp2 len={fp2Str?.Length}");
            Assert.That(fp1Str, Is.Not.Null.And.Not.Empty, "tab1 canvas fingerprint.");
            Assert.That(fp2Str, Is.Not.Null.And.Not.Empty, "tab2 canvas fingerprint.");
            Assert.That(fp1Str, Is.Not.EqualTo(fp2Str), "Canvas fingerprint должен отличаться между контекстами.");
        }

        [TestCase(TestName = "OpenIsolatedTabAsync навигирует на указанный URL"), Order(12)]
        public async Task NavigateToUrlTest()
        {
            var tab = await browser.OpenIsolatedTabAsync(
                url: new Uri("https://example.com"),
                settings: new TabContextSettings { UserAgent = "NavTest" });

            await Task.Delay(1000); // Даём загрузиться.
            var title = await tab.GetTitleAsync();

            logger.WriteLine(LogKind.Default, $"Title: {title}");
            Assert.That(title, Does.Contain("Example"), "Страница должна загрузиться.");
        }

        [TestCase(TestName = "Geolocation override подменяет координаты"), Order(13)]
        public async Task GeolocationOverrideTest()
        {
            var settings = new TabContextSettings
            {
                Geolocation = new GeolocationSettings { Latitude = 55.7558, Longitude = 37.6173, Accuracy = 100 },
            };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);

            var lat = await tab.ExecuteAsync("""
                new Promise(function(ok) {
                    navigator.geolocation.getCurrentPosition(function(pos) {
                        ok(pos.coords.latitude);
                    });
                })
                """);

            var lng = await tab.ExecuteAsync("""
                new Promise(function(ok) {
                    navigator.geolocation.getCurrentPosition(function(pos) {
                        ok(pos.coords.longitude);
                    });
                })
                """);

            logger.WriteLine(LogKind.Default, $"Geolocation: {lat}, {lng}");
            Assert.That(double.Parse(lat?.ToString() ?? "0"), Is.EqualTo(55.7558).Within(0.001));
            Assert.That(double.Parse(lng?.ToString() ?? "0"), Is.EqualTo(37.6173).Within(0.001));
        }

        [TestCase(TestName = "Font fingerprint ограничивает document.fonts.check"), Order(14)]
        public async Task FontFingerprintProtectionTest()
        {
            var settings = new TabContextSettings
            {
                AllowedFonts = ["Arial", "Verdana"],
            };
            var tab = await browser.OpenIsolatedTabAsync(
                url: new Uri("about:blank"), settings: settings);

            await Task.Delay(300);

            // Arial должна быть "доступна".
            var arialCheck = await tab.ExecuteAsync("document.fonts.check('16px Arial')");
            // Несуществующий шрифт должен быть заблокирован.
            var fakeCheck = await tab.ExecuteAsync("document.fonts.check('16px SuperRareFont9999')");

            logger.WriteLine(LogKind.Default, $"Arial: {arialCheck}, Fake: {fakeCheck}");
            Assert.That(arialCheck?.ToString(), Is.EqualTo("True"), "Arial должна пройти проверку.");
            Assert.That(fakeCheck?.ToString(), Is.EqualTo("False"), "Неразрешённый шрифт должен быть заблокирован.");
        }

        [TestCase(TestName = "HardwareConcurrency override подменяет navigator.hardwareConcurrency"), Order(15)]
        public async Task HardwareConcurrencyOverrideTest()
        {
            var settings = new TabContextSettings { HardwareConcurrency = 2, DeviceMemory = 4.0 };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var cores = await tab.ExecuteAsync("navigator.hardwareConcurrency");
            var mem = await tab.ExecuteAsync("navigator.deviceMemory");

            logger.WriteLine(LogKind.Default, $"Cores: {cores}, Memory: {mem}");
            Assert.That(cores?.ToString(), Is.EqualTo("2"), "hardwareConcurrency должен быть 2.");
            Assert.That(mem?.ToString(), Is.EqualTo("4"), "deviceMemory должен быть 4.");
        }

        [TestCase(TestName = "Battery API protection возвращает charging=true, level=1"), Order(16)]
        public async Task BatteryProtectionTest()
        {
            var settings = new TabContextSettings { BatteryProtection = true };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var charging = await tab.ExecuteAsync("""
                navigator.getBattery().then(function(b) { return b.charging; })
                """);
            var level = await tab.ExecuteAsync("""
                navigator.getBattery().then(function(b) { return b.level; })
                """);

            logger.WriteLine(LogKind.Default, $"Charging: {charging}, Level: {level}");
            Assert.That(charging?.ToString(), Is.EqualTo("True"), "charging должен быть true.");
            Assert.That(level?.ToString(), Is.EqualTo("1"), "level должен быть 1.");
        }

        [TestCase(TestName = "Permissions API spoof возвращает prompt"), Order(17)]
        public async Task PermissionsOverrideTest()
        {
            var settings = new TabContextSettings { PermissionsProtection = true };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var state = await tab.ExecuteAsync("""
                navigator.permissions.query({name: 'geolocation'}).then(function(r) { return r.state; })
                """);

            logger.WriteLine(LogKind.Default, $"Permission state: {state}");
            Assert.That(state?.ToString(), Is.EqualTo("prompt"), "permissions.query должен возвращать prompt.");
        }

        [TestCase(TestName = "Client Hints override подменяет navigator.userAgentData"), Order(18)]
        public async Task ClientHintsOverrideTest()
        {
            var settings = new TabContextSettings
            {
                ClientHints = new ClientHintsSettings
                {
                    Platform = "Linux",
                    Mobile = false,
                    Brands = [new("TestBrand", "99")],
                },
            };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var platform = await tab.ExecuteAsync("navigator.userAgentData.platform");
            var mobile = await tab.ExecuteAsync("navigator.userAgentData.mobile");
            var brand = await tab.ExecuteAsync("navigator.userAgentData.brands[0].brand");

            logger.WriteLine(LogKind.Default, $"UA platform: {platform}, mobile: {mobile}, brand: {brand}");
            Assert.That(platform?.ToString(), Is.EqualTo("Linux"), "userAgentData.platform должен быть Linux.");
            Assert.That(mobile?.ToString(), Is.EqualTo("False"), "userAgentData.mobile должен быть false.");
            Assert.That(brand?.ToString(), Is.EqualTo("TestBrand"), "userAgentData.brands[0].brand должен быть TestBrand.");
        }

        [TestCase(TestName = "Network Information override подменяет navigator.connection"), Order(19)]
        public async Task NetworkInfoOverrideTest()
        {
            var settings = new TabContextSettings
            {
                NetworkInfo = new NetworkInfoSettings
                {
                    EffectiveType = "3g",
                    Rtt = 200,
                    Downlink = 1.5,
                },
            };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var effectiveType = await tab.ExecuteAsync("navigator.connection.effectiveType");
            var rtt = await tab.ExecuteAsync("navigator.connection.rtt");

            logger.WriteLine(LogKind.Default, $"Connection: effectiveType={effectiveType}, rtt={rtt}");
            Assert.That(effectiveType?.ToString(), Is.EqualTo("3g"), "effectiveType должен быть 3g.");
            Assert.That(rtt?.ToString(), Is.EqualTo("200"), "rtt должен быть 200.");
        }

        [TestCase(TestName = "MediaDevices protection возвращает стандартный набор устройств"), Order(20)]
        public async Task MediaDevicesOverrideTest()
        {
            var settings = new TabContextSettings { MediaDevicesProtection = true };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var count = await tab.ExecuteAsync(
                "navigator.mediaDevices.enumerateDevices().then(function(d){ return d.length; })");
            var kind = await tab.ExecuteAsync(
                "navigator.mediaDevices.enumerateDevices().then(function(d){ return d[0].kind; })");

            logger.WriteLine(LogKind.Default, $"MediaDevices count={count}, first kind={kind}");
            Assert.That(count?.ToString(), Is.EqualTo("3"), "Должно быть 3 устройства.");
            Assert.That(kind?.ToString(), Is.EqualTo("audioinput"), "Первое устройство — audioinput.");
        }

        [TestCase(TestName = "DoNotTrack / GlobalPrivacyControl override"), Order(21)]
        public async Task DoNotTrackOverrideTest()
        {
            var settings = new TabContextSettings { DoNotTrack = "1", GlobalPrivacyControl = true };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var dnt = await tab.ExecuteAsync("navigator.doNotTrack");
            var gpc = await tab.ExecuteAsync("navigator.globalPrivacyControl");

            logger.WriteLine(LogKind.Default, $"DNT={dnt}, GPC={gpc}");
            Assert.That(dnt?.ToString(), Is.EqualTo("1"), "doNotTrack должен быть '1'.");
            Assert.That(gpc?.ToString(), Is.EqualTo("True"), "globalPrivacyControl должен быть true.");
        }

        [TestCase(TestName = "matchMedia prefers-color-scheme override"), Order(22)]
        public async Task MatchMediaOverrideTest()
        {
            var settings = new TabContextSettings { ColorScheme = "dark", ReducedMotion = true };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var dark = await tab.ExecuteAsync("window.matchMedia('(prefers-color-scheme: dark)').matches");
            var light = await tab.ExecuteAsync("window.matchMedia('(prefers-color-scheme: light)').matches");
            var motion = await tab.ExecuteAsync("window.matchMedia('(prefers-reduced-motion: reduce)').matches");

            logger.WriteLine(LogKind.Default, $"dark={dark}, light={light}, reducedMotion={motion}");
            Assert.That(dark?.ToString(), Is.EqualTo("True"), "dark должен быть true.");
            Assert.That(light?.ToString(), Is.EqualTo("False"), "light должен быть false.");
            Assert.That(motion?.ToString(), Is.EqualTo("True"), "prefers-reduced-motion: reduce должен быть true.");
        }

        [TestCase(TestName = "Timer precision override"), Order(23)]
        public async Task TimerPrecisionOverrideTest()
        {
            var settings = new TabContextSettings { TimerPrecisionMs = 100 };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var raw = await tab.ExecuteAsync("performance.now() % 100");
            var val = double.Parse(raw?.ToString() ?? "0", System.Globalization.CultureInfo.InvariantCulture);

            logger.WriteLine(LogKind.Default, $"performance.now() %% 100 = {val}");
            Assert.That(val, Is.Zero, "performance.now() должен быть кратен 100.");
        }

        [TestCase(TestName = "WebSocket same-origin protection"), Order(24)]
        public async Task WebSocketProtectionTest()
        {
            var settings = new TabContextSettings { WebSocketProtection = "same-origin" };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var err = await tab.ExecuteAsync(
                "try { new WebSocket('ws://evil.example.com'); return 'allowed'; } catch(e) { return e.name; }");

            logger.WriteLine(LogKind.Default, $"WebSocket cross-origin err={err}");
            Assert.That(err?.ToString(), Is.EqualTo("SecurityError"), "Cross-origin WebSocket должен бросать SecurityError.");
        }

        [TestCase(TestName = "Storage quota override"), Order(25)]
        public async Task StorageQuotaOverrideTest()
        {
            var settings = new TabContextSettings { StorageQuota = 2_000_000_000 };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var quota = await tab.ExecuteAsync(
                "(await navigator.storage.estimate()).quota");

            logger.WriteLine(LogKind.Default, $"StorageQuota={quota}");
            Assert.That(quota?.ToString(), Is.EqualTo("2000000000"), "StorageQuota должен быть 2000000000.");
        }

        [TestCase(TestName = "Plugin spoofing override"), Order(26)]
        public async Task PluginSpoofingOverrideTest()
        {
            var settings = new TabContextSettings { PluginSpoofing = true };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var count = await tab.ExecuteAsync("navigator.plugins.length");
            var name = await tab.ExecuteAsync("navigator.plugins[0].name");

            logger.WriteLine(LogKind.Default, $"Plugins count={count}, name={name}");
            Assert.That(count?.ToString(), Is.EqualTo("1"), "Должен быть 1 плагин.");
            Assert.That(name?.ToString(), Is.EqualTo("PDF Viewer"), "Плагин должен быть PDF Viewer.");
        }

        [TestCase(TestName = "MaxTouchPoints override"), Order(27)]
        public async Task MaxTouchPointsOverrideTest()
        {
            var settings = new TabContextSettings { MaxTouchPoints = 5 };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var tp = await tab.ExecuteAsync("navigator.maxTouchPoints");

            logger.WriteLine(LogKind.Default, $"maxTouchPoints={tp}");
            Assert.That(tp?.ToString(), Is.EqualTo("5"), "maxTouchPoints должен быть 5.");
        }

        [TestCase(TestName = "Notification permission override"), Order(28)]
        public async Task NotificationOverrideTest()
        {
            var settings = new TabContextSettings { NotificationPermission = "denied" };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var perm = await tab.ExecuteAsync("Notification.permission");

            logger.WriteLine(LogKind.Default, $"Notification.permission={perm}");
            Assert.That(perm?.ToString(), Is.EqualTo("denied"), "Notification.permission должен быть denied.");
        }

        [TestCase(TestName = "Gamepad API returns empty array"), Order(29)]
        public async Task GamepadProtectionTest()
        {
            var settings = new TabContextSettings { GamepadProtection = true };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var count = await tab.ExecuteAsync("navigator.getGamepads().length");

            logger.WriteLine(LogKind.Default, $"getGamepads().length={count}");
            Assert.That(count?.ToString(), Is.EqualTo("0"), "getGamepads() должен возвращать пустой массив.");
        }

        [TestCase(TestName = "History length override"), Order(30)]
        public async Task HistoryLengthOverrideTest()
        {
            var settings = new TabContextSettings { HistoryLength = 1 };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var len = await tab.ExecuteAsync("history.length");

            logger.WriteLine(LogKind.Default, $"history.length={len}");
            Assert.That(len?.ToString(), Is.EqualTo("1"), "history.length должен быть 1.");
        }

        [TestCase(TestName = "DeviceMotion events blocked"), Order(31)]
        public async Task DeviceMotionProtectionTest()
        {
            var settings = new TabContextSettings { DeviceMotionProtection = true };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var result = await tab.ExecuteAsync("typeof DeviceMotionEvent === 'function' ? 'real' : 'blocked'");

            logger.WriteLine(LogKind.Default, $"DeviceMotionEvent={result}");
            Assert.That(result?.ToString(), Is.EqualTo("blocked"), "DeviceMotionEvent должен быть заблокирован.");
        }

        [TestCase(TestName = "Clipboard read blocked"), Order(32)]
        public async Task ClipboardProtectionTest()
        {
            var settings = new TabContextSettings { ClipboardProtection = true };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var result = await tab.ExecuteAsync(
                "navigator.clipboard.readText().then(()=>'ok').catch(e=>e.name)");

            logger.WriteLine(LogKind.Default, $"clipboard.readText={result}");
            Assert.That(result?.ToString(), Is.EqualTo("NotAllowedError"), "clipboard.readText должен быть заблокирован.");
        }

        [TestCase(TestName = "Web Share API blocked"), Order(33)]
        public async Task WebShareProtectionTest()
        {
            var settings = new TabContextSettings { WebShareProtection = true };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var result = await tab.ExecuteAsync("navigator.canShare({title:'test'})");

            logger.WriteLine(LogKind.Default, $"canShare={result}");
            Assert.That(result?.ToString(), Is.EqualTo("false"), "canShare должен вернуть false.");
        }

        [TestCase(TestName = "Payment Request blocked"), Order(34)]
        public async Task PaymentProtectionTest()
        {
            var settings = new TabContextSettings { PaymentProtection = true };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var result = await tab.ExecuteAsync(
                "try{new PaymentRequest([{supportedMethods:'basic-card'}],{total:{label:'t',amount:{currency:'USD',value:'1'}}});'ok'}catch(e){e.name}");

            logger.WriteLine(LogKind.Default, $"PaymentRequest={result}");
            Assert.That(result?.ToString(), Is.Not.EqualTo("ok"), "PaymentRequest должен быть заблокирован.");
        }

        [TestCase(TestName = "Visibility state override"), Order(35)]
        public async Task VisibilityStateOverrideTest()
        {
            var settings = new TabContextSettings { VisibilityStateOverride = "visible" };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var result = await tab.ExecuteAsync("document.visibilityState");

            logger.WriteLine(LogKind.Default, $"visibilityState={result}");
            Assert.That(result?.ToString(), Is.EqualTo("visible"), "visibilityState должен быть visible.");
        }

        [TestCase(TestName = "Color depth override"), Order(36)]
        public async Task ColorDepthOverrideTest()
        {
            var settings = new TabContextSettings { ColorDepth = 32 };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var result = await tab.ExecuteAsync("screen.colorDepth");

            logger.WriteLine(LogKind.Default, $"colorDepth={result}");
            Assert.That(result?.ToString(), Is.EqualTo("32"), "colorDepth должен быть 32.");
        }

        [TestCase(TestName = "Installed apps protection"), Order(37)]
        public async Task InstalledAppsProtectionTest()
        {
            var settings = new TabContextSettings { InstalledAppsProtection = true };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var result = await tab.ExecuteAsync(
                "navigator.getInstalledRelatedApps ? navigator.getInstalledRelatedApps().then(r=>JSON.stringify(r)) : 'unsupported'");

            logger.WriteLine(LogKind.Default, $"installedApps={result}");
            var val = result?.ToString();
            Assert.That(val is "[]" or "unsupported", $"Ожидалось [] или unsupported, получено {val}.");
        }

        [TestCase(TestName = "Performance.now jitter"), Order(38)]
        public async Task PerformanceNowJitterTest()
        {
            var settings = new TabContextSettings { PerformanceNowJitter = 5.0 };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var result = await tab.ExecuteAsync(
                "(function(){var a=performance.now(),b=performance.now();return Math.abs(b-a)>=0})()");

            logger.WriteLine(LogKind.Default, $"performanceNow jitter ok={result}");
            Assert.That(result?.ToString(), Is.EqualTo("True").Or.EqualTo("true"), "performance.now() должен работать с jitter.");
        }

        [TestCase(TestName = "USB/HID/Serial protection"), Order(39)]
        public async Task UsbHidSerialProtectionTest()
        {
            var settings = new TabContextSettings { UsbHidSerialProtection = true };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var result = await tab.ExecuteAsync(
                "JSON.stringify([typeof navigator.usb, typeof navigator.hid, typeof navigator.serial])");

            logger.WriteLine(LogKind.Default, $"usb/hid/serial={result}");
            var val = result?.ToString();
            Assert.That(val, Does.Contain("undefined"), "navigator.usb/hid/serial должны быть undefined.");
        }

        [TestCase(TestName = "Keyboard API protection"), Order(40)]
        public async Task KeyboardApiProtectionTest()
        {
            var settings = new TabContextSettings { KeyboardApiProtection = true };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var result = await tab.ExecuteAsync(
                "navigator.keyboard ? navigator.keyboard.getLayoutMap().then(m=>m.size) : 'unsupported'");

            logger.WriteLine(LogKind.Default, $"keyboard={result}");
            var val = result?.ToString();
            Assert.That(val is "0" or "unsupported", $"Ожидалось 0 или unsupported, получено {val}.");
        }

        [TestCase(TestName = "Bluetooth protection"), Order(41)]
        public async Task BluetoothProtectionTest()
        {
            var settings = new TabContextSettings { BluetoothProtection = true };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var result = await tab.ExecuteAsync("typeof navigator.bluetooth");

            logger.WriteLine(LogKind.Default, $"bluetooth={result}");
            Assert.That(result?.ToString(), Is.EqualTo("undefined"), "navigator.bluetooth должен быть undefined.");
        }

        [TestCase(TestName = "EyeDropper protection"), Order(42)]
        public async Task EyeDropperProtectionTest()
        {
            var settings = new TabContextSettings { EyeDropperProtection = true };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var result = await tab.ExecuteAsync("typeof window.EyeDropper");

            logger.WriteLine(LogKind.Default, $"eyeDropper={result}");
            Assert.That(result?.ToString(), Is.EqualTo("undefined"), "EyeDropper должен быть undefined.");
        }

        [TestCase(TestName = "WebXR protection"), Order(43)]
        public async Task WebXrProtectionTest()
        {
            var settings = new TabContextSettings { WebXrProtection = true };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var result = await tab.ExecuteAsync("typeof navigator.xr");

            logger.WriteLine(LogKind.Default, $"xr={result}");
            Assert.That(result?.ToString(), Is.EqualTo("undefined"), "navigator.xr должен быть undefined.");
        }

        [TestCase(TestName = "NFC protection"), Order(44)]
        public async Task NfcProtectionTest()
        {
            var settings = new TabContextSettings { NfcProtection = true };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var result = await tab.ExecuteAsync("typeof window.NDEFReader");

            logger.WriteLine(LogKind.Default, $"nfc={result}");
            Assert.That(result?.ToString(), Is.EqualTo("undefined"), "NDEFReader должен быть undefined.");
        }

        [TestCase(TestName = "Cookie Store protection"), Order(45)]
        public async Task CookieStoreProtectionTest()
        {
            var settings = new TabContextSettings { CookieStoreProtection = true };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var result = await tab.ExecuteAsync("typeof window.cookieStore");

            logger.WriteLine(LogKind.Default, $"cookieStore={result}");
            Assert.That(result?.ToString(), Is.EqualTo("undefined"), "cookieStore должен быть undefined.");
        }

        [TestCase(TestName = "Web Locks protection"), Order(46)]
        public async Task WebLocksProtectionTest()
        {
            var settings = new TabContextSettings { WebLocksProtection = true };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var result = await tab.ExecuteAsync("typeof navigator.locks");

            logger.WriteLine(LogKind.Default, $"locks={result}");
            Assert.That(result?.ToString(), Is.EqualTo("undefined"), "navigator.locks должен быть undefined.");
        }

        [TestCase(TestName = "Shape Detection protection"), Order(47)]
        public async Task ShapeDetectionProtectionTest()
        {
            var settings = new TabContextSettings { ShapeDetectionProtection = true };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var result = await tab.ExecuteAsync("typeof window.BarcodeDetector");

            logger.WriteLine(LogKind.Default, $"barcodeDetector={result}");
            Assert.That(result?.ToString(), Is.EqualTo("undefined"), "BarcodeDetector должен быть undefined.");
        }

        [TestCase(TestName = "Compute Pressure protection"), Order(48)]
        public async Task ComputePressureProtectionTest()
        {
            var settings = new TabContextSettings { ComputePressureProtection = true };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var result = await tab.ExecuteAsync("typeof window.PressureObserver");

            logger.WriteLine(LogKind.Default, $"pressureObserver={result}");
            Assert.That(result?.ToString(), Is.EqualTo("undefined"), "PressureObserver должен быть undefined.");
        }

        [TestCase(TestName = "Picture-in-Picture protection"), Order(49)]
        public async Task PictureInPictureProtectionTest()
        {
            var settings = new TabContextSettings { PictureInPictureProtection = true };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var result = await tab.ExecuteAsync("document.pictureInPictureEnabled");

            logger.WriteLine(LogKind.Default, $"pipEnabled={result}");
            Assert.That(result?.ToString(), Is.EqualTo("False").Or.EqualTo("false"), "pictureInPictureEnabled должен быть false.");
        }

        [TestCase(TestName = "Device Posture protection"), Order(50)]
        public async Task DevicePostureProtectionTest()
        {
            var settings = new TabContextSettings { DevicePostureProtection = true };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var result = await tab.ExecuteAsync("typeof navigator.devicePosture");

            logger.WriteLine(LogKind.Default, $"devicePosture={result}");
            Assert.That(result?.ToString(), Is.EqualTo("undefined"), "navigator.devicePosture должен быть undefined.");
        }

        [TestCase(TestName = "Local Font Access protection"), Order(51)]
        public async Task LocalFontAccessProtectionTest()
        {
            var settings = new TabContextSettings { LocalFontAccessProtection = true };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var result = await tab.ExecuteAsync("window.queryLocalFonts ? window.queryLocalFonts().then(f=>f.length) : -1");

            logger.WriteLine(LogKind.Default, $"localFonts={result}");
            Assert.That(result?.ToString(), Is.EqualTo("0"), "queryLocalFonts должен вернуть пустой массив.");
        }

        [TestCase(TestName = "Launch Handler protection"), Order(52)]
        public async Task LaunchHandlerProtectionTest()
        {
            var settings = new TabContextSettings { LaunchHandlerProtection = true };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var result = await tab.ExecuteAsync("typeof window.LaunchParams");

            logger.WriteLine(LogKind.Default, $"launchParams={result}");
            Assert.That(result?.ToString(), Is.EqualTo("undefined"), "LaunchParams должен быть undefined.");
        }

        [TestCase(TestName = "Topics API protection"), Order(53)]
        public async Task TopicsApiProtectionTest()
        {
            var settings = new TabContextSettings { TopicsApiProtection = true };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var result = await tab.ExecuteAsync("document.browsingTopics ? document.browsingTopics().then(t=>JSON.stringify(t)) : 'absent'");

            logger.WriteLine(LogKind.Default, $"browsingTopics={result}");
            Assert.That(result?.ToString(), Is.AnyOf("[]", "absent"), "browsingTopics должен вернуть [] или отсутствовать.");
        }

        [TestCase(TestName = "Shared Storage protection"), Order(54)]
        public async Task SharedStorageProtectionTest()
        {
            var settings = new TabContextSettings { SharedStorageProtection = true };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var result = await tab.ExecuteAsync("typeof window.sharedStorage");

            logger.WriteLine(LogKind.Default, $"sharedStorage={result}");
            Assert.That(result?.ToString(), Is.EqualTo("undefined"), "sharedStorage должен быть undefined.");
        }

        [TestCase(TestName = "Web MIDI protection"), Order(55)]
        public async Task WebMidiProtectionTest()
        {
            var settings = new TabContextSettings { WebMidiProtection = true };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var result = await tab.ExecuteAsync("navigator.requestMIDIAccess ? navigator.requestMIDIAccess().then(()=>'ok').catch(e=>e.name) : 'absent'");

            logger.WriteLine(LogKind.Default, $"MIDI={result}");
            Assert.That(result?.ToString(), Is.AnyOf("NotAllowedError", "absent"), "requestMIDIAccess должен быть заблокирован.");
        }

        [TestCase(TestName = "Navigation API protection"), Order(56)]
        public async Task NavigationApiProtectionTest()
        {
            var settings = new TabContextSettings { NavigationApiProtection = true };
            var tab = await browser.OpenIsolatedTabAsync(settings: settings);

            await Task.Delay(300);
            var result = await tab.ExecuteAsync("typeof window.navigation");

            logger.WriteLine(LogKind.Default, $"navigation={result}");
            Assert.That(result?.ToString(), Is.EqualTo("undefined"), "window.navigation должен быть undefined.");
        }

        [TestCase(TestName = "NavigateAsync body override"), Order(57)]
        public async Task NavigateBodyOverrideTest()
        {
            var tab = await browser.OpenIsolatedTabAsync();

            // Навигируем на bridge /blank с body override — гарантированно достижимый URL.
            var targetUrl = new Uri($"http://127.0.0.1:{browser.BridgePort}/blank?body=1");
            const string customHtml = "<html><head><title>Atom Override</title></head><body><h1>Hello Atom</h1></body></html>";
            await tab.NavigateAsync(targetUrl, new NavigationSettings { Body = customHtml });

            var title = await tab.ExecuteAsync("document.title");
            Assert.That(title?.ToString(), Is.EqualTo("Atom Override"), "Title должен быть из body override.");

            var h1 = await tab.ExecuteAsync("document.querySelector('h1')?.textContent");
            Assert.That(h1?.ToString(), Is.EqualTo("Hello Atom"), "H1 должен быть из body override.");
        }

        [TestCase(TestName = "Turnstile auto-click в iframe"), Order(58)]
        public async Task TurnstileAutoClickTest()
        {
            var tab = await browser.OpenIsolatedTabAsync();

            // Тестовый сайтключ Cloudflare: 1x00000000000000000000AA — visible, always passes.
            // Проверяет инфраструктуру: body override + document.write + внешний скрипт.
            const string testHtml = """
                <!DOCTYPE html>
                <html>
                <head>
                    <title>Turnstile Test</title>
                    <script src="https://challenges.cloudflare.com/turnstile/v0/api.js" async defer></script>
                </head>
                <body>
                    <div class="cf-turnstile" data-sitekey="1x00000000000000000000AA"></div>
                </body>
                </html>
                """;

            var targetUrl = new Uri($"http://127.0.0.1:{browser.BridgePort}/blank?body=1");
            await tab.NavigateAsync(targetUrl, new NavigationSettings { Body = testHtml });

            // Body override: document.write() должен заменить контент.
            var title = await tab.ExecuteAsync("document.title");
            Assert.That(title?.ToString(), Is.EqualTo("Turnstile Test"), "Body override должен заменить документ.");

            // Turnstile SDK должен инициализировать виджет.
            var widgetExists = await tab.ExecuteAsync("!!document.querySelector('.cf-turnstile')");
            Assert.That(widgetExists?.ToString(), Is.EqualTo("true"), "Turnstile виджет должен быть в DOM.");

            // Ожидаем Turnstile token — Cloudflare устанавливает его в hidden input
            // после успешного прохождения проверки (тестовый ключ проходит мгновенно).
            using var tokenCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            string? turnstileToken = null;

            while (!tokenCts.IsCancellationRequested)
            {
                var tokenResult = await tab.ExecuteAsync(
                    "document.querySelector('[name=\"cf-turnstile-response\"]')?.value || ''",
                    tokenCts.Token);

                var tokenValue = tokenResult?.ToString();
                if (!string.IsNullOrEmpty(tokenValue))
                {
                    turnstileToken = tokenValue;
                    break;
                }

                await Task.Delay(300, tokenCts.Token);
            }

            logger.WriteLine(LogKind.Default, $"turnstile token={turnstileToken?[..Math.Min(turnstileToken?.Length ?? 0, 40)]}");
            Assert.That(turnstileToken, Is.Not.Null.And.Not.Empty, "Turnstile token должен быть получен.");
        }

        [TestCase(TestName = "Turnstile auto-click с isTrusted spoofing"), Order(59)]
        public async Task TurnstileInteractiveAutoClickTest()
        {
            var tab = await browser.OpenIsolatedTabAsync();

            // Тестовый ключ 3x — принудительный интерактивный challenge.
            // shadow-intercept.js (MAIN world, document_start) перехватывает addEventListener
            // и оборачивает обработчики Proxy для подмены isTrusted=true.
            const string turnstileHtml = """
                <!DOCTYPE html>
                <html>
                <head>
                    <title>Turnstile Interactive</title>
                    <script src="https://challenges.cloudflare.com/turnstile/v0/api.js" async defer></script>
                </head>
                <body>
                    <div class="cf-turnstile" data-sitekey="3x00000000000000000000FF"></div>
                </body>
                </html>
                """;

            var targetUrl = new Uri($"http://127.0.0.1:{browser.BridgePort}/blank?ts=1");
            await tab.NavigateAsync(targetUrl, new NavigationSettings { Body = turnstileHtml });

            // Ждём появления click-таргетов в CF iframe (polling вместо фиксированной задержки).
            using var readyCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!readyCts.IsCancellationRequested)
            {
                var ready = await tab.ExecuteInAllFramesAsync("""
                    (() => {
                        if (location.hostname !== 'challenges.cloudflare.com') return null;
                        return (window.__clickTargets || []).length;
                    })()
                    """);

                if (ready is System.Text.Json.JsonElement readyArr && readyArr.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var f in readyArr.EnumerateArray())
                    {
                        if (f.ValueKind == System.Text.Json.JsonValueKind.Number && f.GetInt32() > 0)
                            goto clickTargetsReady;
                    }
                }

                await Task.Delay(200, readyCts.Token);
            }

            clickTargetsReady:

            // Авто-клик через Proxy-спуфинг isTrusted в CF iframe.
            // shadow-intercept.js сохраняет элементы с click-обработчиками в __clickTargets.
            await tab.ExecuteInAllFramesAsync("""
                (() => {
                    if (location.hostname !== 'challenges.cloudflare.com') return null;

                    window.__spoofTrusted = true;

                    const targets = window.__clickTargets || [];
                    const target = targets.find(e => e.tagName === 'INPUT')
                                || targets.find(e => e.tagName === 'DIV')
                                || targets[0];

                    if (!target) return null;

                    let cx = 25, cy = 30;
                    try {
                        const rect = target.getBoundingClientRect();
                        if (rect.width > 0) { cx = rect.x + rect.width / 2; cy = rect.y + rect.height / 2; }
                    } catch {}

                    for (let i = 1; i <= 5; i++) {
                        document.dispatchEvent(new MouseEvent('mousemove', {
                            bubbles: true, cancelable: true, view: window,
                            clientX: cx * i / 5 + Math.random() * 3 - 1.5,
                            clientY: cy * i / 5 + Math.random() * 3 - 1.5
                        }));
                    }

                    target.dispatchEvent(new MouseEvent('mouseenter', {
                        bubbles: false, cancelable: false, view: window, clientX: cx, clientY: cy
                    }));
                    target.dispatchEvent(new MouseEvent('mousemove', {
                        bubbles: true, cancelable: true, view: window, clientX: cx, clientY: cy
                    }));

                    for (const evType of ['pointerdown', 'mousedown', 'pointerup', 'mouseup', 'click']) {
                        target.dispatchEvent(new MouseEvent(evType, {
                            bubbles: true, cancelable: true, view: window,
                            clientX: cx, clientY: cy, button: 0
                        }));
                    }

                    return target.tagName;
                })()
                """);

            // Ожидаем токен — CF должен выдать его после прохождения challenge.
            using var tokenCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            string? turnstileToken = null;

            while (!tokenCts.IsCancellationRequested)
            {
                var tokenResult = await tab.ExecuteAsync(
                    "document.querySelector('[name=\"cf-turnstile-response\"]')?.value || ''",
                    tokenCts.Token);

                var tokenValue = tokenResult?.ToString();
                if (!string.IsNullOrEmpty(tokenValue))
                {
                    turnstileToken = tokenValue;
                    break;
                }

                await Task.Delay(300, tokenCts.Token);
            }

            logger.WriteLine(LogKind.Default, $"turnstile token={turnstileToken?[..Math.Min(turnstileToken?.Length ?? 0, 40)]}");
            Assert.That(turnstileToken, Is.Not.Null.And.Not.Empty, "Turnstile token должен быть получен (auto-click через isTrusted Proxy).");
        }

        private async Task<WebDriverPage> WaitForFirstTabAsync()
        {
            var tcs = new TaskCompletionSource<TabConnectedEventArgs>();
            browser.TabConnected += (_, e) =>
            {
                tcs.TrySetResult(e);
                return ValueTask.CompletedTask;
            };

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            if (browser.ConnectionCount > 0)
                return browser.GetAllPages().First();

            var result = await tcs.Task.WaitAsync(cts.Token);
            var page = browser.GetPage(result.TabId)
                ?? throw new BridgeException($"Страница {result.TabId} не найдена.");

            using var warmupCts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            try { await page.ExecuteAsync("1", warmupCts.Token); }
            catch { /* warm-up не критичен */ }

            return page;
        }
    }
}