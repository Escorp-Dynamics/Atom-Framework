using Atom.Text;

namespace Atom.Tests.Text;

public class UniqueCombinationGeneratorTests
{
    [Fact]
    public async Task BaseTest()
    {
        var generator = new UniqueCombinationGenerator(3);
        await generator.FillAsync();
        Assert.Equal(generator.Limit, generator.Size);
    }
}