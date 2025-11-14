using System.Runtime.CompilerServices;

namespace Atom.Net.Tls.Extensions;

/// <summary>
/// Minimal placeholder for SignedCertificateTimestamp TLS extension (SCT).
/// This type provides a no-op, pooled implementation to satisfy callers until full behavior is implemented.
/// </summary>
public sealed class SignedCertificateTimestampTlsExtension : TlsExtension
{
    /// <inheritdoc/>
    public override ushort Id { get; set; } = 0x0012; // arbitrary value; replace with correct id when implemented

    /// <inheritdoc/>
    public override int Size => 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(Span<byte> buffer, ref int offset)
    {
        // no-op placeholder (size == 0)
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Reset() { }
}
