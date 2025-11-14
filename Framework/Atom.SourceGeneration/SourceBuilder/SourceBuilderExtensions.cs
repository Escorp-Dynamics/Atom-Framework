using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Atom.Text;

namespace Atom.SourceGeneration;

/// <summary>
/// Представляет расширения генератора кода.
/// </summary>
public static class SourceBuilderExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static string GetTypeName(this string name, [NotNull] params IEnumerable<string> usings)
    {
        if (string.IsNullOrEmpty(name)) return "void";
        var type = name.Replace("global::", newValue: null, StringComparison.Ordinal);

        foreach (var u in usings)
        {
            var dots = u.CountOf('.', StringComparison.Ordinal) + 1;
            var currentDots = type.CountOf('.', StringComparison.Ordinal);

            if (dots != currentDots) continue;

            type = type.Replace(u + '.', newValue: null);
        }

        return type;
    }

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
        AccessModifier.File => "file",
        _ => "public",
    };

    /// <summary>
    /// Преобразует модификатор доступа в строковое представление.
    /// </summary>
    /// <param name="modifier">Модификатор доступа.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string AsString(this AccessModifier modifier) => modifier.AsString(AccessModifier.Private);
}