namespace Atom.Web.Browsing.DOM;

internal static class ParentNode
{
    public static void Prepend(params IEnumerable<INode> nodes) => throw new NotImplementedException();

    public static void Prepend(params IEnumerable<string> nodes) => throw new NotImplementedException();

    public static void Append(params IEnumerable<INode> nodes) => throw new NotImplementedException();

    public static void Append(params IEnumerable<string> nodes) => throw new NotImplementedException();

    public static void ReplaceChildren(params IEnumerable<INode> nodes) => throw new NotImplementedException();

    public static void ReplaceChildren(params IEnumerable<string> nodes) => throw new NotImplementedException();

    public static IElement? QuerySelector(string selectors) => throw new NotImplementedException();

    public static INodeList QuerySelectorAll(string selectors) => throw new NotImplementedException();
}