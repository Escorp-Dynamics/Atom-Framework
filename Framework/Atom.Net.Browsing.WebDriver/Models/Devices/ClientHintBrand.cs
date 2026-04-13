namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Описывает бренд и версию для navigator.userAgentData.
/// </summary>
/// <param name="Brand">Название бренда.</param>
/// <param name="Version">Версия бренда.</param>
public sealed record ClientHintBrand(string Brand, string Version);