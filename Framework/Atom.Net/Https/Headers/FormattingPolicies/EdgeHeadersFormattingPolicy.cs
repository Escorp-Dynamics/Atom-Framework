using System.Diagnostics.CodeAnalysis;

namespace Atom.Net.Https.Headers;

/// <summary>
/// Политика форматирования заголовков Microsoft Edge.
/// </summary>
/// <remarks>
/// Нарочно пустой наследник Chrome-политики, а не забытая заглушка: Edge собирает заголовки тем
/// же сетевым стеком Chromium, и на проводе его порядок/кейс совпадают с Chrome — расхождение
/// живёт в значениях (бренд <c lang="text">Edg/</c> в User-Agent и client hints), а не в форматировании.
/// Класс нужен как точка выбора, чтобы будущее измеренное расхождение Edge переопределило
/// только его, не трогая Chrome.
/// </remarks>
[SuppressMessage("Major Code Smell", "S2094:Classes should not be empty", Justification = "Marker type for Edge-specific header policy selection.")]
public class EdgeHeadersFormattingPolicy : ChromeHeadersFormattingPolicy;
