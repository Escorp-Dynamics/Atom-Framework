using Atom.Architect.Reactive;
using System.Drawing;

namespace Atom.Media.Video;

/// <summary>
/// Представляет устройство виртуальной камеры.
/// </summary>
public partial class VirtualCamera : Reactively
{
    /// <summary>
    /// Разрешение камеры.
    /// </summary>
    [Reactively]
    private Size resolution;

    /// <summary>
    /// Частота кадров.
    /// </summary>
    [Reactively]
    private int frameRate;

    /// <summary>
    /// .
    /// </summary>
    public VirtualCamera()
    {
        //Resolution = new Size(0, 0);
    }
}