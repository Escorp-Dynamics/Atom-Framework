namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Описывает лимиты и параметры WebGL для getParameter().
/// </summary>
public sealed class WebGLParamsSettings
{
    /// <summary>
    /// Получает или задаёт MAX_TEXTURE_SIZE.
    /// </summary>
    public int? MaxTextureSize { get; set; }

    /// <summary>
    /// Получает или задаёт MAX_RENDERBUFFER_SIZE.
    /// </summary>
    public int? MaxRenderbufferSize { get; set; }

    /// <summary>
    /// Получает или задаёт MAX_VIEWPORT_DIMS.
    /// </summary>
    public IEnumerable<int>? MaxViewportDims { get; set; }

    /// <summary>
    /// Получает или задаёт MAX_VARYING_VECTORS.
    /// </summary>
    public int? MaxVaryingVectors { get; set; }

    /// <summary>
    /// Получает или задаёт MAX_VERTEX_UNIFORM_VECTORS.
    /// </summary>
    public int? MaxVertexUniformVectors { get; set; }

    /// <summary>
    /// Получает или задаёт MAX_FRAGMENT_UNIFORM_VECTORS.
    /// </summary>
    public int? MaxFragmentUniformVectors { get; set; }
}