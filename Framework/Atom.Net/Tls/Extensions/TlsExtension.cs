using System.Runtime.CompilerServices;
using Atom.Buffers;

namespace Atom.Net.Tls.Extensions;

/// <summary>
/// Коллекция расширений.
/// </summary>
public abstract partial class TlsExtension : ITlsExtension
{
    /// <inheritdoc/>
    public abstract ushort Id { get; set; }

    /// <inheritdoc/>
    public abstract int Size { get; }

    /// <summary>
    /// SessionTicket (0x0023).
    /// </summary>
    public static SessionTicketExtension SessionTicket => Rent<SessionTicketExtension>();

    /// <summary>
    /// Extended Master Secret (0x0017).
    /// </summary>
    public static ExtendedMasterSecretTlsExtension ExtendedMasterSecret => Rent<ExtendedMasterSecretTlsExtension>();

    /// <summary>
    /// Renegotiation Info (0xff01).
    /// </summary>
    public static RenegotiationInfoTlsExtension RenegotiationInfo => Rent<RenegotiationInfoTlsExtension>();

    /// <summary>
    /// signature_algorithms (0x000D).
    /// </summary>
    public static SignatureAlgorithmsTlsExtension SignatureAlgorithms => Rent<SignatureAlgorithmsTlsExtension>();

    /// <summary>
    /// supported_groups (0x000A).
    /// </summary>
    public static SupportedGroupsTlsExtension SupportedGroups => Rent<SupportedGroupsTlsExtension>();

    /// <summary>
    /// GREASE.
    /// </summary>
    public static GreaseTlsExtension Grease => Rent<GreaseTlsExtension>();

    /// <summary>
    /// server_name (0x0000).
    /// </summary>
    public static ServerNameTlsExtension ServerName => Rent<ServerNameTlsExtension>();

    /// <summary>
    /// application_layer_protocol_negotiation (0x0010).
    /// </summary>
    public static AlpnTlsExtension Alpn => Rent<AlpnTlsExtension>();

    /// <summary>
    /// supported_versions (0x002B).
    /// </summary>
    public static SupportedVersionsTlsExtension SupportedVersions => Rent<SupportedVersionsTlsExtension>();

    /// <summary>
    /// ec_point_formats (0x000B).
    /// </summary>
    public static EcPointFormatsTlsExtension EcPointFormats => Rent<EcPointFormatsTlsExtension>();

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public abstract void Write(Span<byte> buffer, ref int offset);

    /// <inheritdoc/>
    [Pooled]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void Reset() { }
}