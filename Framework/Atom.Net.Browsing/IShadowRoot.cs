namespace Atom.Net.Browsing;

/// <summary>
/// Представляет теневой корень (Shadow Root) элемента DOM.
/// Является скоупом, в котором все DOM-операции (поиск элементов,
/// выполнение скриптов) выполняются внутри изолированного теневого дерева.
/// </summary>
public interface IShadowRoot : IDomContext, IAsyncDisposable
{
}
