using System.Runtime.CompilerServices;

namespace Atom.SourceGeneration;

/// <summary>
/// Представляет расширения генератора кода.
/// </summary>
public static class SourceBuilderExtensions
{
    /// <summary>
    /// Преобразует модификатор доступа в строковое представление.
    /// </summary>
    /// <param name="modifier">Модификатор доступа.</param>
    /// <param name="defaultModifier">Модификатор доступа по умолчанию.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string AsString(this AccessModifier modifier, AccessModifier defaultModifier) => modifier == defaultModifier
    ? string.Empty
    : modifier switch
    {
        AccessModifier.Internal => "internal",
        AccessModifier.ProtectedInternal => "protected internal",
        AccessModifier.Protected => "protected",
        AccessModifier.Private => "private",
        _ => "public",
    };

    /// <summary>
    /// Преобразует модификатор доступа в строковое представление.
    /// </summary>
    /// <param name="modifier">Модификатор доступа.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string AsString(this AccessModifier modifier) => modifier.AsString(AccessModifier.Private);
}