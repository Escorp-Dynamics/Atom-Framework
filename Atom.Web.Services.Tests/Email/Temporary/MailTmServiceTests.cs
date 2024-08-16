namespace Atom.Web.Services.Email.Temporary.Tests;

public class MailTmServiceTests
{
    [Fact]
    public async Task GetDomainsTest()
    {
        var domain = await MailTmService.Factory.GetNextDomainAsync();
        Assert.NotNull(domain);
        Assert.NotEmpty(domain);
    }
}