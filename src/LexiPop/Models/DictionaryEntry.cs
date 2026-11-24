namespace LexiPop.Models;

/// <summary>词库中的一条词条。</summary>
public sealed record DictionaryEntry(
    string Word,
    string Phonetic,
    string Translation,
    byte Pos,
    byte Collins,
    uint Bnc,
    uint Frq)
{
    private static readonly string[] PosNames =
    [
        "", "n.", "v.", "adj.", "adv.", "prep.", "conj.",
        "pron.", "num.", "art.", "int.", "aux.", "abbr.",
    ];

    public string PosName => Pos < PosNames.Length ? PosNames[Pos] : string.Empty;

    /// <summary>释义按行拆分，供 UI 逐行渲染。</summary>
    public IReadOnlyList<string> SenseLines =>
        Translation
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
}
