namespace Atom.IO.Compression;

/// <summary>
/// Провайдер словарей Zstandard. Возвращает байты словаря по DictionaryId.
/// Поддерживаются raw-content и форматированные словари (RFC: 0xEC30A437).
/// </summary>
public interface IZstdDictionaryProvider
{
    /// <summary>
    /// Возвращает байты словаря по <paramref name="dictionaryId"/>.
    /// Поддерживаются как форматированные словари (magic 0xEC30A437), так и raw‑content.
    /// </summary>
    /// <param name="dictionaryId">Идентификатор словаря (DID) из кадра.</param>
    /// <param name="dictionaryBytes">Буфер словаря. Не изменять содержимое.</param>
    /// <returns>true, если словарь найден; иначе false.</returns>
    bool TryGet(uint dictionaryId, out ReadOnlyMemory<byte> dictionaryBytes);
}
