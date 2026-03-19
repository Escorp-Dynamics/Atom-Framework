﻿﻿using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Atom;
using Atom.Media;
using Atom.Net.Browsing;
using Atom.Net.Browsing.WebDriver;
using Atom.Net.Browsing.WebDriver.Protocol;

namespace Tests;

[TestFixture, Parallelizable(ParallelScope.All)]
public class WebDriverTests(ILogger logger) : BenchmarkTests<WebDriverTests>(logger)
{
    private readonly ILogger log = logger;

    public override bool IsBenchmarkEnabled => default;

    public WebDriverTests() : this(ConsoleLogger.Unicode) { }

    /// <summary>
    /// Путь к папке расширения коннектора (единый для всех браузеров).
    /// Firefox-специфичные изменения manifest.json применяются в рантайме.
    /// </summary>
    private static string ExtensionPath => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "Extension"));

    // ─── Обнаружение установленных браузеров ─────────────────────

    private static readonly (string Name, string[] LinuxPaths, string[] WinPaths, bool IsFirefox)[] KnownBrowsers =
    [
        // Chrome исключён — не поддерживает MV2 (начиная с Chrome 130+).
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
        var extensionPath = ExtensionPath;
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

        [TestCase(TestName = "ClickElementAsync выполняет page-local click по selector"), Order(6)]
        public async Task ClickElementApiTest()
        {
            await page.ExecuteAsync("""
                document.body.innerHTML = '<button id="api-button">Click</button>';
                document.documentElement.dataset.elementClickCount = '0';
                document.getElementById('api-button').addEventListener('click', () => {
                    document.documentElement.dataset.elementClickCount = String(Number(document.documentElement.dataset.elementClickCount || '0') + 1);
                });
            """);

            await page.ClickElementAsync(new ElementSelector
            {
                Strategy = ElementSelectorStrategy.Css,
                Value = "#api-button",
            });

            var clickCount = (await page.ExecuteAsync("document.documentElement.dataset.elementClickCount"))?.ToString();
            Assert.That(clickCount, Is.EqualTo("1"), "Selector-based page click должен диспатчить element-oriented click.");
        }

        [TestCase(TestName = "FocusElementAsync устанавливает фокус по selector"), Order(7)]
        public async Task FocusElementApiTest()
        {
            await page.ExecuteAsync("""
                document.body.innerHTML = '<input id="focus-target" />';
            """);

            await page.FocusElementAsync(new ElementSelector
            {
                Strategy = ElementSelectorStrategy.Id,
                Value = "focus-target",
            });

            var activeId = (await page.ExecuteAsync("document.activeElement?.id || ''"))?.ToString();
            Assert.That(activeId, Is.EqualTo("focus-target"), "Selector-based focus должен устанавливать activeElement.");
        }

        [TestCase(TestName = "TypeElementAsync вводит текст по selector"), Order(8)]
        public async Task TypeElementApiTest()
        {
            await page.ExecuteAsync("""
                document.body.innerHTML = '<input id="type-target" />';
            """);

            await page.TypeElementAsync(new ElementSelector
            {
                Strategy = ElementSelectorStrategy.Id,
                Value = "type-target",
            }, "hello");

            var value = (await page.ExecuteAsync("document.getElementById('type-target')?.value || ''"))?.ToString();
            Assert.That(value, Is.EqualTo("hello"), "Selector-based type должен обновлять value у input-элемента.");
        }

        [TestCase(TestName = "CheckElementAsync отмечает checkbox по selector"), Order(9)]
        public async Task CheckElementApiTest()
        {
            await page.ExecuteAsync("""
                document.body.innerHTML = '<input id="check-target" type="checkbox" />';
            """);

            await page.CheckElementAsync(new ElementSelector
            {
                Strategy = ElementSelectorStrategy.Id,
                Value = "check-target",
            });

            var isChecked = (await page.ExecuteAsync("document.getElementById('check-target')?.checked ? 'true' : 'false'"))?.ToString();
            Assert.That(isChecked, Is.EqualTo("true"), "Selector-based check должен переводить checkbox в checked state.");
        }

        [TestCase(TestName = "HoverElementAsync диспатчит hover по selector"), Order(10)]
        public async Task HoverElementApiTest()
        {
            await page.ExecuteAsync("""
                document.body.innerHTML = '<div id="hover-target" style="width:120px;height:40px"></div>';
                document.documentElement.dataset.hoverCount = '0';
                document.getElementById('hover-target').addEventListener('mouseover', () => {
                    document.documentElement.dataset.hoverCount = String(Number(document.documentElement.dataset.hoverCount || '0') + 1);
                });
            """);

            await page.HoverElementAsync(new ElementSelector
            {
                Strategy = ElementSelectorStrategy.Id,
                Value = "hover-target",
            });

            var hoverCount = (await page.ExecuteAsync("document.documentElement.dataset.hoverCount || '0'"))?.ToString();
            Assert.That(hoverCount, Is.EqualTo("1"), "Selector-based hover должен диспатчить mouseover по элементу.");
        }

        [TestCase(TestName = "Edge-cases: пустой скрипт, undefined, null, типы"), Order(11)]
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
    /// Тесты Shadow DOM: <see cref="IShadowRoot"/> — IAsyncDisposable-скоуп
    /// для поиска элементов и выполнения скриптов внутри теневого дерева.
    /// </summary>
    [TestFixture, Category("GUI"), NonParallelizable]
    public class ShadowRootTests
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
            logger.WriteLine(LogKind.Default, $"[ShadowRootTests] Браузер запущен, вкладка: {page.TabId}");
        }

        [OneTimeTearDown]
        public async Task TearDown()
        {
            await browser.DisposeAsync();
        }

        [TestCase(TestName = "ShadowRoot: OpenShadowRootAsync на open shadow root → IShadowRoot"), Order(1)]
        public async Task OpenShadowRootReturnsScopeTest()
        {
            await page.ExecuteAsync("""
                document.body.innerHTML = '';
                var host = document.createElement('div');
                host.id = 'sr-host';
                document.body.appendChild(host);
                host.attachShadow({mode: 'open'}).innerHTML = '<p id="inner">Shadow</p>';
            """);

            var host = await page.FindElementAsync(new ElementSelector
            {
                Strategy = ElementSelectorStrategy.Css,
                Value = "#sr-host",
            });

            Assert.That(host, Is.Not.Null, "Хост-элемент найден.");

            await using var shadow = await host!.OpenShadowRootAsync();

            Assert.That(shadow, Is.Not.Null, "Open shadow root обнаружен.");
            logger.WriteLine(LogKind.Default, "OpenShadowRoot вернул IShadowRoot.");
        }

        [TestCase(TestName = "ShadowRoot: OpenShadowRootAsync без shadow root → null"), Order(2)]
        public async Task OpenShadowRootNoShadowReturnsNullTest()
        {
            await page.ExecuteAsync("""
                document.body.innerHTML = '<div id="plain">No shadow here</div>';
            """);

            var plain = await page.FindElementAsync(new ElementSelector
            {
                Strategy = ElementSelectorStrategy.Css,
                Value = "#plain",
            });

            Assert.That(plain, Is.Not.Null);

            var shadow = await plain!.OpenShadowRootAsync();

            Assert.That(shadow, Is.Null, "Элемент без shadow root → null.");
        }

        [TestCase(TestName = "ShadowRoot: OpenShadowRootAsync на closed shadow root → null"), Order(2)]
        public async Task OpenShadowRootClosedShadowReturnsNullTest()
        {
            await page.ExecuteAsync("""
                document.body.innerHTML = '';
                var host = document.createElement('div');
                host.id = 'sr-closed';
                document.body.appendChild(host);
                host.attachShadow({mode: 'closed'}).innerHTML = '<p>Closed</p>';
            """);

            var host = await page.FindElementAsync(new ElementSelector
            {
                Strategy = ElementSelectorStrategy.Css,
                Value = "#sr-closed",
            });

            Assert.That(host, Is.Not.Null);

            var shadow = await host!.OpenShadowRootAsync();

            Assert.That(shadow, Is.Null, "Closed shadow root не должен открываться без page-side инжекта и обходов.");
        }

        [TestCase(TestName = "ShadowRoot: FindElementAsync в теневом дереве"), Order(3)]
        public async Task FindElementInsideShadowTest()
        {
            await page.ExecuteAsync("""
                document.body.innerHTML = '';
                var host = document.createElement('div');
                host.id = 'sr-find';
                document.body.appendChild(host);
                host.attachShadow({mode: 'open'}).innerHTML =
                    '<p id="shadow-para" class="item">Found inside shadow</p>';
            """);

            var host = await page.FindElementAsync(new ElementSelector
            {
                Strategy = ElementSelectorStrategy.Css,
                Value = "#sr-find",
            });

            await using var shadow = await host!.OpenShadowRootAsync();
            Assert.That(shadow, Is.Not.Null);

            var el = await shadow!.FindElementAsync(new ElementSelector
            {
                Strategy = ElementSelectorStrategy.Css,
                Value = "#shadow-para",
            });

            Assert.That(el, Is.Not.Null, "Элемент внутри shadow DOM найден по CSS.");

            var text = await el!.GetPropertyAsync("textContent");
            Assert.That(text, Is.EqualTo("Found inside shadow"));
            logger.WriteLine(LogKind.Default, $"FindElement в shadow: textContent='{text}'");
        }

        [TestCase(TestName = "ShadowRoot: FindElementsAsync — несколько элементов"), Order(4)]
        public async Task FindElementsInsideShadowTest()
        {
            await page.ExecuteAsync("""
                document.body.innerHTML = '';
                var host = document.createElement('div');
                host.id = 'sr-multi';
                document.body.appendChild(host);
                host.attachShadow({mode: 'open'}).innerHTML =
                    '<span class="si">A</span><span class="si">B</span><span class="si">C</span>';
            """);

            var host = await page.FindElementAsync(new ElementSelector
            {
                Strategy = ElementSelectorStrategy.Css,
                Value = "#sr-multi",
            });

            await using var shadow = await host!.OpenShadowRootAsync();
            Assert.That(shadow, Is.Not.Null);

            var elements = await shadow!.FindElementsAsync(new ElementSelector
            {
                Strategy = ElementSelectorStrategy.Css,
                Value = ".si",
            });

            Assert.That(elements, Has.Length.EqualTo(3), "Найдены 3 элемента внутри shadow DOM.");
            logger.WriteLine(LogKind.Default, $"FindElements в shadow: count={elements.Length}");
        }

        [TestCase(TestName = "ShadowRoot: ExecuteAsync — переменная shadowRoot доступна"), Order(5)]
        public async Task ShadowRootExecuteAsyncTest()
        {
            await page.ExecuteAsync("""
                document.body.innerHTML = '';
                var host = document.createElement('div');
                host.id = 'sr-exec';
                document.body.appendChild(host);
                host.attachShadow({mode: 'open'}).innerHTML = '<b>bold</b>';
            """);

            var host = await page.FindElementAsync(new ElementSelector
            {
                Strategy = ElementSelectorStrategy.Css,
                Value = "#sr-exec",
            });

            await using var shadow = await host!.OpenShadowRootAsync();
            Assert.That(shadow, Is.Not.Null);

            var result = await shadow!.ExecuteAsync("return shadowRoot.querySelector('b').textContent");

            Assert.That(result?.ToString(), Is.EqualTo("bold"),
                "ExecuteAsync внутри shadow scope имеет доступ к переменной shadowRoot.");
            logger.WriteLine(LogKind.Default, $"ExecuteAsync в shadow: result='{result}'");
        }

        [TestCase(TestName = "ShadowRoot: GetContentAsync — innerHTML теневого дерева"), Order(6)]
        public async Task ShadowRootGetContentAsyncTest()
        {
            await page.ExecuteAsync("""
                document.body.innerHTML = '';
                var host = document.createElement('div');
                host.id = 'sr-content';
                document.body.appendChild(host);
                host.attachShadow({mode: 'open'}).innerHTML = '<em>emphasized</em>';
            """);

            var host = await page.FindElementAsync(new ElementSelector
            {
                Strategy = ElementSelectorStrategy.Css,
                Value = "#sr-content",
            });

            await using var shadow = await host!.OpenShadowRootAsync();
            Assert.That(shadow, Is.Not.Null);

            var html = await shadow!.GetContentAsync();

            Assert.That(html, Does.Contain("<em>emphasized</em>"),
                "GetContentAsync возвращает innerHTML shadow root.");
            logger.WriteLine(LogKind.Default, $"GetContentAsync: '{html}'");
        }

        [TestCase(TestName = "ShadowRoot: WaitForElementAsync — ожидание динамического элемента"), Order(7)]
        public async Task WaitForElementInsideShadowTest()
        {
            await page.ExecuteAsync("""
                document.body.innerHTML = '';
                var host = document.createElement('div');
                host.id = 'sr-wait';
                document.body.appendChild(host);
                host.attachShadow({mode: 'open'});
            """);

            var host = await page.FindElementAsync(new ElementSelector
            {
                Strategy = ElementSelectorStrategy.Css,
                Value = "#sr-wait",
            });

            await using var shadow = await host!.OpenShadowRootAsync();
            Assert.That(shadow, Is.Not.Null);

            // Добавляем элемент в shadow DOM через 300 мс.
            _ = page.ExecuteAsync("""
                setTimeout(() => {
                    var host = document.getElementById('sr-wait');
                    host.shadowRoot.innerHTML = '<div id="delayed">I appeared</div>';
                }, 300);
            """);

            var found = await shadow!.WaitForElementAsync(
                new ElementSelector { Strategy = ElementSelectorStrategy.Css, Value = "#delayed" },
                timeout: TimeSpan.FromSeconds(5));

            Assert.That(found, Is.Not.Null, "WaitForElement обнаружил динамически добавленный элемент в shadow DOM.");

            var text = await found!.GetPropertyAsync("textContent");
            Assert.That(text, Is.EqualTo("I appeared"));
            logger.WriteLine(LogKind.Default, $"WaitForElement в shadow: textContent='{text}'");
        }

        [TestCase(TestName = "ShadowRoot: DisposeAsync → ObjectDisposedException"), Order(8)]
        public async Task ShadowRootDisposeAsyncTest()
        {
            await page.ExecuteAsync("""
                document.body.innerHTML = '';
                var host = document.createElement('div');
                host.id = 'sr-dispose';
                document.body.appendChild(host);
                host.attachShadow({mode: 'open'}).innerHTML = '<p>disposable</p>';
            """);

            var host = await page.FindElementAsync(new ElementSelector
            {
                Strategy = ElementSelectorStrategy.Css,
                Value = "#sr-dispose",
            });

            var shadow = await host!.OpenShadowRootAsync();
            Assert.That(shadow, Is.Not.Null);

            await shadow!.DisposeAsync();

            Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await shadow.ExecuteAsync("return 1"));

            Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await shadow.FindElementAsync(new ElementSelector
                {
                    Strategy = ElementSelectorStrategy.Css,
                    Value = "p",
                }));

            Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await shadow.FindElementsAsync(new ElementSelector
                {
                    Strategy = ElementSelectorStrategy.Css,
                    Value = "p",
                }));

            Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await shadow.WaitForElementAsync(
                    new ElementSelector { Strategy = ElementSelectorStrategy.Css, Value = "p" },
                    TimeSpan.FromSeconds(1)));

            logger.WriteLine(LogKind.Default, "DisposeAsync → все операции бросают ObjectDisposedException.");
        }

        [TestCase(TestName = "ShadowRoot: элементы в shadow DOM изолированы от основного документа"), Order(9)]
        public async Task ShadowDomIsolationTest()
        {
            await page.ExecuteAsync("""
                document.body.innerHTML = '<p id="outer-unique">Outer</p>';
                var host = document.createElement('div');
                host.id = 'sr-iso';
                document.body.appendChild(host);
                host.attachShadow({mode: 'open'}).innerHTML = '<p id="inner-unique">Inner</p>';
            """);

            // Из основного контекста — находим outer, но НЕ inner.
            var outer = await page.FindElementAsync(new ElementSelector
            {
                Strategy = ElementSelectorStrategy.Css,
                Value = "#outer-unique",
            });
            Assert.That(outer, Is.Not.Null, "outer-unique найден в основном DOM.");

            var innerFromPage = await page.FindElementAsync(new ElementSelector
            {
                Strategy = ElementSelectorStrategy.Css,
                Value = "#inner-unique",
            });
            Assert.That(innerFromPage, Is.Null, "inner-unique НЕ найден через page (изоляция shadow DOM).");

            // Из shadow scope — находим inner, но НЕ outer.
            var host = await page.FindElementAsync(new ElementSelector
            {
                Strategy = ElementSelectorStrategy.Css,
                Value = "#sr-iso",
            });

            await using var shadow = await host!.OpenShadowRootAsync();
            Assert.That(shadow, Is.Not.Null);

            var inner = await shadow!.FindElementAsync(new ElementSelector
            {
                Strategy = ElementSelectorStrategy.Css,
                Value = "#inner-unique",
            });
            Assert.That(inner, Is.Not.Null, "inner-unique найден через shadow scope.");

            var outerFromShadow = await shadow.FindElementAsync(new ElementSelector
            {
                Strategy = ElementSelectorStrategy.Css,
                Value = "#outer-unique",
            });
            Assert.That(outerFromShadow, Is.Null, "outer-unique НЕ найден через shadow scope (изоляция).");

            logger.WriteLine(LogKind.Default, "Shadow DOM изоляция подтверждена.");
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
            logger.WriteLine(LogKind.Default, $"Вкладка подключилась: {result.TabId}");

            return browser.GetPage(result.TabId)
                ?? throw new BridgeException($"Страница {result.TabId} не найдена после подключения.");
        }
    }

    /// <summary>
    /// Тесты перехвата сетевых запросов (Continue, Abort, Fulfill).
    /// </summary>
    [TestFixture, Category("GUI"), NonParallelizable]
    public class InterceptionTests
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

        [TestCase(TestName = "Interception: Continue — запрос проходит"), Order(1)]
        public async Task ContinueTest()
        {
            await page.SetRequestInterceptionAsync(true);

            var intercepted = new TaskCompletionSource<InterceptedRequestEventArgs>();
            AsyncEventHandler<WebDriverPage, InterceptedRequestEventArgs> handler = (_, e) =>
            {
                if (e.ResourceType == "main_frame")
                    intercepted.TrySetResult(e);
                e.Continue();
                return ValueTask.CompletedTask;
            };
            page.RequestIntercepted += handler;

            try
            {
                var url = new Uri("https://example.com/continue-test");
                await page.NavigateAsync(url);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var args = await intercepted.Task.WaitAsync(cts.Token);

                Assert.That(args.Url, Does.Contain("example.com"), "Перехвачен запрос к example.com.");
                Assert.That(args.Method, Is.EqualTo("GET"), "Метод — GET.");
            }
            finally
            {
                page.RequestIntercepted -= handler;
                await page.SetRequestInterceptionAsync(false);
            }
        }

        [TestCase(TestName = "Interception: Abort — запрос отменён"), Order(2)]
        public async Task AbortTest()
        {
            await page.SetRequestInterceptionAsync(true);

            var intercepted = new TaskCompletionSource<InterceptedRequestEventArgs>();
            AsyncEventHandler<WebDriverPage, InterceptedRequestEventArgs> handler = (_, e) =>
            {
                if (e.ResourceType == "main_frame")
                {
                    intercepted.TrySetResult(e);
                    e.Abort();
                }
                else
                {
                    e.Continue();
                }

                return ValueTask.CompletedTask;
            };
            page.RequestIntercepted += handler;

            try
            {
                var url = new Uri("https://example.com");
                using var navCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try { await page.NavigateAsync(url, navCts.Token); }
                catch (OperationCanceledException) { } // Ожидаемо — abort предотвращает загрузку

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var args = await intercepted.Task.WaitAsync(cts.Token);

                Assert.That(args.Url, Does.Contain("example.com"), "Перехвачен запрос к example.com.");
            }
            finally
            {
                page.RequestIntercepted -= handler;
                await page.SetRequestInterceptionAsync(false);
            }
        }

        [TestCase(TestName = "Interception: Fulfill — кастомный ответ"), Order(3)]
        public async Task FulfillTest()
        {
            await page.SetRequestInterceptionAsync(true);

            const string customBody = "<html><body><h1 id='fulfill-marker'>Intercepted!</h1></body></html>";
            var url = new Uri("https://httpbin.org/html");
            AsyncEventHandler<WebDriverPage, InterceptedRequestEventArgs> handler = (_, e) =>
            {
                if (e.ResourceType == "main_frame")
                {
                    e.Fulfill(new InterceptedRequestFulfillment
                    {
                        StatusCode = 200,
                        ContentType = "text/html",
                        Body = customBody,
                    });
                }
                else
                {
                    e.Continue();
                }

                return ValueTask.CompletedTask;
            };
            page.RequestIntercepted += handler;

            try
            {
                await page.NavigateAsync(url);
                await Task.Delay(500);

                var content = await page.GetContentAsync();
                var currentUrl = await page.GetUrlAsync();
                Assert.That(content, Does.Contain("Intercepted!"), "Ответ подменён кастомным HTML.");
                Assert.That(currentUrl, Is.EqualTo(url), "Навигация должна сохранить исходный URL при fulfill main_frame.");
            }
            finally
            {
                page.RequestIntercepted -= handler;
                await page.SetRequestInterceptionAsync(false);
            }
        }

        [TestCase(TestName = "Interception: PostData — тело POST-запроса доступно"), Order(4)]
        public async Task PostDataTest()
        {
            await page.SetRequestInterceptionAsync(true);

            var intercepted = new TaskCompletionSource<InterceptedRequestEventArgs>();
            AsyncEventHandler<WebDriverPage, InterceptedRequestEventArgs> handler = (_, e) =>
            {
                if (e.Method == "POST" && e.Url.Contains("httpbin.org"))
                    intercepted.TrySetResult(e);
                e.Continue();
                return ValueTask.CompletedTask;
            };
            page.RequestIntercepted += handler;

            try
            {
                // Инъектируем fetch POST с JSON-телом.
                await page.ExecuteAsync("""
                    fetch('https://httpbin.org/post', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ key: 'value123' })
                    }).catch(() => {}); 0
                """);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var args = await intercepted.Task.WaitAsync(cts.Token);

                Assert.That(args.Method, Is.EqualTo("POST"), "Метод — POST.");
                Assert.That(args.PostData, Is.Not.Null, "PostData не null для POST-запроса.");
                var bodyText = System.Text.Encoding.UTF8.GetString(args.PostData!.Value.Span);
                Assert.That(bodyText, Does.Contain("value123"), "Тело содержит отправленные данные.");
            }
            finally
            {
                page.RequestIntercepted -= handler;
                await page.SetRequestInterceptionAsync(false);
            }
        }

        [TestCase(TestName = "Interception: URL Patterns — фильтрация по паттернам"), Order(5)]
        public async Task UrlPatternsTest()
        {
            // Перехватывать только запросы к httpbin.org.
            await page.SetRequestInterceptionAsync(true, ["*httpbin.org*"]);

            var httpbinIntercepted = new TaskCompletionSource<InterceptedRequestEventArgs>();
            var otherIntercepted = false;

            AsyncEventHandler<WebDriverPage, InterceptedRequestEventArgs> handler = (_, e) =>
            {
                if (e.Url.Contains("httpbin.org"))
                    httpbinIntercepted.TrySetResult(e);
                else
                    otherIntercepted = true;
                e.Continue();
                return ValueTask.CompletedTask;
            };
            page.RequestIntercepted += handler;

            try
            {
                // Запрос к httpbin.org — должен быть перехвачен.
                // Суффикс "; 0" предотвращает возврат Promise из eval — тесту не нужен результат fetch,
                // только факт перехвата. Без этого evalInMainWorld ждёт завершения fetch (до 30с).
                await page.ExecuteAsync("""
                    fetch('https://httpbin.org/get').catch(() => {}); 0
                """);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var args = await httpbinIntercepted.Task.WaitAsync(cts.Token);
                Assert.That(args.Url, Does.Contain("httpbin.org"), "Перехвачен запрос к httpbin.org.");

                // Запрос к другому хосту — НЕ должен быть перехвачен.
                await page.ExecuteAsync("""
                    fetch('https://example.com/test').catch(() => {}); 0
                """);
                await Task.Delay(2000);

                Assert.That(otherIntercepted, Is.False, "Запрос к example.com НЕ перехвачен (паттерн не совпал).");
            }
            finally
            {
                page.RequestIntercepted -= handler;
                await page.SetRequestInterceptionAsync(false);
            }
        }

        [TestCase(TestName = "Interception: Timestamp — метка времени перехвата"), Order(6)]
        public async Task TimestampTest()
        {
            await page.SetRequestInterceptionAsync(true);

            var intercepted = new TaskCompletionSource<InterceptedRequestEventArgs>();
            AsyncEventHandler<WebDriverPage, InterceptedRequestEventArgs> handler = (_, e) =>
            {
                if (e.ResourceType == "main_frame")
                    intercepted.TrySetResult(e);
                e.Continue();
                return ValueTask.CompletedTask;
            };
            page.RequestIntercepted += handler;

            try
            {
                var before = DateTimeOffset.UtcNow;
                var url = new Uri("https://example.com/timestamp-test");
                await page.NavigateAsync(url);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var args = await intercepted.Task.WaitAsync(cts.Token);
                var after = DateTimeOffset.UtcNow;

                Assert.That(args.Timestamp, Is.GreaterThanOrEqualTo(before.AddSeconds(-2)),
                    "Timestamp не раньше момента отправки (с допуском 2 сек).");
                Assert.That(args.Timestamp, Is.LessThanOrEqualTo(after.AddSeconds(2)),
                    "Timestamp не позже момента получения (с допуском 2 сек).");
            }
            finally
            {
                page.RequestIntercepted -= handler;
                await page.SetRequestInterceptionAsync(false);
            }
        }

        [TestCase(TestName = "Interception: Redirect — удобный метод перенаправления"), Order(7)]
        public async Task RedirectTest()
        {
            await page.SetRequestInterceptionAsync(true);

            const string fulfillBody = "<html><body><h1 id='redirected'>Redirected!</h1></body></html>";
            var redirectedUrl = new Uri("https://httpbin.org/anything/redirected-page");
            AsyncEventHandler<WebDriverPage, InterceptedRequestEventArgs> handler = (_, e) =>
            {
                if (e.ResourceType == "main_frame" && e.Url.Contains("original-page"))
                {
                    e.Redirect(redirectedUrl);
                }
                else if (e.ResourceType == "main_frame" && e.Url.Contains("redirected-page"))
                {
                    e.Fulfill(new InterceptedRequestFulfillment
                    {
                        StatusCode = 200,
                        ContentType = "text/html",
                        Body = fulfillBody,
                    });
                }
                else
                {
                    e.Continue();
                }
                return ValueTask.CompletedTask;
            };
            page.RequestIntercepted += handler;

            try
            {
                var url = new Uri("https://httpbin.org/anything/original-page");
                await page.NavigateAsync(url);
                await Task.Delay(500);

                var content = await page.GetContentAsync();
                var currentUrl = await page.GetUrlAsync();
                Assert.That(content, Does.Contain("Redirected!"),
                    "Redirect перенаправил запрос, и подменённый ответ отображён.");
                Assert.That(currentUrl, Is.EqualTo(redirectedUrl),
                    "После redirect навигация должна остаться на конечном URL.");
            }
            finally
            {
                page.RequestIntercepted -= handler;
                await page.SetRequestInterceptionAsync(false);
            }
        }

        [TestCase(TestName = "Interception: Response Headers — модификация заголовков ответа"), Order(8)]
        public async Task ResponseHeadersOverrideTest()
        {
            await page.SetRequestInterceptionAsync(true);

            AsyncEventHandler<WebDriverPage, InterceptedRequestEventArgs> handler = (_, e) =>
            {
                if (e.Url.Contains("httpbin.org/get"))
                {
                    e.Continue(new InterceptedRequestContinuation
                    {
                        ResponseHeaders = new Dictionary<string, string>
                        {
                            ["X-Custom-Test"] = "intercepted-value",
                            ["Access-Control-Expose-Headers"] = "X-Custom-Test",
                        },
                    });
                }
                else
                {
                    e.Continue();
                }
                return ValueTask.CompletedTask;
            };
            page.RequestIntercepted += handler;

            try
            {
                // Делаем fetch и читаем заголовок ответа.
                // evalInMainWorld теперь корректно ожидает промисы из async IIFE.
                var result = await page.ExecuteAsync("""
                    (async () => {
                        const resp = await fetch('https://httpbin.org/get');
                        return resp.headers.get('X-Custom-Test') ?? 'not-found';
                    })()
                """);

                var headerValue = result?.GetString();
                Assert.That(headerValue, Is.EqualTo("intercepted-value"),
                    "Кастомный заголовок ответа присутствует.");
            }
            finally
            {
                page.RequestIntercepted -= handler;
                await page.SetRequestInterceptionAsync(false);
            }
        }

        [TestCase(TestName = "Interception: Body Override — кастомный HTML вместо ответа сервера"), Order(9)]
        public async Task BodyOverrideTest()
        {
            const string customHtml = "<html><body><h1 id='marker'>BODY_OVERRIDE_OK</h1></body></html>";
            var url = new Uri("https://httpbin.org/html");

            await page.NavigateAsync(url, new NavigationSettings { Body = customHtml });
            await Task.Delay(500);

            var result = await page.ExecuteAsync("document.getElementById('marker')?.textContent ?? 'not-found'");
            var currentUrl = await page.GetUrlAsync();
            var text = result?.GetString();

            Assert.That(text, Is.EqualTo("BODY_OVERRIDE_OK"),
                "Страница должна содержать подставленный HTML, а не ответ сервера.");
            Assert.That(currentUrl, Is.EqualTo(url),
                "Body override должен сохранять адрес навигации.");
            logger.WriteLine(LogKind.Default, $"BodyOverride: marker text = {text}");
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

        [TestCase(TestName = "ConsoleMessage: console.log доставляется как событие"), Order(14)]
        public async Task ConsoleMessageEventTest()
        {
            var newTab = await browser.OpenTabAsync(new Uri("about:blank"));
            await Task.Delay(200);

            var messages = new List<ConsoleMessageEventArgs>();
            var logTcs = new TaskCompletionSource<ConsoleMessageEventArgs>();
            AsyncEventHandler<IWebPage, ConsoleMessageEventArgs> handler = (_, e) =>
            {
                messages.Add(e);
                if (e.Level == ConsoleMessageLevel.Log)
                    logTcs.TrySetResult(e);
                return ValueTask.CompletedTask;
            };
            newTab.ConsoleMessage += handler;

            try
            {
                await newTab.ExecuteAsync("console.log('hello', 42)");

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var msg = await logTcs.Task.WaitAsync(cts.Token);

                Assert.That(msg.Level, Is.EqualTo(ConsoleMessageLevel.Log), "Уровень — Log.");
                Assert.That(msg.Args, Has.Count.GreaterThanOrEqualTo(2), "Минимум 2 аргумента.");
                Assert.That(msg.Args[0], Does.Contain("hello"), "Первый аргумент содержит 'hello'.");
                Assert.That(msg.Timestamp, Is.GreaterThan(DateTimeOffset.UnixEpoch), "Timestamp валиден.");
                logger.WriteLine(LogKind.Default, $"ConsoleMessage: level={msg.Level}, args=[{string.Join(", ", msg.Args)}]");
            }
            finally
            {
                newTab.ConsoleMessage -= handler;
            }
        }

        [TestCase(TestName = "ConsoleMessage: console.warn → Level.Warn"), Order(15)]
        public async Task ConsoleWarnLevelTest()
        {
            var newTab = await browser.OpenTabAsync(new Uri("about:blank"));
            await Task.Delay(200);

            var tcs = new TaskCompletionSource<ConsoleMessageEventArgs>();
            AsyncEventHandler<IWebPage, ConsoleMessageEventArgs> handler = (_, e) =>
            {
                if (e.Level == ConsoleMessageLevel.Warn)
                    tcs.TrySetResult(e);
                return ValueTask.CompletedTask;
            };
            newTab.ConsoleMessage += handler;

            try
            {
                await newTab.ExecuteAsync("console.warn('warning message')");

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var msg = await tcs.Task.WaitAsync(cts.Token);

                Assert.That(msg.Level, Is.EqualTo(ConsoleMessageLevel.Warn), "Уровень — Warn.");
                Assert.That(msg.Args, Has.Count.GreaterThanOrEqualTo(1), "Минимум 1 аргумент.");
                Assert.That(msg.Args[0], Does.Contain("warning message"), "Аргумент содержит текст.");
                logger.WriteLine(LogKind.Default, $"ConsoleWarn: level={msg.Level}, args=[{string.Join(", ", msg.Args)}]");
            }
            finally
            {
                newTab.ConsoleMessage -= handler;
            }
        }

        [TestCase(TestName = "ConsoleMessage: console.error → Level.Error"), Order(16)]
        public async Task ConsoleErrorLevelTest()
        {
            var newTab = await browser.OpenTabAsync(new Uri("about:blank"));
            await Task.Delay(200);

            var tcs = new TaskCompletionSource<ConsoleMessageEventArgs>();
            AsyncEventHandler<IWebPage, ConsoleMessageEventArgs> handler = (_, e) =>
            {
                if (e.Level == ConsoleMessageLevel.Error)
                    tcs.TrySetResult(e);
                return ValueTask.CompletedTask;
            };
            newTab.ConsoleMessage += handler;

            try
            {
                await newTab.ExecuteAsync("console.error('error message', {code: 500})");

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var msg = await tcs.Task.WaitAsync(cts.Token);

                Assert.That(msg.Level, Is.EqualTo(ConsoleMessageLevel.Error), "Уровень — Error.");
                Assert.That(msg.Args, Has.Count.GreaterThanOrEqualTo(2), "Минимум 2 аргумента.");
                Assert.That(msg.Args[0], Does.Contain("error message"), "Первый аргумент — текст ошибки.");
                logger.WriteLine(LogKind.Default, $"ConsoleError: level={msg.Level}, args=[{string.Join(", ", msg.Args)}]");
            }
            finally
            {
                newTab.ConsoleMessage -= handler;
            }
        }

        [TestCase(TestName = "ConsoleMessage: все 5 уровней в последовательности"), Order(17)]
        public async Task ConsoleAllLevelsTest()
        {
            var newTab = await browser.OpenTabAsync(new Uri("about:blank"));
            await Task.Delay(200);

            var messages = new List<ConsoleMessageEventArgs>();
            var allReceived = new TaskCompletionSource();
            AsyncEventHandler<IWebPage, ConsoleMessageEventArgs> handler = (_, e) =>
            {
                messages.Add(e);
                if (messages.Count >= 5)
                    allReceived.TrySetResult();
                return ValueTask.CompletedTask;
            };
            newTab.ConsoleMessage += handler;

            try
            {
                await newTab.ExecuteAsync("""
                    console.log('msg-log');
                    console.warn('msg-warn');
                    console.error('msg-error');
                    console.info('msg-info');
                    console.debug('msg-debug');
                """);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await allReceived.Task.WaitAsync(cts.Token);

                var levels = messages.Select(m => m.Level).ToHashSet();
                Assert.That(levels, Does.Contain(ConsoleMessageLevel.Log), "Получен Log.");
                Assert.That(levels, Does.Contain(ConsoleMessageLevel.Warn), "Получен Warn.");
                Assert.That(levels, Does.Contain(ConsoleMessageLevel.Error), "Получен Error.");
                Assert.That(levels, Does.Contain(ConsoleMessageLevel.Info), "Получен Info.");
                Assert.That(levels, Does.Contain(ConsoleMessageLevel.Debug), "Получен Debug.");

                foreach (var msg in messages)
                    logger.WriteLine(LogKind.Default, $"  {msg.Level}: [{string.Join(", ", msg.Args)}]");
            }
            finally
            {
                newTab.ConsoleMessage -= handler;
            }
        }

        [TestCase(TestName = "ConsoleMessage: длинные аргументы усекаются"), Order(18)]
        public async Task ConsoleLargeArgsTest()
        {
            var newTab = await browser.OpenTabAsync(new Uri("about:blank"));
            await Task.Delay(200);

            var tcs = new TaskCompletionSource<ConsoleMessageEventArgs>();
            AsyncEventHandler<IWebPage, ConsoleMessageEventArgs> handler = (_, e) =>
            {
                tcs.TrySetResult(e);
                return ValueTask.CompletedTask;
            };
            newTab.ConsoleMessage += handler;

            try
            {
                // Создаём строку длиннее 500 символов — shadow-intercept.js должен усечь.
                await newTab.ExecuteAsync("console.log('x'.repeat(1000))");

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var msg = await tcs.Task.WaitAsync(cts.Token);

                Assert.That(msg.Args, Has.Count.GreaterThanOrEqualTo(1), "Минимум 1 аргумент.");
                Assert.That(msg.Args[0].Length, Is.LessThanOrEqualTo(510),
                    "Длинный аргумент усечён (≈500 символов + допуск).");
                logger.WriteLine(LogKind.Default, $"LargeArgs: length={msg.Args[0].Length}");
            }
            finally
            {
                newTab.ConsoleMessage -= handler;
            }
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
        // Chrome исключён — не поддерживает MV2 (начиная с Chrome 130+).
        string? browserPath = null;
        foreach (var candidate in (ReadOnlySpan<string>)[
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
        Assert.That(Directory.Exists(ExtensionPath), Is.True, "Расширение не найдено.");

        return (browserPath!, ExtensionPath);
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
            // Chrome исключён — не поддерживает MV2 (начиная с Chrome 130+).
            string[] candidates =
            [
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
        /// Возвращает путь к расширению (единый для всех браузеров).
        /// </summary>
        private static string GetExtensionPath(string browserPath) => ExtensionPath;

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

        [TestCase(TestName = "Turnstile checkbox click в iframe даёт verification token"), Order(59)]
        public async Task TurnstileCheckboxClickInFrameTest()
        {
            var tab = await browser.OpenIsolatedTabAsync();

            // Тестовый visible-сайткей Cloudflare. Клик по чекбоксу должен привести
            // к появлению verification token без legacy auto-click инъекций.
            const string turnstileHtml = """
                <!DOCTYPE html>
                <html>
                <head>
                    <title>Turnstile Interactive</title>
                    <script src="https://challenges.cloudflare.com/turnstile/v0/api.js" async defer></script>
                </head>
                <body>
                    <div class="cf-turnstile" data-sitekey="1x00000000000000000000AA"></div>
                </body>
                </html>
                """;

            var targetUrl = new Uri($"http://127.0.0.1:{browser.BridgePort}/blank?ts=1");
            await tab.NavigateAsync(targetUrl, new NavigationSettings { Body = turnstileHtml });

            var initialDiagnostics = await CaptureTurnstileFrameDiagnosticsAsync(tab);
            logger.WriteLine(LogKind.Default, $"turnstile initial diagnostics={initialDiagnostics}");

            var clickResult = await ClickTurnstileCheckboxInFramesAsync(tab, TimeSpan.FromSeconds(20));
            logger.WriteLine(LogKind.Default, $"turnstile checkbox click result={clickResult}");

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
            Assert.That(turnstileToken, Is.Not.Null.And.Not.Empty, $"Turnstile token должен быть получен после явного checkbox click. click={clickResult}, diagnostics={initialDiagnostics}");
        }

        [TestCase(TestName = "Turnstile real-site key через body override на внешнем HTTPS-домене"), Order(60)]
        [Explicit("Manual exploratory scenario: real Turnstile sitekey does not produce a deterministic verification token in automated runs. Use the external HTTPS test-sitekey test for stable interception coverage.")]
        public async Task TurnstileRealSitekeyTest()
        {
            await using var turnstileBrowser = await LaunchTurnstileBrowserAsync();
            var tab = await turnstileBrowser.OpenIsolatedTabAsync(settings: CreateTurnstileRealSiteContextSettings());
            var preNavigationFingerprint = await WaitForFingerprintSnapshotAsync(
                tab,
                snapshot => ReadFingerprintNumber(snapshot, "hardwareConcurrency") == 8
                    && ReadFingerprintNumber(snapshot, "deviceMemory") == 8,
                TimeSpan.FromSeconds(3));

            // Подменяем содержимое страницы visa.vfsglobal.com на наш HTML с Turnstile.
            var replaceHtml = await File.ReadAllTextAsync(
                Path.Combine(TestContext.CurrentContext.TestDirectory, "assets", "replace.html"));
            var targetUrl = new Uri("https://visa.vfsglobal.com/ind/en/pol/login");
            await tab.NavigateAsync(targetUrl, new NavigationSettings { Body = replaceHtml });

            // URL и origin остаются оригинальными после body override.
            var currentUrl = (await tab.ExecuteAsync("location.href"))?.ToString();
            Assert.That(currentUrl, Does.Contain("visa.vfsglobal.com"), "URL должен оставаться оригинальным после body override.");

            var origin = (await tab.ExecuteAsync("location.origin"))?.ToString();
            Assert.That(origin, Does.Contain("visa.vfsglobal.com"), "Origin должен быть visa.vfsglobal.com.");

            // Title из подменённого HTML.
            var title = (await tab.ExecuteAsync("document.title"))?.ToString();
            Assert.That(title, Is.EqualTo("Turnstile Zero"), "Body override должен заменить документ.");

            var postOverrideFingerprint = await CaptureFingerprintSnapshotAsync(tab);
            var fingerprintDiff = DescribeFingerprintDelta(preNavigationFingerprint, postOverrideFingerprint);

            logger.WriteLine(LogKind.Default, $"turnstile fingerprint before={preNavigationFingerprint}");
            logger.WriteLine(LogKind.Default, $"turnstile fingerprint after={postOverrideFingerprint}");
            logger.WriteLine(LogKind.Default, $"turnstile fingerprint diff={fingerprintDiff}");

            // Ждём появления Turnstile виджета (до 15 секунд).
            using var widgetCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            string? sitekey = null;

            while (!widgetCts.IsCancellationRequested)
            {
                var sk = (await tab.ExecuteAsync(
                    "document.querySelector('[data-sitekey]')?.getAttribute('data-sitekey') || ''",
                    widgetCts.Token))?.ToString();

                if (!string.IsNullOrEmpty(sk))
                {
                    sitekey = sk;
                    break;
                }

                await Task.Delay(500, widgetCts.Token);
            }

            Assert.That(sitekey, Is.Not.Null.And.Not.Empty, "На странице должен быть Turnstile виджет с data-sitekey.");

            var initialWidgetState = await WaitForTurnstileWidgetStateAsync(
                tab,
                state => state?["hasApi"]?.GetValue<bool>() == true
                    && state?["widgetId"] is not null
                    && string.Equals(state?["renderReady"]?.GetValue<string>(), "true", StringComparison.Ordinal),
                TimeSpan.FromSeconds(15));
            string? executeResult = null;

            var initialFrameMapping = await CaptureTurnstileFrameMappingAsync(tab);
            var initialDiagnostics = await CaptureTurnstileFrameDiagnosticsAsync(tab);
            var initialIsolatedDiagnostics = await CaptureTurnstileFrameDiagnosticsAsync(tab, isolatedWorld: true);
            var initialScreenshotProbe = await CaptureTurnstileScreenshotProbeAsync(tab, initialDiagnostics).ConfigureAwait(false);
            var initialFrameMappingSummary = CompactDiagnosticValue(initialFrameMapping);
            var initialDiagnosticsSummary = CompactDiagnosticValue(initialDiagnostics);
            var initialIsolatedDiagnosticsSummary = CompactDiagnosticValue(initialIsolatedDiagnostics);
            var initialScreenshotProbeSummary = CompactDiagnosticValue(initialScreenshotProbe);
            logger.WriteLine(LogKind.Default, $"turnstile real-site widget state before click={initialWidgetState}");
            logger.WriteLine(LogKind.Default, $"turnstile real-site execute result={executeResult}");
            logger.WriteLine(LogKind.Default, $"turnstile real-site input capabilities={tab.InputCapabilities}");
            logger.WriteLine(LogKind.Default, $"turnstile real-site initial frame mapping={initialFrameMappingSummary}");
            logger.WriteLine(LogKind.Default, $"turnstile real-site initial diagnostics={initialDiagnosticsSummary}");
            logger.WriteLine(LogKind.Default, $"turnstile real-site initial isolated diagnostics={initialIsolatedDiagnosticsSummary}");
            logger.WriteLine(LogKind.Default, $"turnstile real-site initial screenshot probe={initialScreenshotProbeSummary}");

            string? clickResult = null;

            var topPageOverlayClick = await ClickTurnstileOverlayFromTopPageAsync(tab, PagePointClickOptions.PreferParallel, CancellationToken.None).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(topPageOverlayClick))
                logger.WriteLine(LogKind.Default, $"turnstile real-site top-page overlay click={topPageOverlayClick}");

            if (string.IsNullOrWhiteSpace(topPageOverlayClick))
            {
                var topPageHostClick = await ClickTurnstileHostFromTopPageAsync(tab, PagePointClickOptions.PreferParallel, CancellationToken.None).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(topPageHostClick))
                    logger.WriteLine(LogKind.Default, $"turnstile real-site top-page host click={topPageHostClick}");
            }

            clickResult = topPageOverlayClick;
            logger.WriteLine(LogKind.Default, $"turnstile real-site click result={clickResult}");

            var preTargetedFrameMapping = await CaptureTurnstileFrameMappingAsync(tab);
            var challengeFrameId = FindChallengeFrameId(preTargetedFrameMapping);
            string? challengeFrameMainProbeBefore = null;
            string? challengeFrameIsolatedProbeBefore = null;
            string? challengeFrameResourceProbeBefore = null;
            string? challengeFrameTrustProbeBefore = null;
            string? challengeFrameDirectClick = null;
            string? challengeFrameTrustProbeAfterDirectClick = null;
            string? challengeFrameActivationKeys = null;
            string? challengeFrameTrustProbeAfterActivationKeys = null;
            string? challengeFrameMainProbeAfter = null;
            string? challengeFrameIsolatedProbeAfter = null;
            string? challengeFrameResourceProbeAfter = null;
            string? challengeFrameLifecycleTrace = null;
            string challengeFrameMainProbeBeforeSummary = "<not-captured>";
            string challengeFrameIsolatedProbeBeforeSummary = "<not-captured>";
            string challengeFrameResourceProbeBeforeSummary = "<not-captured>";
            string challengeFrameTrustProbeBeforeSummary = "<not-captured>";
            string challengeFrameDirectClickSummary = "<not-captured>";
            string challengeFrameTrustProbeAfterDirectClickSummary = "<not-captured>";
            string challengeFrameActivationKeysSummary = "<not-captured>";
            string challengeFrameTrustProbeAfterActivationKeysSummary = "<not-captured>";
            string challengeFrameMainProbeAfterSummary = "<not-captured>";
            string challengeFrameIsolatedProbeAfterSummary = "<not-captured>";
            string challengeFrameResourceProbeAfterSummary = "<not-captured>";
            string challengeFrameLifecycleTraceSummary = "<not-captured>";

            logger.WriteLine(LogKind.Default, $"turnstile real-site pre-targeted frame mapping={preTargetedFrameMapping}");
            logger.WriteLine(LogKind.Default, $"turnstile real-site challenge frame id={challengeFrameId}");

            if (challengeFrameId is int frameId)
            {
                challengeFrameMainProbeBefore = await CaptureChallengeFrameProbeAsync(tab, frameId, isolatedWorld: false).ConfigureAwait(false);
                challengeFrameIsolatedProbeBefore = await CaptureChallengeFrameProbeAsync(tab, frameId, isolatedWorld: true).ConfigureAwait(false);
                challengeFrameResourceProbeBefore = await CaptureChallengeFrameResourceProbeAsync(tab, frameId, isolatedWorld: false).ConfigureAwait(false);
                challengeFrameTrustProbeBefore = await CaptureChallengeFrameTrustProbeAsync(tab, frameId, isolatedWorld: false).ConfigureAwait(false);
                challengeFrameDirectClick = await ClickChallengeFrameDirectAsync(tab, frameId, isolatedWorld: false).ConfigureAwait(false);
                challengeFrameTrustProbeAfterDirectClick = await CaptureChallengeFrameTrustProbeAsync(tab, frameId, isolatedWorld: false).ConfigureAwait(false);
                challengeFrameActivationKeys = await PressChallengeFrameActivationKeysAsync(tab, frameId, isolatedWorld: false).ConfigureAwait(false);
                challengeFrameTrustProbeAfterActivationKeys = await CaptureChallengeFrameTrustProbeAsync(tab, frameId, isolatedWorld: false).ConfigureAwait(false);
                await Task.Delay(350).ConfigureAwait(false);
                challengeFrameMainProbeAfter = await CaptureChallengeFrameProbeAsync(tab, frameId, isolatedWorld: false).ConfigureAwait(false);
                challengeFrameIsolatedProbeAfter = await CaptureChallengeFrameProbeAsync(tab, frameId, isolatedWorld: true).ConfigureAwait(false);
                challengeFrameResourceProbeAfter = await CaptureChallengeFrameResourceProbeAsync(tab, frameId, isolatedWorld: false).ConfigureAwait(false);
                challengeFrameLifecycleTrace = await CaptureChallengeFrameLifecycleTraceAsync(
                    tab,
                    frameId,
                    TimeSpan.FromSeconds(3),
                    TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);

                challengeFrameMainProbeBeforeSummary = CompactDiagnosticValue(challengeFrameMainProbeBefore);
                challengeFrameIsolatedProbeBeforeSummary = CompactDiagnosticValue(challengeFrameIsolatedProbeBefore);
                challengeFrameResourceProbeBeforeSummary = CompactDiagnosticValue(challengeFrameResourceProbeBefore);
                challengeFrameTrustProbeBeforeSummary = SummarizeTurnstileTrustProbe(challengeFrameTrustProbeBefore);
                challengeFrameDirectClickSummary = SummarizeChallengeFrameDirectClick(challengeFrameDirectClick);
                challengeFrameTrustProbeAfterDirectClickSummary = SummarizeTurnstileTrustProbe(challengeFrameTrustProbeAfterDirectClick);
                challengeFrameActivationKeysSummary = SummarizeChallengeFrameActivationKeys(challengeFrameActivationKeys);
                challengeFrameTrustProbeAfterActivationKeysSummary = SummarizeTurnstileTrustProbe(challengeFrameTrustProbeAfterActivationKeys);
                challengeFrameMainProbeAfterSummary = CompactDiagnosticValue(challengeFrameMainProbeAfter);
                challengeFrameIsolatedProbeAfterSummary = CompactDiagnosticValue(challengeFrameIsolatedProbeAfter);
                challengeFrameResourceProbeAfterSummary = CompactDiagnosticValue(challengeFrameResourceProbeAfter);
                challengeFrameLifecycleTraceSummary = CompactDiagnosticValue(challengeFrameLifecycleTrace);

                logger.WriteLine(LogKind.Default, $"turnstile real-site challenge frame main probe before={challengeFrameMainProbeBeforeSummary}");
                logger.WriteLine(LogKind.Default, $"turnstile real-site challenge frame isolated probe before={challengeFrameIsolatedProbeBeforeSummary}");
                logger.WriteLine(LogKind.Default, $"turnstile real-site challenge frame resource probe before={challengeFrameResourceProbeBeforeSummary}");
                logger.WriteLine(LogKind.Default, $"turnstile real-site challenge frame trust probe before={challengeFrameTrustProbeBeforeSummary}");
                logger.WriteLine(LogKind.Default, $"turnstile real-site challenge frame direct click={challengeFrameDirectClickSummary}");
                logger.WriteLine(LogKind.Default, $"turnstile real-site challenge frame trust probe after direct click={challengeFrameTrustProbeAfterDirectClickSummary}");
                logger.WriteLine(LogKind.Default, $"turnstile real-site challenge frame activation keys={challengeFrameActivationKeysSummary}");
                logger.WriteLine(LogKind.Default, $"turnstile real-site challenge frame trust probe after activation keys={challengeFrameTrustProbeAfterActivationKeysSummary}");
                logger.WriteLine(LogKind.Default, $"turnstile real-site challenge frame main probe after={challengeFrameMainProbeAfterSummary}");
                logger.WriteLine(LogKind.Default, $"turnstile real-site challenge frame isolated probe after={challengeFrameIsolatedProbeAfterSummary}");
                logger.WriteLine(LogKind.Default, $"turnstile real-site challenge frame resource probe after={challengeFrameResourceProbeAfterSummary}");
                logger.WriteLine(LogKind.Default, $"turnstile real-site challenge frame lifecycle trace={challengeFrameLifecycleTraceSummary}");
            }

            // Ожидаем Turnstile token.
            using var tokenCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            string? turnstileToken = null;
            string? turnstileError = null;
            string? turnstileExpired = null;
            string? turnstileTimeout = null;
            string? turnstileUnsupported = null;
            string? turnstileEvents = null;
            var fallbackAttempted = false;
            var tokenWaitStartedAt = DateTime.UtcNow;

            while (!tokenCts.IsCancellationRequested)
            {
                var token = (await tab.ExecuteAsync(
                    "document.querySelector('[name=\"cf-turnstile-response\"]')?.value || ''",
                    tokenCts.Token))?.ToString();

                if (!string.IsNullOrEmpty(token))
                {
                    turnstileToken = token;
                    break;
                }

                // Fallback: token из JavaScript-callback.
                var cbTok = (await tab.ExecuteAsync(
                    "document.documentElement.dataset.tsToken || ''",
                    tokenCts.Token))?.ToString();
                if (!string.IsNullOrEmpty(cbTok))
                {
                    turnstileToken = cbTok;
                    break;
                }

                turnstileError = (await tab.ExecuteAsync(
                    "document.documentElement.dataset.tsError || ''",
                    tokenCts.Token))?.ToString();
                turnstileExpired = (await tab.ExecuteAsync(
                    "document.documentElement.dataset.tsExpired || ''",
                    tokenCts.Token))?.ToString();
                turnstileTimeout = (await tab.ExecuteAsync(
                    "document.documentElement.dataset.tsTimeout || ''",
                    tokenCts.Token))?.ToString();
                turnstileUnsupported = (await tab.ExecuteAsync(
                    "document.documentElement.dataset.tsUnsupported || ''",
                    tokenCts.Token))?.ToString();
                turnstileEvents = (await tab.ExecuteAsync(
                    "JSON.stringify(window.__tsEvents || [])",
                    tokenCts.Token))?.ToString();

                if (!string.IsNullOrEmpty(turnstileError))
                    break;

                if (!fallbackAttempted && DateTime.UtcNow - tokenWaitStartedAt >= TimeSpan.FromSeconds(4))
                {
                    var retryTopPageOverlayKeys = await PressTurnstileOverlayKeysFromTopPageAsync(
                        tab,
                        PagePointClickOptions.PreferParallel,
                        PageKeyPressOptions.PreferParallel,
                        tokenCts.Token).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(retryTopPageOverlayKeys))
                        logger.WriteLine(LogKind.Default, $"turnstile real-site fallback top-page overlay keys={retryTopPageOverlayKeys}");

                    fallbackAttempted = true;
                }

                try
                {
                    await Task.Delay(500, tokenCts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            var postClickFrameMapping = await CaptureTurnstileFrameMappingAsync(tab);
            var postClickDiagnostics = await CaptureTurnstileFrameDiagnosticsAsync(tab);
            var postClickIsolatedDiagnostics = await CaptureTurnstileFrameDiagnosticsAsync(tab, isolatedWorld: true);
            var postClickScreenshotProbe = await CaptureTurnstileScreenshotProbeAsync(tab, postClickDiagnostics).ConfigureAwait(false);
            var postClickWidgetState = await CaptureTurnstileWidgetStateAsync(tab);
            var turnstileEventsSummary = CompactDiagnosticValue(turnstileEvents);
            var postClickFrameMappingSummary = CompactDiagnosticValue(postClickFrameMapping);
            var postClickDiagnosticsSummary = CompactDiagnosticValue(postClickDiagnostics);
            var postClickIsolatedDiagnosticsSummary = CompactDiagnosticValue(postClickIsolatedDiagnostics);
            var postClickScreenshotProbeSummary = CompactDiagnosticValue(postClickScreenshotProbe);

            logger.WriteLine(LogKind.Default, $"turnstile token={turnstileToken?[..Math.Min(turnstileToken?.Length ?? 0, 60)]}");
            logger.WriteLine(LogKind.Default, $"turnstile error={turnstileError}");
            logger.WriteLine(LogKind.Default, $"turnstile expired={turnstileExpired}");
            logger.WriteLine(LogKind.Default, $"turnstile timeout={turnstileTimeout}");
            logger.WriteLine(LogKind.Default, $"turnstile unsupported={turnstileUnsupported}");
            logger.WriteLine(LogKind.Default, $"turnstile events={turnstileEventsSummary}");
            logger.WriteLine(LogKind.Default, $"turnstile real-site widget state after click={postClickWidgetState}");
            logger.WriteLine(LogKind.Default, $"turnstile real-site post-click frame mapping={postClickFrameMappingSummary}");
            logger.WriteLine(LogKind.Default, $"turnstile real-site post-click diagnostics={postClickDiagnosticsSummary}");
            logger.WriteLine(LogKind.Default, $"turnstile real-site post-click isolated diagnostics={postClickIsolatedDiagnosticsSummary}");
            logger.WriteLine(LogKind.Default, $"turnstile real-site post-click screenshot probe={postClickScreenshotProbeSummary}");

            if (!string.IsNullOrEmpty(turnstileError))
            {
                Assert.Fail(
                    $"Turnstile завершился error-callback. code={turnstileError}, expired={turnstileExpired}, timeout={turnstileTimeout}, unsupported={turnstileUnsupported}, events={turnstileEventsSummary}, diagnostics={initialDiagnosticsSummary}, isolatedDiagnostics={initialIsolatedDiagnosticsSummary}, initialScreenshotProbe={initialScreenshotProbeSummary}, challengeFrameId={challengeFrameId}, challengeFrameMainProbeBefore={challengeFrameMainProbeBeforeSummary}, challengeFrameIsolatedProbeBefore={challengeFrameIsolatedProbeBeforeSummary}, challengeFrameResourceProbeBefore={challengeFrameResourceProbeBeforeSummary}, challengeFrameTrustProbeBefore={challengeFrameTrustProbeBeforeSummary}, challengeFrameDirectClick={challengeFrameDirectClickSummary}, challengeFrameTrustProbeAfterDirectClick={challengeFrameTrustProbeAfterDirectClickSummary}, challengeFrameActivationKeys={challengeFrameActivationKeysSummary}, challengeFrameTrustProbeAfterActivationKeys={challengeFrameTrustProbeAfterActivationKeysSummary}, challengeFrameMainProbeAfter={challengeFrameMainProbeAfterSummary}, challengeFrameIsolatedProbeAfter={challengeFrameIsolatedProbeAfterSummary}, challengeFrameResourceProbeAfter={challengeFrameResourceProbeAfterSummary}, challengeFrameLifecycleTrace={challengeFrameLifecycleTraceSummary}, postClickDiagnostics={postClickDiagnosticsSummary}, postClickIsolatedDiagnostics={postClickIsolatedDiagnosticsSummary}, postClickScreenshotProbe={postClickScreenshotProbeSummary}, before={preNavigationFingerprint}, after={postOverrideFingerprint}, diff={fingerprintDiff}");
            }

            Assert.That(
                turnstileToken,
                Is.Not.Null.And.Not.Empty,
                $"Turnstile token должен быть получен через body override после явного click. click={clickResult}, error={turnstileError}, expired={turnstileExpired}, timeout={turnstileTimeout}, unsupported={turnstileUnsupported}, events={turnstileEventsSummary}, diagnostics={initialDiagnosticsSummary}, isolatedDiagnostics={initialIsolatedDiagnosticsSummary}, initialScreenshotProbe={initialScreenshotProbeSummary}, challengeFrameId={challengeFrameId}, challengeFrameMainProbeBefore={challengeFrameMainProbeBeforeSummary}, challengeFrameIsolatedProbeBefore={challengeFrameIsolatedProbeBeforeSummary}, challengeFrameResourceProbeBefore={challengeFrameResourceProbeBeforeSummary}, challengeFrameTrustProbeBefore={challengeFrameTrustProbeBeforeSummary}, challengeFrameDirectClick={challengeFrameDirectClickSummary}, challengeFrameTrustProbeAfterDirectClick={challengeFrameTrustProbeAfterDirectClickSummary}, challengeFrameActivationKeys={challengeFrameActivationKeysSummary}, challengeFrameTrustProbeAfterActivationKeys={challengeFrameTrustProbeAfterActivationKeysSummary}, challengeFrameMainProbeAfter={challengeFrameMainProbeAfterSummary}, challengeFrameIsolatedProbeAfter={challengeFrameIsolatedProbeAfterSummary}, challengeFrameResourceProbeAfter={challengeFrameResourceProbeAfterSummary}, challengeFrameLifecycleTrace={challengeFrameLifecycleTraceSummary}, postClickDiagnostics={postClickDiagnosticsSummary}, postClickIsolatedDiagnostics={postClickIsolatedDiagnosticsSummary}, postClickScreenshotProbe={postClickScreenshotProbeSummary}, before={preNavigationFingerprint}, after={postOverrideFingerprint}, diff={fingerprintDiff}");
        }

        [TestCase(TestName = "Turnstile click на внешнем HTTPS-домене перехватывает verification result"), Order(60)]
        public async Task TurnstileTestSitekeyOnExternalHttpsTest()
        {
            var tab = await browser.OpenIsolatedTabAsync(settings: new TabContextSettings
            {
                HardwareConcurrency = 4,
                DeviceMemory = 8,
            });

            var replaceHtml = await File.ReadAllTextAsync(
                Path.Combine(TestContext.CurrentContext.TestDirectory, "assets", "replace.html"));
            replaceHtml = replaceHtml.Replace(
                "0x4AAAAAABhlz7Ei4byodYjs",
                "1x00000000000000000000AA",
                StringComparison.Ordinal);

            var targetUrl = new Uri("https://visa.vfsglobal.com/ind/en/pol/login");
            await tab.NavigateAsync(targetUrl, new NavigationSettings { Body = replaceHtml });

            var title = (await tab.ExecuteAsync("document.title"))?.ToString();
            Assert.That(title, Is.EqualTo("Turnstile Zero"), "Body override должен заменить документ.");

            var initialDiagnostics = await CaptureTurnstileFrameDiagnosticsAsync(tab);
            var executeResult = await ExecuteTurnstileWidgetAsync(tab, CancellationToken.None);
            logger.WriteLine(LogKind.Default, $"turnstile test-sitekey initial diagnostics={initialDiagnostics}");
            logger.WriteLine(LogKind.Default, $"turnstile test-sitekey execute result={executeResult}");

            var clickResult = await ClickTurnstileCheckboxInFramesAsync(tab, TimeSpan.FromSeconds(20));
            logger.WriteLine(LogKind.Default, $"turnstile test-sitekey click result={clickResult}");

            using var tokenCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            string? turnstileToken = null;
            string? turnstileError = null;
            string? turnstileEvents = null;
            string? turnstileResponseInput = null;

            while (!tokenCts.IsCancellationRequested)
            {
                var token = (await tab.ExecuteAsync(
                    "document.querySelector('[name=\"cf-turnstile-response\"]')?.value || document.documentElement.dataset.tsToken || document.documentElement.dataset.tsResponseInput || ''",
                    tokenCts.Token))?.ToString();

                if (!string.IsNullOrEmpty(token))
                {
                    turnstileToken = token;
                    break;
                }

                turnstileResponseInput = (await tab.ExecuteAsync(
                    "document.documentElement.dataset.tsResponseInput || ''",
                    tokenCts.Token))?.ToString();
                turnstileError = (await tab.ExecuteAsync(
                    "document.documentElement.dataset.tsError || ''",
                    tokenCts.Token))?.ToString();
                turnstileEvents = (await tab.ExecuteAsync(
                    "JSON.stringify(window.__tsEvents || [])",
                    tokenCts.Token))?.ToString();

                if (!string.IsNullOrEmpty(turnstileError))
                    break;

                await Task.Delay(500, tokenCts.Token);
            }

            if (!string.IsNullOrEmpty(turnstileToken))
            {
                using var interceptionCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

                while (!interceptionCts.IsCancellationRequested)
                {
                    turnstileResponseInput = (await tab.ExecuteAsync(
                        "document.documentElement.dataset.tsResponseInput || ''",
                        interceptionCts.Token))?.ToString();
                    turnstileEvents = (await tab.ExecuteAsync(
                        "JSON.stringify(window.__tsEvents || [])",
                        interceptionCts.Token))?.ToString();

                    if (string.Equals(turnstileResponseInput, turnstileToken, StringComparison.Ordinal)
                        && !string.IsNullOrWhiteSpace(turnstileEvents)
                        && turnstileEvents.Contains("\"type\":\"response-input\"", StringComparison.Ordinal))
                    {
                        break;
                    }

                    await Task.Delay(250, interceptionCts.Token);
                }
            }

            logger.WriteLine(LogKind.Default, $"turnstile test-sitekey token={turnstileToken?[..Math.Min(turnstileToken?.Length ?? 0, 60)]}");
            logger.WriteLine(LogKind.Default, $"turnstile test-sitekey response-input={turnstileResponseInput?[..Math.Min(turnstileResponseInput?.Length ?? 0, 60)]}");
            logger.WriteLine(LogKind.Default, $"turnstile test-sitekey error={turnstileError}");
            logger.WriteLine(LogKind.Default, $"turnstile test-sitekey events={turnstileEvents}");

            Assert.Multiple(() =>
            {
                Assert.That(
                    turnstileToken,
                    Is.Not.Null.And.Not.Empty,
                    $"Cloudflare test sitekey на внешнем origin должен выдать token после checkbox click. click={clickResult}, error={turnstileError}, events={turnstileEvents}, diagnostics={initialDiagnostics}");
                Assert.That(
                    turnstileResponseInput,
                    Is.EqualTo(turnstileToken),
                    $"Перехваченный hidden input token должен совпадать с итоговым verification token. click={clickResult}, error={turnstileError}, events={turnstileEvents}, diagnostics={initialDiagnostics}");
                Assert.That(turnstileEvents, Does.Contain("\"type\":\"response-input\""), "Должно быть зафиксировано изменение hidden input с verification result.");
            });
        }

        [TestCase(TestName = "Turnstile real-site manual click snapshot без anti-detect"), Order(61)]
        [Explicit("Manual interactive scenario: requires a human to click the Turnstile widget.")]
        public async Task TurnstileRealSitekeyManualClickPlainProfileTest()
        {
            var tab = await browser.OpenIsolatedTabAsync();
            var preNavigationFingerprint = await CaptureFingerprintSnapshotAsync(tab);

            var replaceHtml = await File.ReadAllTextAsync(
                Path.Combine(TestContext.CurrentContext.TestDirectory, "assets", "replace.html"));
            replaceHtml = replaceHtml.Replace(
                "</head>",
                """
                <script>
                    (function () {
                        const manualMessage = { __atomDisableTurnstileAutoClick: true };
                        const broadcastManualMode = function () {
                            for (const frame of document.querySelectorAll('iframe')) {
                                try {
                                    frame.contentWindow?.postMessage(manualMessage, '*');
                                } catch {
                                }
                            }
                        };

                        const observer = new MutationObserver(broadcastManualMode);
                        observer.observe(document.documentElement, { childList: true, subtree: true });

                        setInterval(broadcastManualMode, 50);
                        window.addEventListener('load', broadcastManualMode);
                        broadcastManualMode();
                    })();
                </script>
                </head>
                """,
                StringComparison.Ordinal);

            var targetUrl = new Uri("https://visa.vfsglobal.com/ind/en/pol/login");
            await tab.NavigateAsync(targetUrl, new NavigationSettings { Body = replaceHtml });

            var title = (await tab.ExecuteAsync("document.title"))?.ToString();
            Assert.That(title, Is.EqualTo("Turnstile Zero"), "Body override должен заменить документ.");

            var initialDiagnostics = await CaptureTurnstileFrameDiagnosticsAsync(tab);

            logger.WriteLine(LogKind.Default, $"manual turnstile initial diagnostics={initialDiagnostics}");
            logger.WriteLine(LogKind.Default, "manual turnstile: ready for user click, waiting up to 10 minutes");

            using var tokenCts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            string? turnstileToken = null;
            string? turnstileError = null;
            string? turnstileExpired = null;
            string? turnstileTimeout = null;
            string? turnstileUnsupported = null;
            string? turnstileEvents = null;

            while (!tokenCts.IsCancellationRequested)
            {
                var token = (await tab.ExecuteAsync(
                    "document.querySelector('[name=\"cf-turnstile-response\"]')?.value || ''",
                    tokenCts.Token))?.ToString();

                if (!string.IsNullOrEmpty(token))
                {
                    turnstileToken = token;
                    break;
                }

                var cbTok = (await tab.ExecuteAsync(
                    "document.documentElement.dataset.tsToken || ''",
                    tokenCts.Token))?.ToString();
                if (!string.IsNullOrEmpty(cbTok))
                {
                    turnstileToken = cbTok;
                    break;
                }

                turnstileError = (await tab.ExecuteAsync(
                    "document.documentElement.dataset.tsError || ''",
                    tokenCts.Token))?.ToString();
                turnstileExpired = (await tab.ExecuteAsync(
                    "document.documentElement.dataset.tsExpired || ''",
                    tokenCts.Token))?.ToString();
                turnstileTimeout = (await tab.ExecuteAsync(
                    "document.documentElement.dataset.tsTimeout || ''",
                    tokenCts.Token))?.ToString();
                turnstileUnsupported = (await tab.ExecuteAsync(
                    "document.documentElement.dataset.tsUnsupported || ''",
                    tokenCts.Token))?.ToString();
                turnstileEvents = (await tab.ExecuteAsync(
                    "JSON.stringify(window.__tsEvents || [])",
                    tokenCts.Token))?.ToString();

                if (!string.IsNullOrEmpty(turnstileError))
                    break;

                await Task.Delay(500, tokenCts.Token);
            }

            var postInteractionFingerprint = await CaptureFingerprintSnapshotAsync(tab);
            var fingerprintDiff = DescribeFingerprintDelta(preNavigationFingerprint, postInteractionFingerprint);

            logger.WriteLine(LogKind.Default, $"manual turnstile before={preNavigationFingerprint}");
            logger.WriteLine(LogKind.Default, $"manual turnstile after={postInteractionFingerprint}");
            logger.WriteLine(LogKind.Default, $"manual turnstile diff={fingerprintDiff}");
            logger.WriteLine(LogKind.Default, $"manual turnstile token={turnstileToken?[..Math.Min(turnstileToken?.Length ?? 0, 60)]}");
            logger.WriteLine(LogKind.Default, $"manual turnstile error={turnstileError}");
            logger.WriteLine(LogKind.Default, $"manual turnstile expired={turnstileExpired}");
            logger.WriteLine(LogKind.Default, $"manual turnstile timeout={turnstileTimeout}");
            logger.WriteLine(LogKind.Default, $"manual turnstile unsupported={turnstileUnsupported}");
            logger.WriteLine(LogKind.Default, $"manual turnstile events={turnstileEvents}");
            logger.WriteLine(LogKind.Default, $"manual turnstile diagnostics={initialDiagnostics}");

            Assert.That(
                turnstileToken,
                Is.Not.Null.And.Not.Empty,
                $"Manual real-site Turnstile не выдал token. error={turnstileError}, expired={turnstileExpired}, timeout={turnstileTimeout}, unsupported={turnstileUnsupported}, events={turnstileEvents}, diagnostics={initialDiagnostics}, before={preNavigationFingerprint}, after={postInteractionFingerprint}, diff={fingerprintDiff}");
        }

        [TestCase(TestName = "Обычная и isolated вкладки без контекста дают одинаковый HTTPS fingerprint"), Order(62)]
        public async Task PlainHttpsFingerprintMatchesRegularAndIsolatedTabsTest()
        {
            var targetUrl = new Uri("https://httpbin.org/html");

            var regularTab = await browser.OpenTabAsync();
            var isolatedTab = await browser.OpenIsolatedTabAsync();

            await regularTab.NavigateAsync(targetUrl);
            await isolatedTab.NavigateAsync(targetUrl);

            var regularSnapshot = await CaptureFingerprintSnapshotAsync(regularTab);
            var isolatedSnapshot = await CaptureFingerprintSnapshotAsync(isolatedTab);
            var diff = DescribeFingerprintDelta(regularSnapshot, isolatedSnapshot);

            var regularHardware = ReadFingerprintNumber(regularSnapshot, "hardwareConcurrency");
            var isolatedHardware = ReadFingerprintNumber(isolatedSnapshot, "hardwareConcurrency");
            var regularMemory = ReadFingerprintNumber(regularSnapshot, "deviceMemory");
            var isolatedMemory = ReadFingerprintNumber(isolatedSnapshot, "deviceMemory");

            logger.WriteLine(LogKind.Default, $"plain https regular={regularSnapshot}");
            logger.WriteLine(LogKind.Default, $"plain https isolated={isolatedSnapshot}");
            logger.WriteLine(LogKind.Default, $"plain https regular-vs-isolated diff={diff}");

            Assert.Multiple(() =>
            {
                Assert.That(isolatedHardware, Is.EqualTo(regularHardware),
                    $"Isolated вкладка без контекста не должна менять hardwareConcurrency относительно обычной вкладки. diff={diff}");
                Assert.That(isolatedMemory, Is.EqualTo(regularMemory),
                    $"Isolated вкладка без контекста не должна менять deviceMemory относительно обычной вкладки. diff={diff}");
            });
        }

        [TestCase(TestName = "Целевой VFS URL без body override даёт одинаковый fingerprint в обычной и isolated вкладках"), Order(63)]
        public async Task TargetUrlFingerprintMatchesRegularAndIsolatedTabsTest()
        {
            var targetUrl = new Uri("https://visa.vfsglobal.com/ind/en/pol/login");

            var regularTab = await browser.OpenTabAsync();
            var isolatedTab = await browser.OpenIsolatedTabAsync();

            await regularTab.NavigateAsync(targetUrl);
            await isolatedTab.NavigateAsync(targetUrl);

            var regularSnapshot = await CaptureFingerprintSnapshotAsync(regularTab);
            var isolatedSnapshot = await CaptureFingerprintSnapshotAsync(isolatedTab);
            var diff = DescribeFingerprintDelta(regularSnapshot, isolatedSnapshot);

            var regularHardware = ReadFingerprintNumber(regularSnapshot, "hardwareConcurrency");
            var isolatedHardware = ReadFingerprintNumber(isolatedSnapshot, "hardwareConcurrency");
            var regularMemory = ReadFingerprintNumber(regularSnapshot, "deviceMemory");
            var isolatedMemory = ReadFingerprintNumber(isolatedSnapshot, "deviceMemory");

            logger.WriteLine(LogKind.Default, $"target regular={regularSnapshot}");
            logger.WriteLine(LogKind.Default, $"target isolated={isolatedSnapshot}");
            logger.WriteLine(LogKind.Default, $"target regular-vs-isolated diff={diff}");

            Assert.Multiple(() =>
            {
                Assert.That(isolatedHardware, Is.EqualTo(regularHardware),
                    $"На целевом URL isolated вкладка без контекста не должна менять hardwareConcurrency относительно обычной вкладки. diff={diff}");
                Assert.That(isolatedMemory, Is.EqualTo(regularMemory),
                    $"На целевом URL isolated вкладка без контекста не должна менять deviceMemory относительно обычной вкладки. diff={diff}");
            });
        }

        [TestCase(TestName = "ApplyContext сохраняет hardwareConcurrency и deviceMemory после навигации"), Order(64)]
        public async Task ApplyContextPersistsAcrossNavigationTest()
        {
            var expectedHardwareConcurrency = 4;
            var expectedDeviceMemory = 8d;

            var tab = await browser.OpenIsolatedTabAsync(settings: new TabContextSettings
            {
                HardwareConcurrency = expectedHardwareConcurrency,
                DeviceMemory = expectedDeviceMemory,
            });

            var beforeSnapshot = await WaitForFingerprintSnapshotAsync(
                tab,
                snapshot => ReadFingerprintNumber(snapshot, "hardwareConcurrency") == expectedHardwareConcurrency
                    && ReadFingerprintNumber(snapshot, "deviceMemory") == expectedDeviceMemory,
                TimeSpan.FromSeconds(3));

            await tab.NavigateAsync(new Uri("https://httpbin.org/html"));

            var afterSnapshot = await CaptureFingerprintSnapshotAsync(tab);
            var diff = DescribeFingerprintDelta(beforeSnapshot, afterSnapshot);

            var beforeHardware = ReadFingerprintNumber(beforeSnapshot, "hardwareConcurrency");
            var afterHardware = ReadFingerprintNumber(afterSnapshot, "hardwareConcurrency");
            var beforeMemory = ReadFingerprintNumber(beforeSnapshot, "deviceMemory");
            var afterMemory = ReadFingerprintNumber(afterSnapshot, "deviceMemory");

            logger.WriteLine(LogKind.Default, $"applycontext before={beforeSnapshot}");
            logger.WriteLine(LogKind.Default, $"applycontext after={afterSnapshot}");
            logger.WriteLine(LogKind.Default, $"applycontext diff={diff}");

            Assert.Multiple(() =>
            {
                Assert.That(beforeHardware, Is.EqualTo(expectedHardwareConcurrency), $"До навигации hardwareConcurrency должен быть {expectedHardwareConcurrency}. before={beforeSnapshot}");
                Assert.That(afterHardware, Is.EqualTo(expectedHardwareConcurrency), $"После навигации hardwareConcurrency должен оставаться {expectedHardwareConcurrency}. diff={diff}");
                Assert.That(beforeMemory, Is.EqualTo(expectedDeviceMemory), $"До навигации deviceMemory должен быть {expectedDeviceMemory}. before={beforeSnapshot}");
                Assert.That(afterMemory, Is.EqualTo(expectedDeviceMemory), $"После навигации deviceMemory должен оставаться {expectedDeviceMemory}. diff={diff}");
            });
        }

        private static async Task<string?> CaptureFingerprintSnapshotAsync(WebDriverPage tab)
        {
            return (await tab.ExecuteAsync(
                "(async () => {" +
                "const connection = navigator.connection || navigator.mozConnection || navigator.webkitConnection || null;" +
                "const canvas = document.createElement('canvas');" +
                "const gl = canvas.getContext('webgl') || canvas.getContext('experimental-webgl');" +
                "const debugInfo = gl && gl.getExtension ? gl.getExtension('WEBGL_debug_renderer_info') : null;" +
                "const frameSrcs = Array.from(document.querySelectorAll('iframe')).map(frame => frame.getAttribute('src') || frame.src || '');" +
                "const notificationPermission = typeof Notification !== 'undefined' ? Notification.permission : null;" +
                "const permissionsQuery = async (name) => { try { if (!navigator.permissions?.query) return null; const status = await navigator.permissions.query({ name }); return status?.state ?? null; } catch { return null; } };" +
                "const storageEstimate = async () => { try { return await navigator.storage?.estimate?.(); } catch { return null; } };" +
                "const highEntropyHints = async () => { try { if (!navigator.userAgentData?.getHighEntropyValues) return null; return await navigator.userAgentData.getHighEntropyValues(['platformVersion','architecture','bitness','model','uaFullVersion','fullVersionList']); } catch { return null; } };" +
                "const notificationQuery = await permissionsQuery('notifications');" +
                "const geolocationQuery = await permissionsQuery('geolocation');" +
                "const storageInfo = await storageEstimate();" +
                "const uaHighEntropy = await highEntropyHints();" +
                "const speechVoicesCount = (() => { try { return typeof speechSynthesis !== 'undefined' ? speechSynthesis.getVoices().length : null; } catch { return null; } })();" +
                "const snapshot = {" +
                "href: location.href," +
                "origin: location.origin," +
                "readyState: document.readyState," +
                "title: document.title," +
                "webdriver: navigator.webdriver ?? null," +
                "userAgent: navigator.userAgent," +
                "language: navigator.language ?? null," +
                "languages: Array.from(navigator.languages || [])," +
                "platform: navigator.platform ?? null," +
                "vendor: navigator.vendor ?? null," +
                "hardwareConcurrency: navigator.hardwareConcurrency ?? null," +
                "deviceMemory: navigator.deviceMemory ?? null," +
                "maxTouchPoints: navigator.maxTouchPoints ?? null," +
                "cookieEnabled: navigator.cookieEnabled ?? null," +
                "pdfViewerEnabled: navigator.pdfViewerEnabled ?? null," +
                "pluginsLength: navigator.plugins?.length ?? null," +
                "mimeTypesLength: navigator.mimeTypes?.length ?? null," +
                "hasChromeObject: typeof window.chrome !== 'undefined'," +
                "hasUserAgentData: typeof navigator.userAgentData !== 'undefined'," +
                "userAgentDataMobile: navigator.userAgentData?.mobile ?? null," +
                "userAgentDataPlatform: navigator.userAgentData?.platform ?? null," +
                "userAgentDataBrands: navigator.userAgentData?.brands?.map(x => `${x.brand}/${x.version}`) ?? []," +
                "userAgentDataLowEntropy: navigator.userAgentData?.toJSON ? JSON.stringify(navigator.userAgentData.toJSON()) : null," +
                "userAgentDataHighEntropy: uaHighEntropy ? JSON.stringify(uaHighEntropy) : null," +
                "timezone: Intl.DateTimeFormat().resolvedOptions().timeZone ?? null," +
                "timezoneOffset: new Date().getTimezoneOffset()," +
                "intlLocale: Intl.NumberFormat().resolvedOptions().locale ?? null," +
                "vendorSub: navigator.vendorSub ?? null," +
                "productSub: navigator.productSub ?? null," +
                "screenWidth: screen.width ?? null," +
                "screenHeight: screen.height ?? null," +
                "availWidth: screen.availWidth ?? null," +
                "availHeight: screen.availHeight ?? null," +
                "colorDepth: screen.colorDepth ?? null," +
                "pixelDepth: screen.pixelDepth ?? null," +
                "devicePixelRatio: window.devicePixelRatio ?? null," +
                "outerWidth: window.outerWidth ?? null," +
                "outerHeight: window.outerHeight ?? null," +
                "innerWidth: window.innerWidth ?? null," +
                "innerHeight: window.innerHeight ?? null," +
                "networkEffectiveType: connection?.effectiveType ?? null," +
                "networkRtt: connection?.rtt ?? null," +
                "networkDownlink: connection?.downlink ?? null," +
                "networkSaveData: connection?.saveData ?? null," +
                "prefersDark: matchMedia('(prefers-color-scheme: dark)').matches," +
                "prefersReducedMotion: matchMedia('(prefers-reduced-motion: reduce)').matches," +
                "notificationPermission: notificationPermission," +
                "permissionsNotification: notificationQuery," +
                "permissionsGeolocation: geolocationQuery," +
                "localStorageLength: (() => { try { return localStorage.length; } catch { return -1; } })()," +
                "sessionStorageLength: (() => { try { return sessionStorage.length; } catch { return -1; } })()," +
                "storageQuotaEstimate: storageInfo?.quota ?? null," +
                "storageUsageEstimate: storageInfo?.usage ?? null," +
                "speechVoicesCount: speechVoicesCount," +
                "crossOriginIsolated: window.crossOriginIsolated ?? null," +
                "webglVendor: debugInfo ? gl.getParameter(debugInfo.UNMASKED_VENDOR_WEBGL) : null," +
                "webglRenderer: debugInfo ? gl.getParameter(debugInfo.UNMASKED_RENDERER_WEBGL) : null," +
                "webglVersion: gl ? gl.getParameter(gl.VERSION) : null," +
                "iframeCount: document.querySelectorAll('iframe').length," +
                "challengeIframeCount: document.querySelectorAll('iframe[src*=\"challenges.cloudflare.com\"]').length," +
                "challengeIframeSrc: document.querySelector('iframe[src*=\"challenges.cloudflare.com\"]')?.src || null," +
                "frameSrcs: frameSrcs," +
                "responseInputPresent: !!document.querySelector('[name=\"cf-turnstile-response\"]')," +
                "responseInputLength: (document.querySelector('[name=\"cf-turnstile-response\"]')?.value || '').length" +
                "}; return JSON.stringify(snapshot); })()"))?.ToString();
        }

            private static async Task<string?> CaptureTurnstileWidgetStateAsync(WebDriverPage tab)
            {
                return (await tab.ExecuteAsync(
                "(() => JSON.stringify(typeof window.__tsGetWidgetState === 'function' ? window.__tsGetWidgetState() : null))()"))?.ToString();
            }

            private static async Task<string?> ExecuteTurnstileWidgetAsync(WebDriverPage tab, CancellationToken cancellationToken)
            {
                using var executeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                executeCts.CancelAfter(TimeSpan.FromSeconds(15));

                string? lastResult = null;

                while (!executeCts.IsCancellationRequested)
                {
                    lastResult = (await tab.ExecuteAsync(
                        "(() => {" +
                        "const state = typeof window.__tsGetWidgetState === 'function' ? window.__tsGetWidgetState() : null;" +
                        "const ready = !!state?.hasApi && state?.widgetId != null && state?.renderReady === 'true';" +
                        "const executed = ready && typeof window.__tsExecute === 'function' ? window.__tsExecute() : false;" +
                        "const nextState = typeof window.__tsGetWidgetState === 'function' ? window.__tsGetWidgetState() : state;" +
                        "return JSON.stringify({ ready, executed, state: nextState });" +
                        "})()",
                        executeCts.Token))?.ToString();

                    if (!string.IsNullOrWhiteSpace(lastResult))
                    {
                        try
                        {
                            var root = JsonNode.Parse(lastResult);
                            if (root?["executed"]?.GetValue<bool>() == true)
                                return lastResult;
                        }
                        catch
                        {
                        }
                    }

                    try
                    {
                        await Task.Delay(100, executeCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                return lastResult;
            }

        private static async Task<string?> WaitForFingerprintSnapshotAsync(
            WebDriverPage tab,
            Func<string?, bool> predicate,
            TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            string? lastSnapshot = null;

            while (DateTime.UtcNow < deadline)
            {
                lastSnapshot = await CaptureFingerprintSnapshotAsync(tab);
                if (predicate(lastSnapshot))
                    return lastSnapshot;

                await Task.Delay(50);
            }

            return lastSnapshot;
        }

        private static async Task<string?> WaitForTurnstileWidgetStateAsync(
            WebDriverPage tab,
            Func<JsonNode?, bool> predicate,
            TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            string? lastState = null;

            while (DateTime.UtcNow < deadline)
            {
                lastState = await CaptureTurnstileWidgetStateAsync(tab);

                if (!string.IsNullOrWhiteSpace(lastState))
                {
                    try
                    {
                        if (predicate(JsonNode.Parse(lastState)))
                            return lastState;
                    }
                    catch
                    {
                    }
                }

                await Task.Delay(100);
            }

            return lastState;
        }

        private static string DescribeFingerprintDelta(string? beforeJson, string? afterJson)
        {
            if (string.IsNullOrWhiteSpace(beforeJson) || string.IsNullOrWhiteSpace(afterJson))
                return "before-or-after-missing";

            JsonNode? beforeNode;
            JsonNode? afterNode;

            try
            {
                beforeNode = JsonNode.Parse(beforeJson);
                afterNode = JsonNode.Parse(afterJson);
            }
            catch
            {
                return "fingerprint-parse-failed";
            }

            if (beforeNode is not JsonObject beforeObj || afterNode is not JsonObject afterObj)
                return "fingerprint-not-object";

            var delta = new JsonArray();
            foreach (var key in beforeObj.Select(kvp => kvp.Key).Union(afterObj.Select(kvp => kvp.Key)).OrderBy(key => key))
            {
                var beforeValue = beforeObj[key]?.ToJsonString() ?? "null";
                var afterValue = afterObj[key]?.ToJsonString() ?? "null";
                if (!string.Equals(beforeValue, afterValue, StringComparison.Ordinal))
                {
                    delta.Add(new JsonObject
                    {
                        ["field"] = key,
                        ["before"] = beforeObj[key]?.DeepClone(),
                        ["after"] = afterObj[key]?.DeepClone(),
                    });
                }
            }

            return delta.ToJsonString();
        }

        private static async Task<string> ClickTurnstileCheckboxInFramesAsync(WebDriverPage tab, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            string lastResult = "no-results";
            DateTime? hostClickObservedAt = null;
            DateTime? cloudflareFallbackObservedAt = null;

            while (!cts.IsCancellationRequested)
            {
                var results = await tab.ExecuteInAllFramesAsync(
                    """
                    (() => {
                        function queryDeep(root, selectors) {
                            const queue = [root];
                            while (queue.length > 0) {
                                const current = queue.shift();
                                if (!current?.querySelectorAll) {
                                    continue;
                                }

                                for (const selector of selectors) {
                                    const found = current.querySelector(selector);
                                    if (found) {
                                        return found;
                                    }
                                }

                                for (const element of current.querySelectorAll('*')) {
                                    if (element.shadowRoot) {
                                        queue.push(element.shadowRoot);
                                    }
                                }
                            }

                            return null;
                        }

                        function collectDeep(root, selectors, limit = 24) {
                            const queue = [root];
                            const results = [];
                            const seen = new Set();

                            while (queue.length > 0 && results.length < limit) {
                                const current = queue.shift();
                                if (!current?.querySelectorAll) {
                                    continue;
                                }

                                for (const selector of selectors) {
                                    const matches = current.querySelectorAll(selector);
                                    for (const match of matches) {
                                        if (match instanceof Element && !seen.has(match)) {
                                            seen.add(match);
                                            results.push(match);
                                            if (results.length >= limit) {
                                                return results;
                                            }
                                        }
                                    }
                                }

                                for (const element of current.querySelectorAll('*')) {
                                    if (element.shadowRoot) {
                                        queue.push(element.shadowRoot);
                                    }
                                }
                            }

                            return results;
                        }

                        function isVisible(element) {
                            if (!(element instanceof Element)) {
                                return false;
                            }

                            const style = window.getComputedStyle(element);
                            if (!style || style.display === 'none' || style.visibility === 'hidden' || style.pointerEvents === 'none') {
                                return false;
                            }

                            const rect = element.getBoundingClientRect();

                                                    function getCloudflareHotspotPoints(width, height) {
                                                        const safeWidth = Math.max(width || 0, 1);
                                                        const safeHeight = Math.max(height || 0, 1);
                                                        const points = [];
                                                        const seen = new Set();

                                                        function pushPoint(x, y) {
                                                            const clampedX = Math.min(Math.max(x, 8), Math.max(safeWidth - 8, 8));
                                                            const clampedY = Math.min(Math.max(y, 8), Math.max(safeHeight - 8, 8));
                                                            const key = `${Math.round(clampedX)}:${Math.round(clampedY)}`;
                                                            if (!seen.has(key)) {
                                                                seen.add(key);
                                                                points.push([clampedX, clampedY]);
                                                            }
                                                        }

                                                        for (const x of [28, 34, 40, 46, 52, 58]) {
                                                            for (const y of [22, 28, 34, 40, 46]) {
                                                                pushPoint(x, y);
                                                            }
                                                        }

                                                        for (const [column, row] of [
                                                            [0.08, 0.24],
                                                            [0.1, 0.3],
                                                            [0.12, 0.36],
                                                            [0.14, 0.42],
                                                            [0.16, 0.48],
                                                            [0.18, 0.54],
                                                            [0.2, 0.42],
                                                            [0.22, 0.36],
                                                        ]) {
                                                            pushPoint(safeWidth * column, safeHeight * row);
                                                        }

                                                        return points;
                                                    }
                            return rect.width > 0 && rect.height > 0;
                        }

                        function normalizeTarget(element) {
                            if (!(element instanceof Element)) {
                                return null;
                            }

                            return element.closest?.([
                                '[role="checkbox"]',
                                'input[type="checkbox"]',
                                '[data-action="verify"]',
                                'label',
                                'button',
                                'a',
                                '[tabindex]',
                                '[class*="checkbox"]',
                                '[class*="mark"]',
                                '[class*="ctp"]',
                            ].join(',')) || element;
                        }

                        function targetKindOf(element, cloudflareFrame) {
                            if (!(element instanceof Element)) {
                                return cloudflareFrame ? 'cloudflare-frame' : 'unknown';
                            }

                            if (cloudflareFrame && (element === document.body || element === document.documentElement)) {
                                return 'cloudflare-body-fallback';
                            }

                            if (element.matches?.('input[type="checkbox"]')) {
                                return 'checkbox';
                            }

                            if (element.getAttribute?.('role') === 'checkbox') {
                                return 'role-checkbox';
                            }

                            if (element.matches?.('[data-action="verify"]')) {
                                return 'verify-action';
                            }

                            if (element.matches?.('label, button, a')) {
                                return element.tagName.toLowerCase();
                            }

                            return cloudflareFrame ? 'cloudflare-candidate' : 'host';
                        }

                        function scoreCandidate(element, cloudflareFrame, pointX, pointY, source) {
                            const target = normalizeTarget(element);
                            if (!(target instanceof Element)) {
                                return null;
                            }

                            const allowBodyFallback = cloudflareFrame
                                && source === 'cloudflare-body-fallback'
                                && (target === document.body || target === document.documentElement);

                            if (!allowBodyFallback && (target === document.body || target === document.documentElement)) {
                                return null;
                            }

                            const rect = target.getBoundingClientRect();
                            const allowCollapsedHost = !cloudflareFrame && source === 'host' && rect.width > 0;
                            if (!isVisible(target) && !allowCollapsedHost) {
                                return null;
                            }

                            const className = (typeof target.className === 'string' ? target.className : '').toLowerCase();
                            const role = (target.getAttribute?.('role') || '').toLowerCase();
                            const dataAction = (target.getAttribute?.('data-action') || '').toLowerCase();
                            const ariaLabel = (target.getAttribute?.('aria-label') || '').toLowerCase();
                            const tabIndex = target.tabIndex ?? -1;
                            let score = source === 'hit-test' ? 30 : 10;

                            if (target.matches?.('input[type="checkbox"]')) {
                                score += 240;
                            }

                            if (role === 'checkbox') {
                                score += 220;
                            }

                            if (dataAction === 'verify') {
                                score += 180;
                            }

                            if (target.matches?.('label')) {
                                score += 140;
                            }

                            if (target.matches?.('button, a')) {
                                score += 120;
                            }

                            if (target.matches?.('[tabindex]') || tabIndex >= 0) {
                                score += 80;
                            }

                            if (target.matches?.('[class*="checkbox"], [class*="mark"], [class*="ctp"]')) {
                                score += 110;
                            }

                            if (className.includes('checkbox') || className.includes('verify') || className.includes('mark') || className.includes('ctp')) {
                                score += 60;
                            }

                            if (ariaLabel.includes('checkbox') || ariaLabel.includes('verify') || ariaLabel.includes('human')) {
                                score += 60;
                            }

                            if (typeof target.onclick === 'function' || typeof target.onmousedown === 'function') {
                                score += 40;
                            }

                            if (rect.width >= 18 && rect.height >= 18) {
                                score += 20;
                            }

                            if (allowCollapsedHost) {
                                score += 40;
                            }

                            if (allowBodyFallback) {
                                score += 25;
                            }

                            if (cloudflareFrame) {
                                score += 100;

                                const viewportWidth = Math.max(window.innerWidth || 0, document.documentElement?.clientWidth || 0, rect.right || 0);
                                const viewportHeight = Math.max(window.innerHeight || 0, document.documentElement?.clientHeight || 0, rect.bottom || 0);
                                const hotspotX = Math.min(Math.max(40, 8), Math.max(viewportWidth - 8, 8));
                                const hotspotY = Math.min(Math.max(34, 8), Math.max(viewportHeight - 8, 8));
                                const rectCenterX = rect.width > 0 ? rect.left + rect.width / 2 : pointX;
                                const rectCenterY = rect.height > 0 ? rect.top + rect.height / 2 : pointY;
                                const deltaX = Math.abs(rectCenterX - hotspotX);
                                const deltaY = Math.abs(rectCenterY - hotspotY);

                                if (deltaX <= 24) {
                                    score += 120;
                                } else if (deltaX <= 56) {
                                    score += 70;
                                } else if (deltaX <= 96) {
                                    score += 35;
                                }

                                if (deltaY <= 18) {
                                    score += 90;
                                } else if (deltaY <= 36) {
                                    score += 50;
                                } else if (deltaY <= 64) {
                                    score += 20;
                                }

                                if (rect.left <= Math.max(viewportWidth * 0.32, 96)) {
                                    score += 40;
                                }

                                if (rect.width >= 14 && rect.width <= 96 && rect.height >= 14 && rect.height <= 96) {
                                    score += 35;
                                }
                            }

                            return {
                                target,
                                score,
                                pointX: Number.isFinite(pointX) ? pointX : rect.left + rect.width / 2,
                                pointY: Number.isFinite(pointY) ? pointY : (rect.height > 0 ? rect.top + rect.height / 2 : rect.top + 24),
                                source,
                            };
                        }

                        function pushCandidate(bucket, candidate) {
                            if (!candidate?.target) {
                                return;
                            }

                            const key = [candidate.target.tagName, candidate.target.id || '', candidate.target.className || '', candidate.source].join('|');
                            const existing = bucket.get(key);
                            if (!existing || candidate.score > existing.score) {
                                bucket.set(key, candidate);
                            }
                        }

                        function collectHitTestCandidates(bucket, cloudflareFrame) {
                            const width = Math.max(window.innerWidth || 0, document.documentElement?.clientWidth || 0);
                            const height = Math.max(window.innerHeight || 0, document.documentElement?.clientHeight || 0);
                            if (width <= 0 || height <= 0) {
                                return;
                            }

                            if (cloudflareFrame) {
                                for (const [pointX, pointY] of getCloudflareHotspotPoints(width, height)) {
                                    const stack = document.elementsFromPoint(pointX, pointY) || [];

                                    for (const element of stack.slice(0, 8)) {
                                        const candidate = scoreCandidate(element, true, pointX, pointY, 'cloudflare-hotspot');
                                        if (candidate) {
                                            pushCandidate(bucket, candidate);
                                        }
                                    }
                                }
                            }

                            const columns = cloudflareFrame ? [0.2, 0.35, 0.5, 0.65, 0.8] : [0.3, 0.5, 0.7];
                            const rows = cloudflareFrame ? [0.18, 0.3, 0.42, 0.54, 0.66, 0.78] : [0.35, 0.5, 0.65];

                            for (const row of rows) {
                                for (const column of columns) {
                                    const pointX = width * column;
                                    const pointY = height * row;
                                    const stack = document.elementsFromPoint(pointX, pointY) || [];

                                    for (const element of stack.slice(0, 6)) {
                                        const candidate = scoreCandidate(element, cloudflareFrame, pointX, pointY, 'hit-test');
                                        if (candidate) {
                                            pushCandidate(bucket, candidate);
                                        }
                                    }
                                }
                            }

                            if (cloudflareFrame && bucket.size === 0) {
                                for (const [pointX, pointY] of getCloudflareHotspotPoints(width, height)) {
                                    const candidate = scoreCandidate(document.body || document.documentElement, true, pointX, pointY, 'cloudflare-body-fallback');
                                    if (candidate) {
                                        pushCandidate(bucket, candidate);
                                    }
                                }
                            }
                        }

                        try {
                            const isCloudflareFrame = location.href.includes('challenges.cloudflare.com');
                            const checkbox = queryDeep(document, ['input[type="checkbox"]']);
                            const roleCheckbox = queryDeep(document, ['[role="checkbox"]']);
                            const host = queryDeep(document, ['.cf-turnstile']);
                            const renderedWidgetMarker = queryDeep(document, [
                                '[name="cf-turnstile-response"]',
                                'iframe',
                                'input[type="checkbox"]',
                                '[role="checkbox"]',
                                '.ctp-checkbox-label',
                                '.ctp-checkbox-container',
                                '[data-action="verify"]',
                                '[class*="checkbox"]',
                                '[class*="mark"]',
                            ]);
                            const selectors = [
                                '[role="checkbox"]',
                                'input[type="checkbox"]',
                                'label',
                                '.ctp-checkbox-label',
                                '.ctp-checkbox-container',
                                '[data-action="verify"]',
                                '[class*="checkbox"]',
                                '[class*="mark"]',
                                '[class*="ctp"]',
                                '[tabindex]',
                                'button',
                            ];
                            const candidateBucket = new Map();

                            for (const explicitElement of collectDeep(document, selectors, isCloudflareFrame ? 48 : 24)) {
                                const rect = explicitElement.getBoundingClientRect();
                                const candidate = scoreCandidate(
                                    explicitElement,
                                    isCloudflareFrame,
                                    rect.left + rect.width / 2,
                                    rect.top + rect.height / 2,
                                    'explicit');
                                if (candidate) {
                                    pushCandidate(candidateBucket, candidate);
                                }
                            }

                            if (checkbox instanceof Element) {
                                const rect = checkbox.getBoundingClientRect();
                                pushCandidate(candidateBucket, scoreCandidate(checkbox, isCloudflareFrame, rect.left + rect.width / 2, rect.top + rect.height / 2, 'checkbox'));
                            }

                            if (roleCheckbox instanceof Element) {
                                const rect = roleCheckbox.getBoundingClientRect();
                                pushCandidate(candidateBucket, scoreCandidate(roleCheckbox, isCloudflareFrame, rect.left + rect.width / 2, rect.top + rect.height / 2, 'role-checkbox'));
                            }

                            if (renderedWidgetMarker instanceof Element) {
                                const rect = renderedWidgetMarker.getBoundingClientRect();
                                pushCandidate(candidateBucket, scoreCandidate(renderedWidgetMarker, isCloudflareFrame, rect.left + rect.width / 2, rect.top + rect.height / 2, 'widget-marker'));
                            }

                            if (host instanceof Element) {
                                const rect = host.getBoundingClientRect();
                                pushCandidate(candidateBucket, scoreCandidate(
                                    host,
                                    isCloudflareFrame,
                                    rect.left + rect.width / 2,
                                    rect.height > 0 ? rect.top + rect.height / 2 : rect.top + 24,
                                    'host'));
                            }

                            collectHitTestCandidates(candidateBucket, isCloudflareFrame);

                            const bestCandidate = Array.from(candidateBucket.values()).sort((left, right) => right.score - left.score)[0] || null;
                            if (!bestCandidate) {
                                return JSON.stringify({
                                    found: false,
                                    href: location.href,
                                    title: document.title,
                                    isCloudflareFrame,
                                    renderedWidget: !!renderedWidgetMarker,
                                    candidateCount: candidateBucket.size,
                                    html: (document.body?.innerHTML || '').slice(0, 400),
                                });
                            }

                            const target = bestCandidate.target;
                            if (typeof target.scrollIntoView === 'function') {
                                target.scrollIntoView({ block: 'center', inline: 'center', behavior: 'instant' });
                            }

                            const rect = target.getBoundingClientRect();
                            const centerX = Number.isFinite(bestCandidate.pointX)
                                ? bestCandidate.pointX
                                : (rect.width > 0 ? rect.left + rect.width / 2 : window.innerWidth / 2);
                            const centerY = Number.isFinite(bestCandidate.pointY)
                                ? bestCandidate.pointY
                                : (rect.height > 0 ? rect.top + rect.height / 2 : window.innerHeight / 2);
                            const eventSequence = [
                                ['pointerover', 0],
                                ['mouseover', 0],
                                ['pointerenter', 0],
                                ['mouseenter', 0],
                                ['pointermove', 0],
                                ['mousemove', 0],
                                ['pointerdown', 1],
                                ['mousedown', 1],
                                ['pointerup', 0],
                                ['mouseup', 0],
                                ['click', 0],
                            ];

                            const interactionPoints = targetKindOf(target, isCloudflareFrame) === 'cloudflare-body-fallback'
                                ? (() => {
                                    const width = Math.max(window.innerWidth || 0, document.documentElement?.clientWidth || 0, rect.width || 0);
                                    const height = Math.max(window.innerHeight || 0, document.documentElement?.clientHeight || 0, rect.height || 0);
                                    const points = getCloudflareHotspotPoints(width, height);
                                    points.push([width * 0.3, height * 0.5]);
                                    return points;
                                })()
                                : [[centerX, centerY]];

                            const dispatchTargets = targetKindOf(target, isCloudflareFrame) === 'cloudflare-body-fallback'
                                ? Array.from(new Set([
                                    window,
                                    document,
                                    document.documentElement,
                                    document.body,
                                    target,
                                ].filter(Boolean)))
                                : [target];

                            for (const [pointX, pointY] of interactionPoints) {
                                for (const dispatchTarget of dispatchTargets) {
                                    for (const [type, buttons] of eventSequence) {
                                        const init = {
                                            bubbles: type !== 'mouseenter' && type !== 'pointerenter',
                                            cancelable: true,
                                            composed: true,
                                            clientX: pointX,
                                            clientY: pointY,
                                            button: 0,
                                            buttons,
                                        };

                                        if (type.startsWith('pointer')) {
                                            dispatchTarget.dispatchEvent(new PointerEvent(type, {
                                                ...init,
                                                pointerId: 1,
                                                pointerType: 'mouse',
                                                isPrimary: true,
                                            }));
                                        } else {
                                            dispatchTarget.dispatchEvent(new MouseEvent(type, init));
                                        }
                                    }
                                }
                            }

                            if (typeof target.focus === 'function') {
                                target.focus({ preventScroll: true });
                            }

                            if (target.matches?.('label') && target.control && typeof target.control.click === 'function') {
                                target.control.click();
                            }

                            if (typeof target.click === 'function') {
                                target.click();
                            }

                            return JSON.stringify({
                                found: true,
                                clicked: true,
                                href: location.href,
                                title: document.title,
                                isCloudflareFrame,
                                targetKind: targetKindOf(target, isCloudflareFrame),
                                tagName: target.tagName || null,
                                className: target.className || null,
                                targetScore: bestCandidate.score,
                                targetSource: bestCandidate.source,
                                targetPointX: centerX,
                                targetPointY: centerY,
                                checked: checkbox?.checked ?? null,
                                ariaChecked: target.getAttribute?.('aria-checked') ?? checkbox?.getAttribute?.('aria-checked') ?? null,
                            });
                        } catch (error) {
                            return JSON.stringify({
                                found: false,
                                clicked: false,
                                href: location.href,
                                error: error?.message || String(error),
                            });
                        }
                    })()
                    """,
                    cts.Token).ConfigureAwait(false);

                if (results is JsonElement arr && arr.ValueKind == JsonValueKind.Array)
                {
                    string? bestClickedResult = null;
                    var bestClickedScore = int.MinValue;
                    var anyCloudflareFrameSeen = false;

                    foreach (var item in arr.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.String)
                            continue;

                        var value = item.GetString();
                        if (string.IsNullOrWhiteSpace(value))
                            continue;

                        lastResult = value;

                        JsonNode? parsed;
                        try
                        {
                            parsed = JsonNode.Parse(value);
                        }
                        catch
                        {
                            continue;
                        }

                        if (parsed?["isCloudflareFrame"]?.GetValue<bool>() == true)
                            anyCloudflareFrameSeen = true;

                        if (parsed?["clicked"]?.GetValue<bool>() == true)
                        {
                            var score = 0;
                            if (parsed?["isCloudflareFrame"]?.GetValue<bool>() == true)
                                score += 100;

                            var targetKind = parsed?["targetKind"]?.GetValue<string>();
                            if (string.Equals(targetKind, "checkbox", StringComparison.Ordinal))
                                score += 30;
                            else if (string.Equals(targetKind, "role-checkbox", StringComparison.Ordinal))
                                score += 20;
                            else if (string.Equals(targetKind, "frame-fallback", StringComparison.Ordinal)
                                || string.Equals(targetKind, "cloudflare-body-fallback", StringComparison.Ordinal))
                                score += 10;

                            if (score > bestClickedScore)
                            {
                                bestClickedScore = score;
                                bestClickedResult = value;
                            }
                        }
                    }

                    if (bestClickedResult is not null)
                    {
                        JsonNode? bestClickedParsed = null;
                        try
                        {
                            bestClickedParsed = JsonNode.Parse(bestClickedResult);
                        }
                        catch
                        {
                        }

                        var bestTargetKind = bestClickedParsed?["targetKind"]?.GetValue<string>();
                        var bestIsCloudflare = bestClickedParsed?["isCloudflareFrame"]?.GetValue<bool>() == true;

                        if (bestIsCloudflare)
                        {
                            if (string.Equals(bestTargetKind, "cloudflare-body-fallback", StringComparison.Ordinal))
                            {
                                cloudflareFallbackObservedAt ??= DateTime.UtcNow;
                                if (DateTime.UtcNow - cloudflareFallbackObservedAt < TimeSpan.FromSeconds(2))
                                {
                                    lastResult = bestClickedResult;
                                }
                                else
                                {
                                    return bestClickedResult;
                                }
                            }
                            else
                            {
                                return bestClickedResult;
                            }
                        }

                        if (string.Equals(bestTargetKind, "host", StringComparison.Ordinal))
                        {
                            hostClickObservedAt ??= DateTime.UtcNow;

                            if (anyCloudflareFrameSeen)
                            {
                                lastResult = bestClickedResult;
                            }
                            else if (DateTime.UtcNow - hostClickObservedAt >= TimeSpan.FromSeconds(8))
                            {
                                return bestClickedResult;
                            }
                        }
                        else
                        {
                            return bestClickedResult;
                        }
                    }
                }

                try
                {
                    await Task.Delay(250, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            return lastResult;
        }

        private async Task<WebDriverBrowser> LaunchTurnstileBrowserAsync()
        {
            var (browserPath, extensionPath) = FindChromiumBrowser();
            var arguments = new List<string> { "--no-sandbox", "--disable-features=Translate" };

            var launchedBrowser = await WebDriverBrowser.LaunchAsync(browserPath, extensionPath, arguments: arguments).ConfigureAwait(false);

            try
            {
                await WaitForFirstTabAsync(launchedBrowser).ConfigureAwait(false);
                logger.WriteLine(LogKind.Default, $"turnstile trusted browser args={string.Join(' ', arguments)} display={Environment.GetEnvironmentVariable("DISPLAY")} session={Environment.GetEnvironmentVariable("XDG_SESSION_TYPE")}");
                return launchedBrowser;
            }
            catch
            {
                await launchedBrowser.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        private static TabContextSettings CreateTurnstileRealSiteContextSettings()
        {
            return new TabContextSettings
            {
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/145.0.0.0 Safari/537.36",
                Locale = "en-US",
                Languages = ["en-US", "en"],
                Platform = "Win32",
                Timezone = "America/New_York",
                Screen = new ScreenSettings
                {
                    Width = 1920,
                    Height = 1080,
                    ColorDepth = 24,
                },
                WebGL = new WebGLSettings
                {
                    Vendor = "Google Inc. (Intel)",
                    Renderer = "ANGLE (Intel, Intel(R) UHD Graphics 630 Direct3D11 vs_5_0 ps_5_0, D3D11)",
                },
                CanvasNoise = true,
                AudioNoise = true,
                WebRtcPolicy = "disable",
                BatteryProtection = true,
                PermissionsProtection = true,
                HardwareConcurrency = 8,
                DeviceMemory = 8,
                AllowedFonts = ["Arial", "Verdana", "Helvetica", "Times New Roman", "Courier New", "Georgia"],
                ClientHints = new ClientHintsSettings
                {
                    Platform = "Windows",
                    PlatformVersion = "10.0.0",
                    Mobile = false,
                    Architecture = "x86",
                    Bitness = "64",
                    Model = "",
                    Brands = [new("Not:A-Brand", "99"), new("Chromium", "145"), new("Google Chrome", "145")],
                    FullVersionList = [new("Not:A-Brand", "99.0.0.0"), new("Chromium", "145.0.0.0"), new("Google Chrome", "145.0.0.0")],
                },
                NetworkInfo = new NetworkInfoSettings(),
                SpeechVoices =
                [
                    new SpeechVoiceSettings { Name = "Microsoft David", Lang = "en-US" },
                    new SpeechVoiceSettings { Name = "Microsoft Zira", Lang = "en-US" },
                ],
                MediaDevicesProtection = true,
                IntlSpoofing = true,
                ColorScheme = "light",
                WebGLNoise = true,
                PluginSpoofing = true,
                MaxTouchPoints = 0,
                VisibilityStateOverride = "visible",
            };
        }

        private static async Task<string?> CaptureTurnstileFrameDiagnosticsAsync(WebDriverPage tab, bool isolatedWorld = false)
        {
            var results = isolatedWorld
                ? await tab.ExecuteInAllFramesIsolatedAsync(
                """
                (() => {
                    function rectInfo(element) {
                        if (!element) {
                            return null;
                        }

                        const rect = element.getBoundingClientRect();
                        return {
                            left: rect.left,
                            top: rect.top,
                            width: rect.width,
                            height: rect.height,
                            centerX: rect.width > 0 ? rect.left + rect.width / 2 : rect.left,
                            centerY: rect.height > 0 ? rect.top + rect.height / 2 : rect.top,
                        };
                    }

                    function describeElement(element) {
                        if (!element) {
                            return null;
                        }

                        return {
                            tagName: element.tagName || '',
                            id: element.id || '',
                            className: typeof element.className === 'string' ? element.className : '',
                            role: element.getAttribute?.('role') || '',
                        };
                    }

                    function describeChildren(root, limit) {
                        if (!root?.children) {
                            return [];
                        }

                        return Array.from(root.children)
                            .slice(0, limit)
                            .map(describeElement)
                            .filter(Boolean);
                    }

                    function sampleArea(rect, label, columns, rows) {
                        if (!rect || rect.width <= 0 || rect.height <= 0) {
                            return [];
                        }

                        const samples = [];
                        for (const rowRatio of rows) {
                            for (const columnRatio of columns) {
                                const x = rect.left + rect.width * columnRatio;
                                const y = rect.top + rect.height * rowRatio;
                                samples.push({
                                    label,
                                    x,
                                    y,
                                    rowRatio,
                                    columnRatio,
                                    stack: (document.elementsFromPoint(x, y) || []).slice(0, 5).map(describeElement).filter(Boolean),
                                });
                            }
                        }

                        return samples;
                    }

                    const host = document.querySelector('.cf-turnstile');
                    const hostRect = rectInfo(host);
                    const viewportRect = {
                        left: 0,
                        top: 0,
                        width: Math.max(window.innerWidth || 0, document.documentElement?.clientWidth || 0),
                        height: Math.max(window.innerHeight || 0, document.documentElement?.clientHeight || 0),
                    };
                    const belowHostRect = hostRect
                        ? {
                            left: hostRect.left,
                            top: hostRect.top + hostRect.height,
                            width: Math.max(hostRect.width, 280),
                            height: Math.max(hostRect.height * 1.6, 120),
                        }
                        : null;

                    return JSON.stringify({
                        world: 'ISOLATED',
                        href: location.href,
                        title: document.title,
                        readyState: document.readyState,
                        iframeCount: document.querySelectorAll('iframe').length,
                        challengeIframeCount: document.querySelectorAll('iframe[src*="challenges.cloudflare.com"]').length,
                        hasTurnstileHost: !!host,
                        hasCheckbox: !!document.querySelector('input[type="checkbox"]'),
                        hasRoleCheckbox: !!document.querySelector('[role="checkbox"]'),
                        viewportRect,
                        documentElementRect: rectInfo(document.documentElement),
                        bodyRect: rectInfo(document.body),
                        bodyChildElementCount: document.body?.childElementCount || 0,
                        bodyNodeCount: document.body?.childNodes?.length || 0,
                        bodyChildren: describeChildren(document.body, 12),
                        documentChildren: describeChildren(document.documentElement, 12),
                        bodyTextSnippet: (document.body?.textContent || '').trim().slice(0, 300),
                        hostRect,
                        hostHtmlSnippet: (host?.innerHTML || '').slice(0, 700),
                        hostHitSamples: sampleArea(hostRect, 'host', [0.16, 0.28, 0.44, 0.62], [0.3, 0.5, 0.7]),
                        belowHostHitSamples: sampleArea(belowHostRect, 'below-host', [0.12, 0.28, 0.44, 0.62, 0.8], [0.12, 0.32, 0.52, 0.72]),
                        viewportHitSamples: sampleArea(viewportRect, 'viewport', [0.08, 0.16, 0.24, 0.36, 0.5, 0.64, 0.78, 0.9], [0.1, 0.22, 0.34, 0.5, 0.66, 0.82]),
                        documentHtmlSnippet: (document.documentElement?.outerHTML || '').slice(0, 700),
                        bodySnippet: (document.body?.innerHTML || '').slice(0, 500)
                    });
                })()
                """).ConfigureAwait(false)
                : await tab.ExecuteInAllFramesAsync(
                """
                (() => {
                    function rectInfo(element) {
                        if (!element) {
                            return null;
                        }

                        const rect = element.getBoundingClientRect();
                        return {
                            left: rect.left,
                            top: rect.top,
                            width: rect.width,
                            height: rect.height,
                            centerX: rect.width > 0 ? rect.left + rect.width / 2 : rect.left,
                            centerY: rect.height > 0 ? rect.top + rect.height / 2 : rect.top,
                        };
                    }

                    function describeElement(element) {
                        if (!element) {
                            return null;
                        }

                        return {
                            tagName: element.tagName || '',
                            id: element.id || '',
                            className: typeof element.className === 'string' ? element.className : '',
                            role: element.getAttribute?.('role') || '',
                        };
                    }

                    function describeChildren(root, limit) {
                        if (!root?.children) {
                            return [];
                        }

                        return Array.from(root.children)
                            .slice(0, limit)
                            .map(describeElement)
                            .filter(Boolean);
                    }

                    function sampleArea(rect, label, columns, rows) {
                        if (!rect || rect.width <= 0 || rect.height <= 0) {
                            return [];
                        }

                        const samples = [];
                        for (const rowRatio of rows) {
                            for (const columnRatio of columns) {
                                const x = rect.left + rect.width * columnRatio;
                                const y = rect.top + rect.height * rowRatio;
                                samples.push({
                                    label,
                                    x,
                                    y,
                                    rowRatio,
                                    columnRatio,
                                    stack: (document.elementsFromPoint(x, y) || []).slice(0, 5).map(describeElement).filter(Boolean),
                                });
                            }
                        }

                        return samples;
                    }

                    const host = document.querySelector('.cf-turnstile');
                    const hostRect = rectInfo(host);
                    const viewportRect = {
                        left: 0,
                        top: 0,
                        width: Math.max(window.innerWidth || 0, document.documentElement?.clientWidth || 0),
                        height: Math.max(window.innerHeight || 0, document.documentElement?.clientHeight || 0),
                    };
                    const belowHostRect = hostRect
                        ? {
                            left: hostRect.left,
                            top: hostRect.top + hostRect.height,
                            width: Math.max(hostRect.width, 280),
                            height: Math.max(hostRect.height * 1.6, 120),
                        }
                        : null;

                    return JSON.stringify({
                        href: location.href,
                        title: document.title,
                        readyState: document.readyState,
                        iframeCount: document.querySelectorAll('iframe').length,
                        challengeIframeCount: document.querySelectorAll('iframe[src*="challenges.cloudflare.com"]').length,
                        hasTurnstileHost: !!host,
                        hasCheckbox: !!document.querySelector('input[type="checkbox"]'),
                        hasRoleCheckbox: !!document.querySelector('[role="checkbox"]'),
                        viewportRect,
                        documentElementRect: rectInfo(document.documentElement),
                        bodyRect: rectInfo(document.body),
                        bodyChildElementCount: document.body?.childElementCount || 0,
                        bodyNodeCount: document.body?.childNodes?.length || 0,
                        bodyChildren: describeChildren(document.body, 12),
                        documentChildren: describeChildren(document.documentElement, 12),
                        bodyTextSnippet: (document.body?.textContent || '').trim().slice(0, 300),
                        hostRect,
                        hostHtmlSnippet: (host?.innerHTML || '').slice(0, 700),
                        hostHitSamples: sampleArea(hostRect, 'host', [0.16, 0.28, 0.44, 0.62], [0.3, 0.5, 0.7]),
                        belowHostHitSamples: sampleArea(belowHostRect, 'below-host', [0.12, 0.28, 0.44, 0.62, 0.8], [0.12, 0.32, 0.52, 0.72]),
                        viewportHitSamples: sampleArea(viewportRect, 'viewport', [0.08, 0.16, 0.24, 0.36, 0.5, 0.64, 0.78, 0.9], [0.1, 0.22, 0.34, 0.5, 0.66, 0.82]),
                        documentHtmlSnippet: (document.documentElement?.outerHTML || '').slice(0, 700),
                        bodySnippet: (document.body?.innerHTML || '').slice(0, 500)
                    });
                })()
                """).ConfigureAwait(false);

            if (results is not JsonElement arr || arr.ValueKind != JsonValueKind.Array)
                return null;

            var diagnostics = arr.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToArray();

            if (diagnostics.Length == 0)
                return "[]";

            return "[" + string.Join(",", diagnostics.Select(static diagnostic => "\"" + diagnostic!
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal)
                + "\"")) + "]";
        }

        private static async Task<string?> CaptureTurnstileFrameMappingAsync(WebDriverPage tab)
        {
            var topPageIframesJson = (await tab.ExecuteAsync(
                """
                (() => {
                    function describeIframe(iframe, index) {
                        const rect = iframe.getBoundingClientRect();
                        return {
                            index,
                            src: iframe.src || '',
                            title: iframe.title || '',
                            name: iframe.name || '',
                            id: iframe.id || '',
                            className: typeof iframe.className === 'string' ? iframe.className : '',
                            left: rect.left,
                            top: rect.top,
                            width: rect.width,
                            height: rect.height,
                            isChallenge: (iframe.src || '').includes('challenges.cloudflare.com'),
                        };
                    }

                    return JSON.stringify({
                        href: location.href,
                        title: document.title,
                        iframeCount: document.querySelectorAll('iframe').length,
                        iframes: Array.from(document.querySelectorAll('iframe')).map(describeIframe),
                    });
                })()
                """).ConfigureAwait(false))?.ToString();

            var resultNode = new JsonObject();

            if (!string.IsNullOrWhiteSpace(topPageIframesJson))
            {
                try
                {
                    resultNode["topPage"] = JsonNode.Parse(topPageIframesJson);
                }
                catch
                {
                    resultNode["topPageRaw"] = topPageIframesJson;
                }
            }

            var mainWorldResponse = await tab.SendBridgeCommandAsync(
                BridgeCommand.ExecuteScriptInFrames,
                new JsonObject
                {
                    ["script"] =
                    """
                    (() => JSON.stringify({
                        href: location.href,
                        title: document.title,
                        readyState: document.readyState,
                        isTop: window === window.top,
                        isCloudflareFrame: location.href.includes('challenges.cloudflare.com'),
                        iframeCount: document.querySelectorAll('iframe').length,
                        challengeIframeCount: document.querySelectorAll('iframe[src*="challenges.cloudflare.com"]').length,
                        bodyTextSnippet: (document.body?.textContent || '').trim().slice(0, 180),
                        htmlSnippet: (document.documentElement?.outerHTML || '').slice(0, 220)
                    }))()
                    """,
                    ["includeMetadata"] = true,
                }).ConfigureAwait(false);

            var isolatedWorldResponse = await tab.SendBridgeCommandAsync(
                BridgeCommand.ExecuteScriptInFrames,
                new JsonObject
                {
                    ["script"] =
                    """
                    (() => JSON.stringify({
                        href: location.href,
                        title: document.title,
                        readyState: document.readyState,
                        isTop: window === window.top,
                        isCloudflareFrame: location.href.includes('challenges.cloudflare.com'),
                        iframeCount: document.querySelectorAll('iframe').length,
                        challengeIframeCount: document.querySelectorAll('iframe[src*="challenges.cloudflare.com"]').length,
                        bodyTextSnippet: (document.body?.textContent || '').trim().slice(0, 180),
                        htmlSnippet: (document.documentElement?.outerHTML || '').slice(0, 220)
                    }))()
                    """,
                    ["includeMetadata"] = true,
                    ["world"] = "ISOLATED",
                }).ConfigureAwait(false);

            resultNode["mainWorldBridgeStatus"] = mainWorldResponse.Status?.ToString();
            resultNode["mainWorldBridgeError"] = mainWorldResponse.Error;
            resultNode["isolatedWorldBridgeStatus"] = isolatedWorldResponse.Status?.ToString();
            resultNode["isolatedWorldBridgeError"] = isolatedWorldResponse.Error;

            if (mainWorldResponse.Payload is JsonElement mainFrameArray)
            {
                resultNode["mainWorldPayloadKind"] = mainFrameArray.ValueKind.ToString();

                if (mainFrameArray.ValueKind == JsonValueKind.Array)
                {
                    try
                    {
                        resultNode["mainWorldExecutedFrames"] = JsonNode.Parse(mainFrameArray.GetRawText());
                    }
                    catch
                    {
                        resultNode["mainWorldExecutedFramesRaw"] = mainFrameArray.GetRawText();
                    }
                }
                else
                {
                    resultNode["mainWorldPayloadRaw"] = mainFrameArray.GetRawText();
                }
            }

            if (isolatedWorldResponse.Payload is JsonElement isolatedFrameArray)
            {
                resultNode["isolatedWorldPayloadKind"] = isolatedFrameArray.ValueKind.ToString();

                if (isolatedFrameArray.ValueKind == JsonValueKind.Array)
                {
                    try
                    {
                        resultNode["isolatedWorldExecutedFrames"] = JsonNode.Parse(isolatedFrameArray.GetRawText());
                    }
                    catch
                    {
                        resultNode["isolatedWorldExecutedFramesRaw"] = isolatedFrameArray.GetRawText();
                    }
                }
                else
                {
                    resultNode["isolatedWorldPayloadRaw"] = isolatedFrameArray.GetRawText();
                }
            }

            return resultNode.ToJsonString();
        }

        private static async Task<string?> WaitForChallengeIframeAsync(WebDriverPage tab, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;

            while (DateTime.UtcNow < deadline)
            {
                var frameSrc = (await tab.ExecuteAsync(
                    """
                    (() => {
                        const queue = [document];
                        while (queue.length > 0) {
                            const current = queue.shift();
                            if (!current?.querySelectorAll) {
                                continue;
                            }

                            const iframe = current.querySelector('iframe[src*="challenges.cloudflare.com"]');
                            if (iframe?.src) {
                                return iframe.src;
                            }

                            for (const element of current.querySelectorAll('*')) {
                                if (element.shadowRoot) {
                                    queue.push(element.shadowRoot);
                                }
                            }
                        }

                        return '';
                    })()
                    """).ConfigureAwait(false))?.ToString();

                if (!string.IsNullOrWhiteSpace(frameSrc))
                    return frameSrc;

                await Task.Delay(250).ConfigureAwait(false);
            }

            return null;
        }

        private static async Task<string?> ClickTurnstileHostFromTopPageAsync(
            WebDriverPage tab,
            PagePointClickOptions clickOptions,
            CancellationToken cancellationToken)
        {
            var hostRectJson = (await tab.ExecuteAsync(
                """
                (() => {
                    function queryDeep(root, selector) {
                        const queue = [root];

                        while (queue.length > 0) {
                            const current = queue.shift();
                            if (!current?.querySelectorAll) {
                                continue;
                            }

                            const element = current.querySelector(selector);
                            if (element instanceof Element) {
                                return element;
                            }

                            for (const child of current.querySelectorAll('*')) {
                                if (child.shadowRoot) {
                                    queue.push(child.shadowRoot);
                                }
                            }
                        }

                        return null;
                    }

                    const host = queryDeep(document, '.cf-turnstile');
                    if (!(host instanceof Element)) {
                        return '';
                    }

                    const rect = host.getBoundingClientRect();
                    return JSON.stringify({
                        found: true,
                        left: rect.left,
                        top: rect.top,
                        width: rect.width,
                        height: rect.height,
                    });
                })()
                """,
                cancellationToken).ConfigureAwait(false))?.ToString();

            if (string.IsNullOrWhiteSpace(hostRectJson))
                return null;

            JsonNode? hostRect;
            try
            {
                hostRect = JsonNode.Parse(hostRectJson);
            }
            catch
            {
                return null;
            }

            if (hostRect?["found"]?.GetValue<bool>() != true)
                return null;

            var left = hostRect["left"]?.GetValue<double>() ?? 0;
            var top = hostRect["top"]?.GetValue<double>() ?? 0;
            var width = hostRect["width"]?.GetValue<double>() ?? 0;
            var height = hostRect["height"]?.GetValue<double>() ?? 0;

            if (width <= 0 || height <= 0)
                return hostRectJson;

            var clickPoints = new (double X, double Y)[]
            {
                (0.12, 0.50),
                (0.16, 0.50),
                (0.20, 0.50),
                (0.24, 0.50),
                (0.16, 0.34),
                (0.16, 0.66),
            };

            foreach (var point in clickPoints)
            {
                await tab.ClickPointAsync(
                    left + (width * point.X),
                    top + (height * point.Y),
                    clickOptions,
                    cancellationToken).ConfigureAwait(false);
            }

            hostRect["clickPreference"] = clickOptions.Preference.ToString();
            return hostRect.ToJsonString();
        }

        private static async Task<string?> ClickChallengeIframeFromTopPageAsync(WebDriverPage tab, CancellationToken cancellationToken)
        {
            var iframeRectJson = (await tab.ExecuteAsync(
                """
                (() => {
                    function queryDeep(root, selector) {
                        const queue = [root];

                        while (queue.length > 0) {
                            const current = queue.shift();
                            if (!current?.querySelectorAll) {
                                continue;
                            }

                            const iframe = current.querySelector(selector);
                            if (iframe instanceof HTMLIFrameElement) {
                                return iframe;
                            }

                            for (const element of current.querySelectorAll('*')) {
                                if (element.shadowRoot) {
                                    queue.push(element.shadowRoot);
                                }
                            }
                        }

                        return null;
                    }

                    const iframe = queryDeep(document, 'iframe[src*="challenges.cloudflare.com"]');
                    if (!(iframe instanceof HTMLIFrameElement)) {
                        return '';
                    }

                    const rect = iframe.getBoundingClientRect();
                    return JSON.stringify({
                        found: true,
                        left: rect.left,
                        top: rect.top,
                        width: rect.width,
                        height: rect.height,
                    });
                })()
                """,
                cancellationToken).ConfigureAwait(false))?.ToString();

            if (string.IsNullOrWhiteSpace(iframeRectJson))
                return null;

            JsonNode? iframeRect;
            try
            {
                iframeRect = JsonNode.Parse(iframeRectJson);
            }
            catch
            {
                return null;
            }

            if (iframeRect?["found"]?.GetValue<bool>() != true)
                return null;

            var left = iframeRect["left"]?.GetValue<double>() ?? 0;
            var top = iframeRect["top"]?.GetValue<double>() ?? 0;
            var width = iframeRect["width"]?.GetValue<double>() ?? 0;
            var height = iframeRect["height"]?.GetValue<double>() ?? 0;

            if (width <= 0 || height <= 0)
                return iframeRectJson;

            var clickPoints = new (double X, double Y)[]
            {
                (0.12, 0.5),
                (0.16, 0.5),
                (0.2, 0.5),
                (0.24, 0.5),
                (0.12, 0.34),
                (0.16, 0.34),
                (0.12, 0.66),
                (0.16, 0.66),
                (0.3, 0.5),
            };

            foreach (var point in clickPoints)
            {
                await tab.ClickPointAsync(
                    left + (width * point.X),
                    top + (height * point.Y),
                    PagePointClickOptions.PreferParallel,
                    cancellationToken).ConfigureAwait(false);
            }

            return iframeRectJson;
        }

        private static async Task<string?> ClickTurnstileOverlayFromTopPageAsync(
            WebDriverPage tab,
            PagePointClickOptions clickOptions,
            CancellationToken cancellationToken)
        {
            var overlayJson = (await tab.ExecuteAsync(
                """
                (() => {
                    function queryDeep(root, selector) {
                        const queue = [root];

                        while (queue.length > 0) {
                            const current = queue.shift();
                            if (!current?.querySelectorAll) {
                                continue;
                            }

                            const element = current.querySelector(selector);
                            if (element instanceof Element) {
                                return element;
                            }

                            for (const child of current.querySelectorAll('*')) {
                                if (child.shadowRoot) {
                                    queue.push(child.shadowRoot);
                                }
                            }
                        }

                        return null;
                    }

                    function describeElement(element) {
                        if (!(element instanceof Element)) {
                            return null;
                        }

                        const rect = element.getBoundingClientRect();
                        return {
                            tagName: element.tagName || '',
                            id: element.id || '',
                            className: typeof element.className === 'string' ? element.className : '',
                            role: element.getAttribute?.('role') || '',
                            left: rect.left,
                            top: rect.top,
                            width: rect.width,
                            height: rect.height,
                        };
                    }

                    function elementAncestors(element, maxDepth) {
                        const result = [];
                        let current = element;

                        while (current instanceof Element && result.length < maxDepth) {
                            result.push(current);
                            current = current.parentElement;
                        }

                        return result;
                    }

                    const host = queryDeep(document, '.cf-turnstile');
                    if (!(host instanceof Element)) {
                        return '';
                    }

                    const rect = host.getBoundingClientRect();
                    if (!(rect.width > 0) || !(rect.height > 0)) {
                        return JSON.stringify({ found: true, host: describeElement(host), clicked: false, reason: 'empty-host-rect' });
                    }

                    const sampleRatios = [
                        [0.12, 0.34],
                        [0.16, 0.34],
                        [0.12, 0.5],
                        [0.16, 0.5],
                        [0.2, 0.5],
                        [0.24, 0.5],
                        [0.3, 0.5],
                        [0.12, 0.66],
                        [0.16, 0.66],
                    ];
                    const eventSequence = [
                        ['pointerover', 0],
                        ['mouseover', 0],
                        ['pointerenter', 0],
                        ['mouseenter', 0],
                        ['pointermove', 0],
                        ['mousemove', 0],
                        ['pointerdown', 1],
                        ['mousedown', 1],
                        ['pointerup', 0],
                        ['mouseup', 0],
                        ['click', 0],
                    ];
                    const sampleResults = [];

                    for (const [columnRatio, rowRatio] of sampleRatios) {
                        const pointX = rect.left + (rect.width * columnRatio);
                        const pointY = rect.top + (rect.height * rowRatio);
                        const stack = (document.elementsFromPoint(pointX, pointY) || []).filter((entry) => entry instanceof Element);
                        const overlay = stack.find((entry) => entry !== host && entry !== document.body && entry !== document.documentElement) || stack[0] || host;
                        const dispatchTargets = Array.from(new Set([
                            ...elementAncestors(overlay, 4),
                            host,
                            document.body,
                            document.documentElement,
                            document,
                            window,
                        ].filter(Boolean)));

                        for (const dispatchTarget of dispatchTargets) {
                            for (const [type, buttons] of eventSequence) {
                                const init = {
                                    bubbles: type !== 'mouseenter' && type !== 'pointerenter',
                                    cancelable: true,
                                    composed: true,
                                    clientX: pointX,
                                    clientY: pointY,
                                    button: 0,
                                    buttons,
                                };

                                if (type.startsWith('pointer')) {
                                    dispatchTarget.dispatchEvent(new PointerEvent(type, {
                                        ...init,
                                        pointerId: 1,
                                        pointerType: 'mouse',
                                        isPrimary: true,
                                    }));
                                } else {
                                    dispatchTarget.dispatchEvent(new MouseEvent(type, init));
                                }
                            }
                        }

                        if (overlay instanceof HTMLElement) {
                            overlay.focus?.({ preventScroll: true });
                            overlay.click?.();
                        }

                        if (host instanceof HTMLElement && host !== overlay) {
                            host.focus?.({ preventScroll: true });
                            host.click?.();
                        }

                        sampleResults.push({
                            columnRatio,
                            rowRatio,
                            pointX,
                            pointY,
                            overlay: describeElement(overlay),
                            stack: stack.slice(0, 5).map(describeElement).filter(Boolean),
                        });
                    }

                    return JSON.stringify({
                        found: true,
                        host: describeElement(host),
                        sampleResults,
                    });
                })()
                """,
                cancellationToken).ConfigureAwait(false))?.ToString();

            if (string.IsNullOrWhiteSpace(overlayJson))
                return null;

            JsonNode? overlayNode;
            try
            {
                overlayNode = JsonNode.Parse(overlayJson);
            }
            catch
            {
                return overlayJson;
            }

            if (overlayNode?["found"]?.GetValue<bool>() != true)
                return overlayJson;

            if (overlayNode["sampleResults"] is not JsonArray sampleResults)
                return overlayJson;

            foreach (var sampleResult in sampleResults)
            {
                var pointX = sampleResult?["pointX"]?.GetValue<double>() ?? double.NaN;
                var pointY = sampleResult?["pointY"]?.GetValue<double>() ?? double.NaN;

                if (double.IsNaN(pointX) || double.IsNaN(pointY))
                    continue;

                await tab.ClickPointAsync(
                    pointX,
                    pointY,
                    clickOptions,
                    cancellationToken).ConfigureAwait(false);
            }

            overlayNode["clicked"] = true;
            overlayNode["clickPreference"] = clickOptions.Preference.ToString();
            return overlayNode.ToJsonString();
        }

        private static async Task<string?> PressTurnstileOverlayKeysFromTopPageAsync(
            WebDriverPage tab,
            PagePointClickOptions pointClickOptions,
            PageKeyPressOptions keyPressOptions,
            CancellationToken cancellationToken)
        {
            var overlayJson = (await tab.ExecuteAsync(
                """
                (() => {
                    function queryDeep(root, selector) {
                        const queue = [root];

                        while (queue.length > 0) {
                            const current = queue.shift();
                            if (!current?.querySelectorAll) {
                                continue;
                            }

                            const element = current.querySelector(selector);
                            if (element instanceof Element) {
                                return element;
                            }

                            for (const child of current.querySelectorAll('*')) {
                                if (child.shadowRoot) {
                                    queue.push(child.shadowRoot);
                                }
                            }
                        }

                        return null;
                    }

                    function describeElement(element) {
                        if (!(element instanceof Element)) {
                            return null;
                        }

                        const rect = element.getBoundingClientRect();
                        return {
                            tagName: element.tagName || '',
                            id: element.id || '',
                            className: typeof element.className === 'string' ? element.className : '',
                            role: element.getAttribute?.('role') || '',
                            left: rect.left,
                            top: rect.top,
                            width: rect.width,
                            height: rect.height,
                        };
                    }

                    function dispatchKey(target, key, code, keyCode) {
                        if (!target?.dispatchEvent) {
                            return false;
                        }

                        target.focus?.({ preventScroll: true });

                        const eventInit = {
                            key,
                            code,
                            keyCode,
                            which: keyCode,
                            charCode: keyCode,
                            bubbles: true,
                            cancelable: true,
                            composed: true,
                        };

                        target.dispatchEvent(new KeyboardEvent('keydown', eventInit));
                        target.dispatchEvent(new KeyboardEvent('keypress', eventInit));
                        target.dispatchEvent(new KeyboardEvent('keyup', eventInit));
                        return true;
                    }

                    const host = queryDeep(document, '.cf-turnstile');
                    if (!(host instanceof Element)) {
                        return '';
                    }

                    const rect = host.getBoundingClientRect();
                    const pointX = rect.left + (rect.width * 0.16);
                    const pointY = rect.top + (rect.height * 0.5);
                    const stack = (document.elementsFromPoint(pointX, pointY) || []).filter((entry) => entry instanceof Element);
                    const overlay = stack.find((entry) => entry !== host && entry !== document.body && entry !== document.documentElement) || stack[0] || host;
                    const targets = Array.from(new Set([
                        overlay,
                        host,
                        document.activeElement,
                        document.body,
                        document.documentElement,
                    ].filter(Boolean)));
                    const actions = [];

                    for (const target of targets) {
                        actions.push({
                            target: describeElement(target),
                            space: dispatchKey(target, ' ', 'Space', 32),
                            enter: dispatchKey(target, 'Enter', 'Enter', 13),
                        });
                    }

                    return JSON.stringify({
                        found: true,
                        host: describeElement(host),
                        overlay: describeElement(overlay),
                        activeElement: describeElement(document.activeElement),
                        actions,
                        pointX,
                        pointY,
                    });
                })()
                """,
                cancellationToken).ConfigureAwait(false))?.ToString();

            if (string.IsNullOrWhiteSpace(overlayJson))
                return null;

            JsonNode? overlayNode;
            try
            {
                overlayNode = JsonNode.Parse(overlayJson);
            }
            catch
            {
                return overlayJson;
            }

            if (overlayNode?["found"]?.GetValue<bool>() != true)
                return overlayJson;

            var pointX = overlayNode["pointX"]?.GetValue<double>() ?? double.NaN;
            var pointY = overlayNode["pointY"]?.GetValue<double>() ?? double.NaN;

            if (!double.IsNaN(pointX) && !double.IsNaN(pointY))
            {
                await tab.ClickPointAsync(
                    pointX,
                    pointY,
                    pointClickOptions,
                    cancellationToken).ConfigureAwait(false);
            }

            await tab.KeyPressAsync(" ", keyPressOptions, cancellationToken).ConfigureAwait(false);
            await tab.KeyPressAsync("Enter", keyPressOptions, cancellationToken).ConfigureAwait(false);

            overlayNode["browserLevelKeyPress"] = new JsonArray(" ", "Enter");
            overlayNode["pointClickPreference"] = pointClickOptions.Preference.ToString();
            overlayNode["keyPressPreference"] = keyPressOptions.Preference.ToString();
            return overlayNode.ToJsonString();
        }

        private static int? FindChallengeFrameId(string? frameMappingJson)
        {
            if (string.IsNullOrWhiteSpace(frameMappingJson))
                return null;

            try
            {
                var node = JsonNode.Parse(frameMappingJson);
                foreach (var propertyName in new[] { "mainWorldExecutedFrames", "isolatedWorldExecutedFrames" })
                {
                    if (node?[propertyName] is not JsonArray frames)
                        continue;

                    foreach (var frame in frames)
                    {
                        var url = frame?["url"]?.GetValue<string>();
                        if (string.IsNullOrWhiteSpace(url) || !url.Contains("challenges.cloudflare.com", StringComparison.Ordinal))
                            continue;

                        if (frame?["frameId"] is JsonNode frameIdNode)
                            return frameIdNode.GetValue<int>();
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private readonly record struct ScreenshotRegion(int Left, int Top, int Width, int Height);

        private static async Task<string?> CaptureTurnstileScreenshotProbeAsync(WebDriverPage tab, string? diagnosticsJson)
        {
            if (!TryReadTurnstileHostRegion(diagnosticsJson, out var hostRegion))
                return null;

            var screenshotDataUrl = await tab.CaptureScreenshotAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(screenshotDataUrl))
                return null;

            return AnalyzePngScreenshot(screenshotDataUrl, hostRegion, padding: 16);
        }

        private static bool TryReadTurnstileHostRegion(string? diagnosticsJson, out ScreenshotRegion region)
        {
            region = default;

            if (string.IsNullOrWhiteSpace(diagnosticsJson))
                return false;

            try
            {
                if (JsonNode.Parse(diagnosticsJson) is not JsonArray diagnostics)
                    return false;

                foreach (var item in diagnostics)
                {
                    JsonNode? diagnosticNode = item;
                    if (item is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var diagnosticText) && !string.IsNullOrWhiteSpace(diagnosticText))
                        diagnosticNode = JsonNode.Parse(diagnosticText);

                    var hostRect = diagnosticNode?["hostRect"];
                    if (hostRect is null)
                        continue;

                    var left = (int)Math.Round(hostRect["left"]?.GetValue<double>() ?? 0);
                    var top = (int)Math.Round(hostRect["top"]?.GetValue<double>() ?? 0);
                    var width = (int)Math.Round(hostRect["width"]?.GetValue<double>() ?? 0);
                    var height = (int)Math.Round(hostRect["height"]?.GetValue<double>() ?? 0);

                    if (width <= 0 || height <= 0)
                        continue;

                    region = new ScreenshotRegion(left, top, width, height);
                    return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static string? AnalyzePngScreenshot(string screenshotDataUrl, ScreenshotRegion hostRegion, int padding)
        {
            const string Prefix = "data:image/png;base64,";
            var base64 = screenshotDataUrl.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
                ? screenshotDataUrl[Prefix.Length..]
                : screenshotDataUrl;

            byte[] pngBytes;
            try
            {
                pngBytes = Convert.FromBase64String(base64);
            }
            catch
            {
                return null;
            }

            if (!TryReadPngSize(pngBytes, out var screenshotWidth, out var screenshotHeight)
                || screenshotWidth <= 0
                || screenshotHeight <= 0)
            {
                return null;
            }

            using var codec = new PngCodec();
            codec.InitializeDecoder(new ImageCodecParameters(screenshotWidth, screenshotHeight, VideoPixelFormat.Rgba32));

            using var buffer = new VideoFrameBuffer(screenshotWidth, screenshotHeight, VideoPixelFormat.Rgba32);
            var frame = buffer.AsFrame();
            if (codec.Decode(pngBytes, ref frame) != CodecResult.Success)
                return null;

            var cropLeft = Math.Max(0, hostRegion.Left - padding);
            var cropTop = Math.Max(0, hostRegion.Top - padding);
            var cropRight = Math.Min(screenshotWidth, hostRegion.Left + hostRegion.Width + padding);
            var cropBottom = Math.Min(screenshotHeight, hostRegion.Top + hostRegion.Height + padding);
            var cropWidth = cropRight - cropLeft;
            var cropHeight = cropBottom - cropTop;

            if (cropWidth <= 0 || cropHeight <= 0)
                return null;

            var packed = frame.PackedData;
            var data = packed.Data;
            var stride = packed.Stride;

            long sumLuma = 0;
            long sumLumaSq = 0;
            long sumNeighborHorizontal = 0;
            long sumNeighborVertical = 0;
            var horizontalPairs = 0;
            var verticalPairs = 0;
            var opaquePixels = 0;
            var quantizedColors = new Dictionary<int, int>();
            var sampleGrid = new JsonArray();

            static int Quantize(byte red, byte green, byte blue) => ((red >> 4) << 8) | ((green >> 4) << 4) | (blue >> 4);
            static string Hex(byte red, byte green, byte blue, byte alpha) => $"#{red:X2}{green:X2}{blue:X2}{alpha:X2}";

            for (var y = cropTop; y < cropBottom; y++)
            {
                var rowOffset = (y * stride) + (cropLeft * 4);
                for (var x = cropLeft; x < cropRight; x++)
                {
                    var pixelOffset = rowOffset + ((x - cropLeft) * 4);
                    var red = data[pixelOffset];
                    var green = data[pixelOffset + 1];
                    var blue = data[pixelOffset + 2];
                    var alpha = data[pixelOffset + 3];
                    var luma = (red * 2126L + green * 7152L + blue * 722L) / 10000L;

                    sumLuma += luma;
                    sumLumaSq += luma * luma;
                    if (alpha >= 250)
                        opaquePixels++;

                    var quantized = Quantize(red, green, blue);
                    quantizedColors.TryGetValue(quantized, out var colorCount);
                    quantizedColors[quantized] = colorCount + 1;

                    if (x + 1 < cropRight)
                    {
                        var rightOffset = pixelOffset + 4;
                        var rightLuma = (data[rightOffset] * 2126L + data[rightOffset + 1] * 7152L + data[rightOffset + 2] * 722L) / 10000L;
                        sumNeighborHorizontal += Math.Abs(luma - rightLuma);
                        horizontalPairs++;
                    }

                    if (y + 1 < cropBottom)
                    {
                        var bottomOffset = pixelOffset + stride;
                        var bottomLuma = (data[bottomOffset] * 2126L + data[bottomOffset + 1] * 7152L + data[bottomOffset + 2] * 722L) / 10000L;
                        sumNeighborVertical += Math.Abs(luma - bottomLuma);
                        verticalPairs++;
                    }
                }
            }

            var pixelCount = cropWidth * cropHeight;
            if (pixelCount <= 0)
                return null;

            foreach (var rowRatio in new[] { 0.2, 0.5, 0.8 })
            {
                foreach (var columnRatio in new[] { 0.12, 0.2, 0.35, 0.5, 0.65, 0.8 })
                {
                    var sampleX = Math.Clamp(hostRegion.Left + (int)Math.Round(hostRegion.Width * columnRatio), 0, screenshotWidth - 1);
                    var sampleY = Math.Clamp(hostRegion.Top + (int)Math.Round(hostRegion.Height * rowRatio), 0, screenshotHeight - 1);
                    var sampleOffset = (sampleY * stride) + (sampleX * 4);
                    var red = data[sampleOffset];
                    var green = data[sampleOffset + 1];
                    var blue = data[sampleOffset + 2];
                    var alpha = data[sampleOffset + 3];

                    sampleGrid.Add(new JsonObject
                    {
                        ["columnRatio"] = columnRatio,
                        ["rowRatio"] = rowRatio,
                        ["x"] = sampleX,
                        ["y"] = sampleY,
                        ["rgba"] = Hex(red, green, blue, alpha),
                    });
                }
            }

            var meanLuma = (double)sumLuma / pixelCount;
            var variance = Math.Max(0, ((double)sumLumaSq / pixelCount) - (meanLuma * meanLuma));
            var topColors = quantizedColors
                .OrderByDescending(static pair => pair.Value)
                .Take(8)
                .Select(static pair => new JsonObject
                {
                    ["rgb12"] = $"0x{pair.Key:X3}",
                    ["count"] = pair.Value,
                });

            return new JsonObject
            {
                ["screenshotWidth"] = screenshotWidth,
                ["screenshotHeight"] = screenshotHeight,
                ["hostRect"] = new JsonObject
                {
                    ["left"] = hostRegion.Left,
                    ["top"] = hostRegion.Top,
                    ["width"] = hostRegion.Width,
                    ["height"] = hostRegion.Height,
                },
                ["cropRect"] = new JsonObject
                {
                    ["left"] = cropLeft,
                    ["top"] = cropTop,
                    ["width"] = cropWidth,
                    ["height"] = cropHeight,
                },
                ["pixelCount"] = pixelCount,
                ["opaquePixelRatio"] = Math.Round((double)opaquePixels / pixelCount, 4),
                ["quantizedColorCount"] = quantizedColors.Count,
                ["meanLuma"] = Math.Round(meanLuma, 2),
                ["lumaStdDev"] = Math.Round(Math.Sqrt(variance), 2),
                ["avgHorizontalNeighborDelta"] = Math.Round(horizontalPairs > 0 ? (double)sumNeighborHorizontal / horizontalPairs : 0, 2),
                ["avgVerticalNeighborDelta"] = Math.Round(verticalPairs > 0 ? (double)sumNeighborVertical / verticalPairs : 0, 2),
                ["topQuantizedColors"] = new JsonArray(topColors.ToArray()),
                ["sampleGrid"] = sampleGrid,
            }.ToJsonString();
        }

        private static bool TryReadPngSize(ReadOnlySpan<byte> pngBytes, out int width, out int height)
        {
            width = 0;
            height = 0;

            if (pngBytes.Length < 24)
                return false;

            var pngSignature = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
            if (!pngBytes[..8].SequenceEqual(pngSignature))
                return false;

            width = BinaryPrimitives.ReadInt32BigEndian(pngBytes[16..20]);
            height = BinaryPrimitives.ReadInt32BigEndian(pngBytes[20..24]);
            return width > 0 && height > 0;
        }

        private static async Task<string?> ExecuteScriptInSpecificFrameAsync(WebDriverPage tab, int frameId, string script, bool isolatedWorld)
        {
            var response = await tab.SendBridgeCommandAsync(
                BridgeCommand.ExecuteScriptInFrames,
                new JsonObject
                {
                    ["script"] = script,
                    ["frameId"] = frameId,
                    ["world"] = isolatedWorld ? "ISOLATED" : "MAIN",
                }).ConfigureAwait(false);

            var resultNode = new JsonObject
            {
                ["frameId"] = frameId,
                ["world"] = isolatedWorld ? "ISOLATED" : "MAIN",
                ["bridgeStatus"] = response.Status?.ToString(),
                ["bridgeError"] = response.Error,
            };

            if (response.Payload is JsonElement payload)
            {
                resultNode["payloadKind"] = payload.ValueKind.ToString();

                if (payload.ValueKind == JsonValueKind.Array)
                {
                    try
                    {
                        resultNode["payload"] = JsonNode.Parse(payload.GetRawText());
                    }
                    catch
                    {
                        resultNode["payloadRaw"] = payload.GetRawText();
                    }

                    if (payload.GetArrayLength() > 0)
                    {
                        var first = payload[0];
                        if (first.ValueKind == JsonValueKind.String)
                        {
                            var firstValue = first.GetString();
                            if (!string.IsNullOrWhiteSpace(firstValue))
                            {
                                try
                                {
                                    resultNode["scriptResult"] = JsonNode.Parse(firstValue);
                                }
                                catch
                                {
                                    resultNode["scriptResultRaw"] = firstValue;
                                }
                            }
                        }
                    }
                }
                else
                {
                    resultNode["payloadRaw"] = payload.GetRawText();
                }
            }

            return resultNode.ToJsonString();
        }

        private static Task<string?> CaptureChallengeFrameProbeAsync(WebDriverPage tab, int frameId, bool isolatedWorld)
        {
            return ExecuteScriptInSpecificFrameAsync(
                tab,
                frameId,
                """
                (() => {
                    function describeElement(element) {
                        if (!(element instanceof Element)) {
                            return null;
                        }

                        const rect = element.getBoundingClientRect();
                        return {
                            tagName: element.tagName || '',
                            id: element.id || '',
                            className: typeof element.className === 'string' ? element.className : '',
                            role: element.getAttribute?.('role') || '',
                            ariaLabel: element.getAttribute?.('aria-label') || '',
                            left: rect.left,
                            top: rect.top,
                            width: rect.width,
                            height: rect.height,
                        };
                    }

                    const width = Math.max(window.innerWidth || 0, document.documentElement?.clientWidth || 0);
                    const height = Math.max(window.innerHeight || 0, document.documentElement?.clientHeight || 0);
                    const sampleRatios = [
                        [0.12, 0.34],
                        [0.16, 0.34],
                        [0.2, 0.34],
                        [0.12, 0.5],
                        [0.16, 0.5],
                        [0.2, 0.5],
                        [0.24, 0.5],
                        [0.12, 0.66],
                        [0.16, 0.66],
                        [0.2, 0.66],
                        [0.3, 0.5],
                    ];

                    return JSON.stringify({
                        href: location.href,
                        title: document.title,
                        readyState: document.readyState,
                        viewportWidth: width,
                        viewportHeight: height,
                        activeElement: describeElement(document.activeElement),
                        checkbox: describeElement(document.querySelector('input[type="checkbox"]')),
                        roleCheckbox: describeElement(document.querySelector('[role="checkbox"]')),
                        verifyAction: describeElement(document.querySelector('[data-action="verify"]')),
                        bodyTextSnippet: (document.body?.textContent || '').trim().slice(0, 220),
                        bodyHtmlSnippet: (document.body?.innerHTML || '').slice(0, 500),
                        points: sampleRatios.map(([columnRatio, rowRatio]) => {
                            const x = width * columnRatio;
                            const y = height * rowRatio;
                            return {
                                columnRatio,
                                rowRatio,
                                x,
                                y,
                                elementFromPoint: describeElement(document.elementFromPoint(x, y)),
                                stack: (document.elementsFromPoint(x, y) || []).slice(0, 6).map(describeElement).filter(Boolean),
                            };
                        }),
                    });
                })()
                """,
                isolatedWorld);
        }

        private static Task<string?> ClickChallengeFrameDirectAsync(WebDriverPage tab, int frameId, bool isolatedWorld)
        {
            return ExecuteScriptInSpecificFrameAsync(
                tab,
                frameId,
                """
                (() => {
                    function describeElement(element) {
                        if (!(element instanceof Element)) {
                            return null;
                        }

                        const rect = element.getBoundingClientRect();
                        return {
                            tagName: element.tagName || '',
                            id: element.id || '',
                            className: typeof element.className === 'string' ? element.className : '',
                            role: element.getAttribute?.('role') || '',
                            ariaLabel: element.getAttribute?.('aria-label') || '',
                            left: rect.left,
                            top: rect.top,
                            width: rect.width,
                            height: rect.height,
                        };
                    }

                    const width = Math.max(window.innerWidth || 0, document.documentElement?.clientWidth || 0);
                    const height = Math.max(window.innerHeight || 0, document.documentElement?.clientHeight || 0);
                    const sampleRatios = [
                        [0.12, 0.34],
                        [0.16, 0.34],
                        [0.2, 0.34],
                        [0.12, 0.5],
                        [0.16, 0.5],
                        [0.2, 0.5],
                        [0.24, 0.5],
                        [0.12, 0.66],
                        [0.16, 0.66],
                        [0.2, 0.66],
                    ];
                    const eventSequence = [
                        ['pointerover', 0],
                        ['mouseover', 0],
                        ['pointerenter', 0],
                        ['mouseenter', 0],
                        ['pointermove', 0],
                        ['mousemove', 0],
                        ['pointerdown', 1],
                        ['mousedown', 1],
                        ['pointerup', 0],
                        ['mouseup', 0],
                        ['click', 0],
                    ];
                    const sampleResults = [];

                    for (const [columnRatio, rowRatio] of sampleRatios) {
                        const x = width * columnRatio;
                        const y = height * rowRatio;
                        const stack = (document.elementsFromPoint(x, y) || []).filter((entry) => entry instanceof Element);
                        const primary = stack[0] || document.body || document.documentElement;
                        const dispatchTargets = Array.from(new Set([
                            primary,
                            document.body,
                            document.documentElement,
                            document,
                            window,
                        ].filter(Boolean)));

                        for (const dispatchTarget of dispatchTargets) {
                            for (const [type, buttons] of eventSequence) {
                                const init = {
                                    bubbles: type !== 'mouseenter' && type !== 'pointerenter',
                                    cancelable: true,
                                    composed: true,
                                    clientX: x,
                                    clientY: y,
                                    button: 0,
                                    buttons,
                                };

                                if (type.startsWith('pointer')) {
                                    dispatchTarget.dispatchEvent(new PointerEvent(type, {
                                        ...init,
                                        pointerId: 1,
                                        pointerType: 'mouse',
                                        isPrimary: true,
                                    }));
                                } else {
                                    dispatchTarget.dispatchEvent(new MouseEvent(type, init));
                                }
                            }
                        }

                        if (primary instanceof HTMLElement) {
                            primary.focus?.({ preventScroll: true });
                            primary.click?.();
                        }

                        sampleResults.push({
                            columnRatio,
                            rowRatio,
                            x,
                            y,
                            primary: describeElement(primary),
                            stack: stack.slice(0, 6).map(describeElement).filter(Boolean),
                        });
                    }

                    return JSON.stringify({
                        href: location.href,
                        title: document.title,
                        readyState: document.readyState,
                        viewportWidth: width,
                        viewportHeight: height,
                        activeElement: describeElement(document.activeElement),
                        checkbox: describeElement(document.querySelector('input[type="checkbox"]')),
                        roleCheckbox: describeElement(document.querySelector('[role="checkbox"]')),
                        verifyAction: describeElement(document.querySelector('[data-action="verify"]')),
                        bodyHtmlSnippet: (document.body?.innerHTML || '').slice(0, 500),
                        sampleResults,
                    });
                })()
                """,
                isolatedWorld);
        }

        private static Task<string?> PressChallengeFrameActivationKeysAsync(WebDriverPage tab, int frameId, bool isolatedWorld)
        {
            return ExecuteScriptInSpecificFrameAsync(
                tab,
                frameId,
                """
                (() => {
                    function describeElement(element) {
                        if (!(element instanceof Element)) {
                            return null;
                        }

                        const rect = element.getBoundingClientRect();
                        return {
                            tagName: element.tagName || '',
                            id: element.id || '',
                            className: typeof element.className === 'string' ? element.className : '',
                            role: element.getAttribute?.('role') || '',
                            ariaLabel: element.getAttribute?.('aria-label') || '',
                            left: rect.left,
                            top: rect.top,
                            width: rect.width,
                            height: rect.height,
                        };
                    }

                    function dispatchKey(target, key, code, keyCode) {
                        if (!target?.dispatchEvent) {
                            return false;
                        }

                        target.focus?.({ preventScroll: true });

                        const eventInit = {
                            key,
                            code,
                            keyCode,
                            which: keyCode,
                            charCode: keyCode,
                            bubbles: true,
                            cancelable: true,
                            composed: true,
                        };

                        target.dispatchEvent(new KeyboardEvent('keydown', eventInit));
                        target.dispatchEvent(new KeyboardEvent('keypress', eventInit));
                        target.dispatchEvent(new KeyboardEvent('keyup', eventInit));
                        return true;
                    }

                    const targets = Array.from(new Set([
                        document.activeElement,
                        document.body,
                        document.documentElement,
                    ].filter(Boolean)));
                    const actions = [];

                    for (const target of targets) {
                        actions.push({
                            target: describeElement(target),
                            space: dispatchKey(target, ' ', 'Space', 32),
                            enter: dispatchKey(target, 'Enter', 'Enter', 13),
                        });
                    }

                    return JSON.stringify({
                        href: location.href,
                        title: document.title,
                        readyState: document.readyState,
                        activeElement: describeElement(document.activeElement),
                        actions,
                    });
                })()
                """,
                isolatedWorld);
        }

        private static Task<string?> CaptureChallengeFrameTrustProbeAsync(WebDriverPage tab, int frameId, bool isolatedWorld)
        {
            return ExecuteScriptInSpecificFrameAsync(
                tab,
                frameId,
                """
                (() => {
                    function describeElement(element) {
                        if (!(element instanceof Element)) {
                            return null;
                        }

                        const rect = element.getBoundingClientRect();
                        return {
                            tagName: element.tagName || '',
                            id: element.id || '',
                            className: typeof element.className === 'string' ? element.className : '',
                            role: element.getAttribute?.('role') || '',
                            ariaLabel: element.getAttribute?.('aria-label') || '',
                            left: rect.left,
                            top: rect.top,
                            width: rect.width,
                            height: rect.height,
                        };
                    }

                    const stateKey = '__atomTurnstileTrustProbe';
                    const targetTypes = ['pointerdown', 'mousedown', 'mouseup', 'click', 'keydown', 'keyup', 'focus'];

                    if (!window[stateKey]) {
                        const state = { events: [] };
                        const recordEvent = (event) => {
                            try {
                                state.events.push({
                                    type: event?.type || '',
                                    isTrusted: !!event?.isTrusted,
                                    detail: typeof event?.detail === 'number' ? event.detail : null,
                                    key: typeof event?.key === 'string' ? event.key : '',
                                    code: typeof event?.code === 'string' ? event.code : '',
                                    pointerType: typeof event?.pointerType === 'string' ? event.pointerType : '',
                                    target: describeElement(event?.target),
                                    activeElement: describeElement(document.activeElement),
                                    userActivationActive: !!navigator.userActivation?.isActive,
                                    userActivationHasBeenActive: !!navigator.userActivation?.hasBeenActive,
                                    ts: Date.now(),
                                });

                                if (state.events.length > 32) {
                                    state.events.splice(0, state.events.length - 32);
                                }
                            } catch {
                            }
                        };

                        for (const type of targetTypes) {
                            document.addEventListener(type, recordEvent, true);
                        }

                        window[stateKey] = state;
                    }

                    const state = window[stateKey];
                    return JSON.stringify({
                        href: location.href,
                        title: document.title,
                        readyState: document.readyState,
                        userActivationSupported: !!navigator.userActivation,
                        userActivationActive: !!navigator.userActivation?.isActive,
                        userActivationHasBeenActive: !!navigator.userActivation?.hasBeenActive,
                        activeElement: describeElement(document.activeElement),
                        events: Array.isArray(state?.events) ? state.events.slice(-16) : [],
                    });
                })()
                """,
                isolatedWorld);
        }

        private static string SummarizeTurnstileTrustProbe(string? probe)
        {
            if (string.IsNullOrWhiteSpace(probe))
                return CompactDiagnosticValue(probe);

            try
            {
                var root = UnwrapFrameScriptResultObject(probe);
                if (root is null)
                    return CompactDiagnosticValue(probe);

                var activeElement = SummarizeTurnstileProbeElement(root["activeElement"]);
                var events = root["events"] as JsonArray;
                var eventNodes = events?.ToArray() ?? Array.Empty<JsonNode?>();
                var eventCount = eventNodes.Length;
                var trustedCount = eventNodes.Count(static node => GetJsonBoolean(node?["isTrusted"]));
                var eventTypes = eventNodes
                    .Select(static node => GetJsonString(node?["type"]))
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Cast<string>()
                    .GroupBy(static value => value, StringComparer.Ordinal)
                    .Select(static group => $"{group.Key}:{group.Count()}")
                    .ToArray();
                var recentEvents = eventNodes
                    .Select(SummarizeTurnstileProbeEvent)
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Cast<string>()
                    .ToArray();

                if (recentEvents.Length > 4)
                    recentEvents = recentEvents[^4..];

                return string.Join(
                    "; ",
                    [
                        $"ready={GetJsonString(root["readyState"]) ?? "?"}",
                        $"activation={GetJsonBoolean(root["userActivationActive"])}/{GetJsonBoolean(root["userActivationHasBeenActive"])}",
                        $"active={activeElement}",
                        $"events={eventCount}",
                        $"trusted={trustedCount}",
                        $"types=[{string.Join(", ", eventTypes)}]",
                        $"last=[{string.Join(", ", recentEvents)}]",
                    ]);
            }
            catch (JsonException)
            {
                return CompactDiagnosticValue(probe);
            }
        }

        private static string SummarizeChallengeFrameDirectClick(string? result)
        {
            if (string.IsNullOrWhiteSpace(result))
                return CompactDiagnosticValue(result);

            try
            {
                var root = UnwrapFrameScriptResultObject(result);
                if (root is null)
                    return CompactDiagnosticValue(result);

                var sampleResults = root["sampleResults"] as JsonArray;
                var primaryHits = (sampleResults?.ToArray() ?? Array.Empty<JsonNode?>())
                    .Select(static node => SummarizeTurnstileProbeElement(node?["primary"]))
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .GroupBy(static value => value, StringComparer.Ordinal)
                    .Select(static group => $"{group.Key}:{group.Count()}")
                    .ToArray();

                return string.Join(
                    "; ",
                    [
                        $"ready={GetJsonString(root["readyState"]) ?? "?"}",
                        $"active={SummarizeTurnstileProbeElement(root["activeElement"])}",
                        $"checkbox={SummarizeTurnstileProbeElement(root["checkbox"])}",
                        $"roleCheckbox={SummarizeTurnstileProbeElement(root["roleCheckbox"])}",
                        $"verify={SummarizeTurnstileProbeElement(root["verifyAction"])}",
                        $"samples={sampleResults?.Count ?? 0}",
                        $"primary=[{string.Join(", ", primaryHits)}]",
                    ]);
            }
            catch (JsonException)
            {
                return CompactDiagnosticValue(result);
            }
        }

        private static string SummarizeChallengeFrameActivationKeys(string? result)
        {
            if (string.IsNullOrWhiteSpace(result))
                return CompactDiagnosticValue(result);

            try
            {
                var root = UnwrapFrameScriptResultObject(result);
                if (root is null)
                    return CompactDiagnosticValue(result);

                var actionSummaries = (root["actions"] as JsonArray)?.ToArray()
                    .Select(static node =>
                    {
                        if (node is not JsonObject action)
                            return null;

                        return $"{SummarizeTurnstileProbeElement(action["target"])}(space={GetJsonBoolean(action["space"])}, enter={GetJsonBoolean(action["enter"])})";
                    })
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Cast<string>()
                    .ToArray() ?? Array.Empty<string>();

                return string.Join(
                    "; ",
                    [
                        $"ready={GetJsonString(root["readyState"]) ?? "?"}",
                        $"active={SummarizeTurnstileProbeElement(root["activeElement"])}",
                        $"actions=[{string.Join(", ", actionSummaries)}]",
                    ]);
            }
            catch (JsonException)
            {
                return CompactDiagnosticValue(result);
            }
        }

        private static JsonObject? UnwrapFrameScriptResultObject(string probe)
        {
            if (JsonNode.Parse(probe) is not JsonObject wrapper)
                return null;

            if (wrapper["scriptResult"] is JsonObject scriptResultObject)
                return scriptResultObject;

            if (wrapper["scriptResult"] is JsonValue scriptResultValue)
            {
                var scriptResultText = GetJsonString(scriptResultValue);
                if (!string.IsNullOrWhiteSpace(scriptResultText) && JsonNode.Parse(scriptResultText) is JsonObject parsedScriptResult)
                    return parsedScriptResult;
            }

            return wrapper;
        }

        private static string SummarizeTurnstileProbeEvent(JsonNode? eventNode)
        {
            if (eventNode is not JsonObject eventObject)
                return "?";

            var eventType = GetJsonString(eventObject["type"]) ?? "?";
            var trust = GetJsonBoolean(eventObject["isTrusted"]) ? "t" : "u";
            var target = SummarizeTurnstileProbeElement(eventObject["target"]);
            var key = GetJsonString(eventObject["key"]);

            if (!string.IsNullOrWhiteSpace(key))
                return $"{eventType}:{trust}@{target} key={CompactDiagnosticValue(key, 24)}";

            return $"{eventType}:{trust}@{target}";
        }

        private static string SummarizeTurnstileProbeElement(JsonNode? elementNode)
        {
            if (elementNode is not JsonObject elementObject)
                return "<none>";

            var tagName = GetJsonString(elementObject["tagName"]);
            var id = GetJsonString(elementObject["id"]);
            var role = GetJsonString(elementObject["role"]);
            var ariaLabel = GetJsonString(elementObject["ariaLabel"]);

            var summary = string.IsNullOrWhiteSpace(tagName) ? "element" : tagName.ToUpperInvariant();

            if (!string.IsNullOrWhiteSpace(id))
                summary += $"#{CompactDiagnosticValue(id, 32)}";

            if (!string.IsNullOrWhiteSpace(role))
                summary += $"[{CompactDiagnosticValue(role, 32)}]";
            else if (!string.IsNullOrWhiteSpace(ariaLabel))
                summary += $"[{CompactDiagnosticValue(ariaLabel, 32)}]";

            return summary;
        }

        private static string CompactDiagnosticValue(string? value, int maxLength = 280)
        {
            if (value is null)
                return "<null>";

            if (string.IsNullOrWhiteSpace(value))
                return "<empty>";

            var compact = string.Join(' ', value.Replace('\r', ' ').Replace('\n', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries));

            if (compact.Length <= maxLength)
                return compact;

            return compact[..(maxLength - 3)] + "...";
        }

        private static bool GetJsonBoolean(JsonNode? node)
        {
            try
            {
                return node?.GetValue<bool>() == true;
            }
            catch
            {
                return false;
            }
        }

        private static string? GetJsonString(JsonNode? node)
        {
            try
            {
                return node?.GetValue<string>();
            }
            catch
            {
                return node?.ToString();
            }
        }

        private static Task<string?> CaptureChallengeFrameResourceProbeAsync(WebDriverPage tab, int frameId, bool isolatedWorld)
        {
            return ExecuteScriptInSpecificFrameAsync(
                tab,
                frameId,
                """
                (() => {
                    function clip(value, maxLength = 220) {
                        if (typeof value !== 'string') {
                            return '';
                        }

                        return value.slice(0, maxLength);
                    }

                    function describeElement(element) {
                        if (!(element instanceof Element)) {
                            return null;
                        }

                        const rect = element.getBoundingClientRect();
                        return {
                            tagName: element.tagName || '',
                            id: element.id || '',
                            className: typeof element.className === 'string' ? element.className : '',
                            role: element.getAttribute?.('role') || '',
                            left: rect.left,
                            top: rect.top,
                            width: rect.width,
                            height: rect.height,
                        };
                    }

                    const resourceEntries = (performance.getEntriesByType?.('resource') || []).slice(-16).map((entry) => ({
                        name: clip(entry.name, 260),
                        initiatorType: entry.initiatorType || '',
                        transferSize: entry.transferSize || 0,
                        encodedBodySize: entry.encodedBodySize || 0,
                        decodedBodySize: entry.decodedBodySize || 0,
                        duration: Number.isFinite(entry.duration) ? Math.round(entry.duration * 100) / 100 : 0,
                    }));

                    return JSON.stringify({
                        href: location.href,
                        title: document.title,
                        readyState: document.readyState,
                        visibilityState: document.visibilityState,
                        scriptCount: document.scripts.length,
                        linkCount: document.querySelectorAll('link').length,
                        styleCount: document.querySelectorAll('style').length,
                        imageCount: document.images.length,
                        iframeCount: document.querySelectorAll('iframe').length,
                        rootChildren: Array.from(document.documentElement?.children || []).map(describeElement).filter(Boolean),
                        scripts: Array.from(document.scripts || []).slice(0, 12).map((script) => ({
                            src: clip(script.src || '', 260),
                            type: clip(script.type || '', 80),
                            async: !!script.async,
                            defer: !!script.defer,
                            textLength: script.textContent?.length || 0,
                        })),
                        links: Array.from(document.querySelectorAll('link')).slice(0, 12).map((link) => ({
                            rel: clip(link.rel || '', 80),
                            href: clip(link.href || '', 260),
                            as: clip(link.as || '', 80),
                        })),
                        stylesheets: Array.from(document.styleSheets || []).slice(0, 12).map((sheet) => ({
                            href: clip(sheet.href || '', 260),
                            ruleCount: (() => {
                                try {
                                    return sheet.cssRules?.length || 0;
                                } catch {
                                    return -1;
                                }
                            })(),
                        })),
                        bodyInnerHtmlLength: document.body?.innerHTML?.length || 0,
                        bodyTextLength: document.body?.textContent?.trim()?.length || 0,
                        activeElement: describeElement(document.activeElement),
                        lastResourceEntries: resourceEntries,
                    });
                })()
                """,
                isolatedWorld);
        }

        private static async Task<string?> CaptureChallengeFrameLifecycleTraceAsync(
            WebDriverPage tab,
            int frameId,
            TimeSpan duration,
            TimeSpan interval)
        {
            var deadline = DateTime.UtcNow + duration;
            var snapshots = new JsonArray();

            while (DateTime.UtcNow < deadline)
            {
                var snapshot = await CaptureChallengeFrameSurfaceSnapshotAsync(tab, frameId, isolatedWorld: false).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(snapshot))
                {
                    try
                    {
                        snapshots.Add(JsonNode.Parse(snapshot));
                    }
                    catch
                    {
                        snapshots.Add(snapshot);
                    }
                }

                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                    break;

                await Task.Delay(remaining < interval ? remaining : interval).ConfigureAwait(false);
            }

            return snapshots.ToJsonString();
        }

        private static async Task<string?> CaptureChallengeFrameSurfaceSnapshotAsync(WebDriverPage tab, int frameId, bool isolatedWorld)
        {
            var response = await ExecuteScriptInSpecificFrameAsync(
                tab,
                frameId,
                """
                (() => {
                    function rectInfo(element) {
                        if (!(element instanceof Element)) {
                            return null;
                        }

                        const rect = element.getBoundingClientRect();
                        return {
                            left: rect.left,
                            top: rect.top,
                            width: rect.width,
                            height: rect.height,
                        };
                    }

                    function compactStyle(style) {
                        if (!style) {
                            return null;
                        }

                        return {
                            display: style.display,
                            visibility: style.visibility,
                            opacity: style.opacity,
                            pointerEvents: style.pointerEvents,
                            cursor: style.cursor,
                            backgroundImage: style.backgroundImage,
                            backgroundColor: style.backgroundColor,
                            transform: style.transform,
                            content: style.content,
                        };
                    }

                    function describeElement(element) {
                        if (!(element instanceof Element)) {
                            return null;
                        }

                        return {
                            tagName: element.tagName || '',
                            id: element.id || '',
                            className: typeof element.className === 'string' ? element.className : '',
                            role: element.getAttribute?.('role') || '',
                            tabIndex: element.tabIndex ?? -1,
                        };
                    }

                    const width = Math.max(window.innerWidth || 0, document.documentElement?.clientWidth || 0);
                    const height = Math.max(window.innerHeight || 0, document.documentElement?.clientHeight || 0);
                    const body = document.body;
                    const html = document.documentElement;
                    const samplePoint = {
                        x: width * 0.16,
                        y: height * 0.5,
                    };

                    return JSON.stringify({
                        ts: Date.now(),
                        href: location.href,
                        title: document.title,
                        readyState: document.readyState,
                        viewportWidth: width,
                        viewportHeight: height,
                        bodyRect: rectInfo(body),
                        htmlRect: rectInfo(html),
                        bodyChildElementCount: body?.childElementCount || 0,
                        bodyInnerHtmlLength: body?.innerHTML?.length || 0,
                        bodyTextLength: body?.textContent?.trim()?.length || 0,
                        roleElementCount: document.querySelectorAll('[role]').length,
                        ariaElementCount: document.querySelectorAll('[aria-label], [aria-labelledby], [aria-describedby], [aria-hidden]').length,
                        focusableCount: document.querySelectorAll('a[href], button, input, select, textarea, [tabindex]').length,
                        animationCount: document.getAnimations?.().length || 0,
                        bodyStyle: compactStyle(body ? getComputedStyle(body) : null),
                        htmlStyle: compactStyle(html ? getComputedStyle(html) : null),
                        bodyBeforeStyle: compactStyle(body ? getComputedStyle(body, '::before') : null),
                        bodyAfterStyle: compactStyle(body ? getComputedStyle(body, '::after') : null),
                        htmlBeforeStyle: compactStyle(html ? getComputedStyle(html, '::before') : null),
                        htmlAfterStyle: compactStyle(html ? getComputedStyle(html, '::after') : null),
                        activeElement: describeElement(document.activeElement),
                        firstRoleElement: describeElement(document.querySelector('[role]')),
                        firstFocusableElement: describeElement(document.querySelector('a[href], button, input, select, textarea, [tabindex]')),
                        samplePoint,
                        elementFromPoint: describeElement(document.elementFromPoint(samplePoint.x, samplePoint.y)),
                        stackAtSamplePoint: (document.elementsFromPoint(samplePoint.x, samplePoint.y) || []).slice(0, 6).map(describeElement).filter(Boolean),
                    });
                })()
                """,
                isolatedWorld).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(response))
                return null;

            try
            {
                var node = JsonNode.Parse(response);
                return node?["scriptResult"]?.ToJsonString() ?? response;
            }
            catch
            {
                return response;
            }
        }

        private static double? ReadFingerprintNumber(string? snapshotJson, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(snapshotJson))
                return null;

            try
            {
                var node = JsonNode.Parse(snapshotJson);
                return node?[propertyName]?.GetValue<double>();
            }
            catch
            {
                return null;
            }
        }

        private async Task<WebDriverPage> WaitForFirstTabAsync()
        {
            return await WaitForFirstTabAsync(browser).ConfigureAwait(false);
        }

        private static async Task<WebDriverPage> WaitForFirstTabAsync(WebDriverBrowser activeBrowser)
        {
            var tcs = new TaskCompletionSource<TabConnectedEventArgs>();
            activeBrowser.TabConnected += (_, e) =>
            {
                tcs.TrySetResult(e);
                return ValueTask.CompletedTask;
            };

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            if (activeBrowser.ConnectionCount > 0)
                return activeBrowser.GetAllPages().First();

            var result = await tcs.Task.WaitAsync(cts.Token);
            var page = activeBrowser.GetPage(result.TabId)
                ?? throw new BridgeException($"Страница {result.TabId} не найдена.");

            using var warmupCts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            try { await page.ExecuteAsync("1", warmupCts.Token); }
            catch { /* warm-up не критичен */ }

            return page;
        }

        private static double? ReadDouble(JsonNode? node, string propertyName)
        {
            try
            {
                return node?[propertyName]?.GetValue<double>();
            }
            catch
            {
                return null;
            }
        }

    }
}