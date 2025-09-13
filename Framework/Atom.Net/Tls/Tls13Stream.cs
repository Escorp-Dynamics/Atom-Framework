using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Atom.Net.Tls;

/// <summary>
/// Реализация потока TLS 1.3.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="Tls13Stream"/>.
/// </remarks>
/// <param name="settings">Настройки TLS.</param>
/// <param name="stream">Сетевой поток.</param>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public sealed class Tls13Stream([NotNull] NetworkStream stream, in TlsSettings settings) : Tls12Stream(stream, settings);