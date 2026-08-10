using System.Text.Json;

namespace Atom.Net.Browsing.WebDriver.Tests;

/// <summary>
/// Разбор скалярных значений из результата пользовательского скрипта.
/// </summary>
/// <remarks>
/// Эти тесты закрывают целый класс молчаливых дефектов. Расширение приводит результат скрипта к
/// строке (<c>String(result)</c>), поэтому логическое значение приходит как <c>"true"</c>, а число —
/// как <c>"42"</c>. Пока разбор принимал только литералы JSON, <c>EvaluateAsync&lt;bool&gt;</c> молча
/// возвращал <see langword="false"/> при любом результате, а <c>EvaluateAsync&lt;int&gt;</c> и
/// <c>&lt;double&gt;</c> бросали исключение: <c>JsonElement.TryGetInt32</c> требует числового вида.
/// Набор этого не ловил вовсе — во всех тестах вызывался только <c>EvaluateAsync&lt;string&gt;</c>.
/// </remarks>
[TestFixture]
public sealed class WebDriverScriptResultValueTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [TestCase("true", true)]
    [TestCase("false", false)]
    [TestCase("\"true\"", true)]
    [TestCase("\"false\"", false)]
    [TestCase("\"True\"", true)]
    [TestCase("\"TRUE\"", true)]
    public void TryReadBooleanAcceptsBothJsonLiteralAndItsStringForm(string json, bool expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(ScriptResultValue.TryReadBoolean(Parse(json), out var value), Is.True, json);
            Assert.That(value, Is.EqualTo(expected), json);
        });
    }

    [TestCase("null")]
    [TestCase("\"\"")]
    [TestCase("\"null\"")]
    [TestCase("\"undefined\"")]
    [TestCase("\"yes\"")]
    [TestCase("\"1\"")]
    [TestCase("1")]
    [TestCase("{}")]
    [TestCase("[]")]
    public void TryReadBooleanRefusesEverythingThatIsNotABoolean(string json)
    {
        Assert.That(ScriptResultValue.TryReadBoolean(Parse(json), out var value), Is.False, json);
        Assert.That(value, Is.False, json);
    }

    [TestCase("42", 42)]
    [TestCase("\"42\"", 42)]
    [TestCase("-7", -7)]
    [TestCase("\"-7\"", -7)]
    [TestCase("0", 0)]
    public void TryReadInt32AcceptsBothJsonLiteralAndItsStringForm(string json, int expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(ScriptResultValue.TryReadInt32(Parse(json), out var value), Is.True, json);
            Assert.That(value, Is.EqualTo(expected), json);
        });
    }

    // Главное здесь — что метод ВОЗВРАЩАЕТ false, а не бросает: прежний код звал TryGetInt32 на
    // значении любого вида, а тот на нечисловом виде выбрасывает InvalidOperationException.
    [TestCase("\"не число\"")]
    [TestCase("\"\"")]
    [TestCase("null")]
    [TestCase("true")]
    [TestCase("{}")]
    [TestCase("[]")]
    [TestCase("\"3.5\"")]
    [TestCase("3.5")]
    [TestCase("\"2147483648\"")]
    [TestCase("2147483648")]
    public void TryReadInt32RefusesWithoutThrowing(string json)
    {
        var element = Parse(json);

        Assert.DoesNotThrow(() => ScriptResultValue.TryReadInt32(element, out _), json);
        Assert.That(ScriptResultValue.TryReadInt32(element, out var value), Is.False, json);
        Assert.That(value, Is.Zero, json);
    }

    [TestCase("3.5", 3.5)]
    [TestCase("\"3.5\"", 3.5)]
    [TestCase("\"-0.25\"", -0.25)]
    [TestCase("42", 42d)]
    [TestCase("\"42\"", 42d)]
    [TestCase("\"1e3\"", 1000d)]
    public void TryReadDoubleAcceptsBothJsonLiteralAndItsStringForm(string json, double expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(ScriptResultValue.TryReadDouble(Parse(json), out var value), Is.True, json);
            Assert.That(value, Is.EqualTo(expected).Within(1e-9), json);
        });
    }

    // Разбор идёт в инвариантной культуре и без разделителей разрядов. Иначе «1,5» превратилось бы
    // в 15 на любой машине — молчаливая порча данных, которую вызывающий никак не заметит.
    [TestCase("\"1,5\"")]
    [TestCase("\"1 234\"")]
    [TestCase("\"1234,5\"")]
    [TestCase("\"не число\"")]
    [TestCase("\"\"")]
    [TestCase("null")]
    [TestCase("true")]
    [TestCase("{}")]
    public void TryReadDoubleRefusesWithoutThrowing(string json)
    {
        var element = Parse(json);

        Assert.DoesNotThrow(() => ScriptResultValue.TryReadDouble(element, out _), json);
        Assert.That(ScriptResultValue.TryReadDouble(element, out var value), Is.False, json);
        Assert.That(value, Is.Zero, json);
    }

    [Test]
    public void TryReadDoubleIsCultureInvariant()
    {
        var previousCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            // Культура с запятой в роли десятичного разделителя: разбор не должен от неё зависеть.
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ru-RU");

            Assert.Multiple(() =>
            {
                Assert.That(ScriptResultValue.TryReadDouble(Parse("\"3.5\""), out var dotted), Is.True);
                Assert.That(dotted, Is.EqualTo(3.5).Within(1e-9));
                Assert.That(ScriptResultValue.TryReadDouble(Parse("\"3,5\""), out _), Is.False);
            });
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previousCulture;
        }
    }
}
