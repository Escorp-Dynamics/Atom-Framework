#pragma warning disable CA1008

namespace Atom.Net.Tls;

/// <summary>
/// Уровень тревоги.
/// </summary>
public enum TlsAlertLevel : byte
{
    /// <summary>
    /// 
    /// </summary>
    Warning = 1,
    /// <summary>
    /// 
    /// </summary>
    Fatal = 2,
}