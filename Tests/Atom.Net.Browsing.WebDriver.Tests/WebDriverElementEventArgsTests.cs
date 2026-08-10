using System.Text.Json;

namespace Atom.Net.Browsing.WebDriver.Tests;

/// <summary>
/// Разбор полезной нагрузки события DOM в аргументы обработчика.
/// </summary>
/// <remarks>
/// Скрипт-слушатель отправляет <c>button</c>, <c>clientX</c> и <c>clientY</c> как JSON <c>null</c>
/// для всех немышиных событий: у объекта события просто нет таких свойств. Прежде эти поля читались
/// напрямую через <c>JsonElement.TryGetInt32</c>/<c>TryGetDouble</c>, а те выбрасывают исключение,
/// когда вид значения не числовой, — и создание аргументов падало на каждом событии ввода,
/// клавиатуры, фокуса и отправки формы. Исключение терялось в незанаблюдённой задаче ретрансляции,
/// поэтому подписка молча не срабатывала: ни ошибки, ни записи в журнале.
/// </remarks>
[TestFixture]
public sealed class WebDriverElementEventArgsTests
{
    private static ElementEventArgs FromJson(string json)
        => ElementEventArgs.FromPayload(JsonDocument.Parse(json).RootElement);

    [TestCase("input")]
    [TestCase("keydown")]
    [TestCase("keyup")]
    [TestCase("change")]
    [TestCase("focus")]
    [TestCase("blur")]
    [TestCase("submit")]
    public void FromPayloadHandlesNonMouseEventsWhereCoordinatesArriveAsNull(string eventType)
    {
        var json = $$"""
            {
                "type": "{{eventType}}",
                "isTrusted": true,
                "targetId": "search",
                "currentTargetId": "search",
                "value": "abc",
                "key": null,
                "code": null,
                "button": null,
                "clientX": null,
                "clientY": null
            }
            """;

        ElementEventArgs args = null!;
        Assert.DoesNotThrow(() => args = FromJson(json), eventType);

        Assert.Multiple(() =>
        {
            Assert.That(args.Type, Is.EqualTo(eventType));
            Assert.That(args.IsTrusted, Is.True);
            Assert.That(args.TargetId, Is.EqualTo("search"));
            Assert.That(args.Value, Is.EqualTo("abc"));
            Assert.That(args.Button, Is.Null);
            Assert.That(args.ClientX, Is.Null);
            Assert.That(args.ClientY, Is.Null);
        });
    }

    [Test]
    public void FromPayloadReadsMouseCoordinatesWhenTheyArePresent()
    {
        var args = FromJson("""
            {
                "type": "click",
                "isTrusted": true,
                "targetId": "trusted-button",
                "button": 0,
                "clientX": 53.5,
                "clientY": 25
            }
            """);

        Assert.Multiple(() =>
        {
            Assert.That(args.Type, Is.EqualTo("click"));
            Assert.That(args.Button, Is.Zero);
            Assert.That(args.ClientX, Is.EqualTo(53.5).Within(1e-9));
            Assert.That(args.ClientY, Is.EqualTo(25d).Within(1e-9));
        });
    }

    // Мост доставляет результат скрипта строкой, поэтому числовые поля могут приехать в строковой
    // записи. Это должно читаться, а не молча теряться.
    [Test]
    public void FromPayloadReadsNumbersDeliveredInStringForm()
    {
        var args = FromJson("""
            {
                "type": "click",
                "button": "2",
                "clientX": "10.25",
                "clientY": "40"
            }
            """);

        Assert.Multiple(() =>
        {
            Assert.That(args.Button, Is.EqualTo(2));
            Assert.That(args.ClientX, Is.EqualTo(10.25).Within(1e-9));
            Assert.That(args.ClientY, Is.EqualTo(40d).Within(1e-9));
        });
    }

    [TestCase("""{"type":"click","button":"не число","clientX":{},"clientY":[]}""")]
    [TestCase("""{"type":"click","button":true,"clientX":"","clientY":"abc"}""")]
    [TestCase("""{}""")]
    [TestCase("""[]""")]
    [TestCase("""null""")]
    [TestCase("""42""")]
    public void FromPayloadNeverThrowsOnMalformedPayloads(string json)
    {
        Assert.DoesNotThrow(() => FromJson(json), json);
    }

    [Test]
    public void FromPayloadKeepsRawPayloadAvailableToTheCaller()
    {
        var args = FromJson("""{"type":"custom","detail":{"nested":1}}""");

        Assert.Multiple(() =>
        {
            Assert.That(args.Type, Is.EqualTo("custom"));
            Assert.That(args.Payload.ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(args.Payload.GetProperty("detail").GetProperty("nested").GetInt32(), Is.EqualTo(1));
        });
    }
}
