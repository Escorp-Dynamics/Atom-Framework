using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Atom.Buffers;

namespace Atom.Web.Browsing.BiDi;

/// <summary>
/// Представляет базовый класс для набора параметров команды.
/// </summary>
public abstract class CommandParameters : IPooled
{
    /// <summary>
    /// Имя метода команды.
    /// </summary>
    [JsonIgnore]
    public abstract string MethodName { get; }

    /// <summary>
    /// Дополнительные свойства, которые будут сериализованы вместе с этой командой.
    /// </summary>
    [JsonIgnore]
    public IDictionary<string, object?> AdditionalData { get; } = new Dictionary<string, object?>();

    /// <inheritdoc/>
    public virtual void ClearForPool() => AdditionalData.Clear();

    /// <inheritdoc/>
    public static T Rent<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>() where T : IPooled => ObjectPool<T>.Shared.Rent();

    /// <inheritdoc/>
    public static void Return<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(T value) where T : IPooled => ObjectPool<T>.Shared.Return(value, x => x.ClearForPool());
}

/// <summary>
/// Представляет данные для команды WebDriver Bidi, где тип ответа известен.
/// </summary>
/// <typeparam name="T">Тип ответа для этой команды.</typeparam>
public abstract class CommandParameters<T> : CommandParameters where T : CommandResult;