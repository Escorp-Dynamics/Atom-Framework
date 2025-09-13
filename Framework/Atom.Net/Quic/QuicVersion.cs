#pragma warning disable CA1008

namespace Atom.Net.Quic;

/// <summary>
/// Поддерживаемые версии QUIC.
/// </summary>
public enum QuicVersion : uint
{
    /// <summary>
    /// QUIC v1 (RFC 9000/9001/9002).
    /// </summary>
    V1 = 0x00000001,
}