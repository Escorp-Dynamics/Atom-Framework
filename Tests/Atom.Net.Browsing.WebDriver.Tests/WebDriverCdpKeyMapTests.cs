namespace Atom.Net.Browsing.WebDriver.Tests;

[TestFixture]
public sealed class WebDriverCdpKeyMapTests
{
    [Test]
    public void LetterWithShiftProducesUpperCaseText()
    {
        var definition = CdpKeyMap.Resolve(ConsoleKey.A, ConsoleModifiers.Shift);

        Assert.Multiple(() =>
        {
            Assert.That(definition.Key, Is.EqualTo("A"));
            Assert.That(definition.Code, Is.EqualTo("KeyA"));
            Assert.That(definition.VirtualKeyCode, Is.EqualTo(65));
            Assert.That(definition.Text, Is.EqualTo("A"));
        });
    }

    [Test]
    public void ControlChordSuppressesText()
    {
        var definition = CdpKeyMap.Resolve(ConsoleKey.C, ConsoleModifiers.Control);

        Assert.Multiple(() =>
        {
            Assert.That(definition.Text, Is.Null);
            Assert.That(CdpKeyMap.ToModifierMask(ConsoleModifiers.Control | ConsoleModifiers.Shift | ConsoleModifiers.Alt), Is.EqualTo(11));
        });
    }

    [Test]
    public void NamedAndPunctuationKeysMapToDomNames()
    {
        var enter = CdpKeyMap.Resolve(ConsoleKey.Enter, ConsoleModifiers.None);
        var backspace = CdpKeyMap.Resolve(ConsoleKey.Backspace, ConsoleModifiers.None);
        var plus = CdpKeyMap.Resolve(ConsoleKey.OemPlus, ConsoleModifiers.Shift);
        var equal = CdpKeyMap.Resolve(ConsoleKey.OemPlus, ConsoleModifiers.None);
        var f5 = CdpKeyMap.Resolve(ConsoleKey.F5, ConsoleModifiers.None);

        Assert.Multiple(() =>
        {
            Assert.That((enter.Key, enter.VirtualKeyCode, enter.Text), Is.EqualTo(("Enter", 13, "\r")));
            Assert.That((backspace.Key, backspace.Text), Is.EqualTo(("Backspace", (string?)null)));
            Assert.That((plus.Key, plus.Code, plus.Text), Is.EqualTo(("+", "Equal", "+")));
            Assert.That((equal.Key, equal.Text), Is.EqualTo(("=", "=")));
            Assert.That((f5.Key, f5.Text), Is.EqualTo(("F5", (string?)null)));
        });
    }

    [Test]
    public void CharactersOutsideLatinLayoutCarryTextWithoutKeyCode()
    {
        var (cyrillic, cyrillicModifiers) = CdpKeyMap.ResolveCharacter("ж");
        var (upper, upperModifiers) = CdpKeyMap.ResolveCharacter("Q");

        Assert.Multiple(() =>
        {
            Assert.That((cyrillic.Text, cyrillic.VirtualKeyCode, cyrillicModifiers), Is.EqualTo(("ж", 0, 0)));
            Assert.That((upper.Text, upper.Code, upperModifiers), Is.EqualTo(("Q", "KeyQ", 8)));
        });
    }
}
