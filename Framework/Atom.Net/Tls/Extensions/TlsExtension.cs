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
    /// PSK Key Exchange Modes (0x002d).
    /// </summary>
    public static PskKeyExchangeModesTlsExtension PskKeyExchangeModes => Rent<PskKeyExchangeModesTlsExtension>();

    /// <summary>
    /// 
    /// </summary>
    public static PreSharedKeyTlsExtension PreSharedKey => Rent<PreSharedKeyTlsExtension>();

    /// <summary>
    /// SessionTicket (0x0023).
    /// </summary>
    public static SessionTicketExtension SessionTicket => Rent<SessionTicketExtension>();

    /// <summary>
    /// Padding (0x0015).
    /// </summary>
    public static PaddingTlsExtension Padding => Rent<PaddingTlsExtension>();

    /// <summary>
    /// Encrypt-Then-MAC (0x0016).
    /// </summary>
    public static EncryptThenMacTlsExtension EncryptThenMac => Rent<EncryptThenMacTlsExtension>();

    /// <summary>
    /// Extended Master Secret (0x0017).
    /// </summary>
    public static ExtendedMasterSecretTlsExtension ExtendedMasterSecret => Rent<ExtendedMasterSecretTlsExtension>();

    /// <summary>
    /// Renegotiation Info (0xff01).
    /// </summary>
    public static RenegotiationInfoTlsExtension RenegotiationInfo => Rent<RenegotiationInfoTlsExtension>();

    /// <summary>
    /// Record Size Limit (0x001C).
    /// </summary>
    public static RecordSizeLimitTlsExtension RecordSizeLimit => Rent<RecordSizeLimitTlsExtension>();

    /// <summary>
    /// signature_algorithms (0x000D).
    /// </summary>
    public static SignatureAlgorithmsTlsExtension SignatureAlgorithms => Rent<SignatureAlgorithmsTlsExtension>();

    /// <summary>
    /// supported_groups (0x000A).
    /// </summary>
    public static SupportedGroupsTlsExtension SupportedGroups => Rent<SupportedGroupsTlsExtension>();

    /// <summary>
    /// supported_groups (0x000A).
    /// </summary>
    public static KeyShareTlsExtension KeyShare => Rent<KeyShareTlsExtension>();

    /// <summary>
    /// signature_algorithms_cert (0x0032).
    /// </summary>
    public static SignatureAlgorithmsCertTlsExtension SignatureAlgorithmsCert => Rent<SignatureAlgorithmsCertTlsExtension>();

    /// <summary>
    /// application_settings (0x00FF).
    /// </summary>
    public static ApplicationSettingsTlsExtension ApplicationSettings => Rent<ApplicationSettingsTlsExtension>();

    /// <summary>
    /// GREASE.
    /// </summary>
    public static GreaseTlsExtension Grease => Rent<GreaseTlsExtension>();

    /// <summary>
    /// status_request (0x0005).
    /// </summary>
    public static StatusRequestTlsExtension StatusRequest => Rent<StatusRequestTlsExtension>();

    /// <summary>
    /// delegated_credentials (0x0037).
    /// </summary>
    public static DelegatedCredentialsTlsExtension DelegatedCredentials => Rent<DelegatedCredentialsTlsExtension>();

    /// <summary>
    /// compress_certificate (0x001B).
    /// </summary>
    public static CompressCertificateTlsExtension CompressCertificate => Rent<CompressCertificateTlsExtension>();

    /// <summary>
    /// early_data (0x002A).
    /// </summary>
    public static EarlyDataTlsExtension EarlyData => Rent<EarlyDataTlsExtension>();

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