using Atom.Web.Browsing.BiDi;
using Atom.Web.Browsing.BiDi.Browser;

namespace Atom.Web.Browsing.Drivers;

internal static class BiDiExtensions
{
    public static async ValueTask<IEnumerable<string>> GetUserContextsAsync(this BiDiDriver biDi)
    {
        var settings = CommandParameters.Rent<GetUserContextsCommandParameters>();
        var result = await biDi.Browser.GetUserContextsAsync(settings).ConfigureAwait(false);
        CommandParameters.Return(settings);

        return result.UserContexts.Select(x => x.UserContextId);
    }

    public static async ValueTask<string> CreateUserContextAsync(this BiDiDriver biDi)
    {
        var settings = CommandParameters.Rent<CreateUserContextCommandParameters>();
        var result = await biDi.Browser.CreateUserContextAsync(settings).ConfigureAwait(false);
        CommandParameters.Return(settings);

        return result.UserContextId;
    }
}