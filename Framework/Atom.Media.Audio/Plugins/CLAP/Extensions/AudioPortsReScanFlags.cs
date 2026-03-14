namespace Atom.Media.Audio.Plugins.CLAP.Extensions;

/// <summary>
/// 
/// </summary>
[Flags]
public enum AudioPortsReScanFlags : uint
{
    /// <summary>
    /// 
    /// </summary>
    Names = 1 << 0,
    /// <summary>
    /// 
    /// </summary>
    Flags = 1 << 1,
    /// <summary>
    /// 
    /// </summary>
    ChannelCount = 1 << 2,
    /// <summary>
    /// 
    /// </summary>
    PortType = 1 << 3,
    /// <summary>
    /// 
    /// </summary>
    InPlacePair = 1 << 4,
    /// <summary>
    /// 
    /// </summary>
    List = 1 << 5,
}