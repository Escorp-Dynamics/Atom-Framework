namespace Atom.Text.Tests;

[TestFixture]
public class UniqueCombinationGeneratorTests
{
    [Test]
    public async Task BaseTest()
    {
        var generator = new UniqueCombinationGenerator(3);
        await generator.FillAsync();
        Assert.That(generator.Size, Is.EqualTo(generator.Limit));
    }
}