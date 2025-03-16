using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Atom.Text;

namespace Atom.SourceGeneration;

/// <summary>
/// Представляет расширения генератора кода.
/// </summary>
public static class SourceBuilderExtensions
{
    internal static string GetTypeName(this string name, [NotNull] params string[] usings)
    {
        if (string.IsNullOrEmpty(name)) return "void";
        var type = name.Replace("global::", null);

        foreach (var u in usings)
        {
            var dots = u.CountOf('.') + 1;
            var currentDots = type.CountOf('.');

            if (dots != currentDots) continue;

            type = type.Replace(u + '.', null);
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