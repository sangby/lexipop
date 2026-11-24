using System.IO;
using System.Text;
using LexiPop.Models;

namespace LexiPop.Services;

/// <summary>
/// 加载自定义二进制词库（由 tools/build-dict.py 生成）。
/// 格式：magic "LXPD" + version(uint16) + count(uint32) + 逐条记录。
/// </summary>
public sealed class DictionaryStore
{
    private const string Magic = "LXPD";

    private readonly Dictionary<string, DictionaryEntry> _entries;

    private DictionaryStore(Dictionary<string, DictionaryEntry> entries) => _entries = entries;

    public int Count => _entries.Count;

    public static DictionaryStore? TryLoad(string path)
    {
        if (!File.Exists(path)) return null;

        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);

            var magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
            if (magic != Magic) return null;

            var version = reader.ReadUInt16();
            if (version != 1) return null;

            var count = reader.ReadInt32();
            if (count <= 0 || count > 5_000_000) return null;

            var entries = new Dictionary<string, DictionaryEntry>(count, StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < count; i++)
            {
                var word = ReadUtf8(reader, reader.ReadUInt16());
                var phonetic = ReadUtf8(reader, reader.ReadUInt16());
                var translation = ReadUtf8(reader, (int)reader.ReadUInt32());

                var pos = reader.ReadByte();
                var collins = reader.ReadByte();
                var bnc = reader.ReadUInt32();
                var frq = reader.ReadUInt32();

                entries[word] = new DictionaryEntry(word, phonetic, translation, pos, collins, bnc, frq);
            }

            return new DictionaryStore(entries);
        }
        catch
        {
            return null;
        }
    }

    private static string ReadUtf8(BinaryReader reader, int length)
    {
        if (length <= 0) return string.Empty;
        var bytes = reader.ReadBytes(length);
        return Encoding.UTF8.GetString(bytes);
    }

    public DictionaryEntry? Lookup(string word)
    {
        var key = word.Trim();
        if (key.Length == 0) return null;

        if (_entries.TryGetValue(key, out var entry)) return entry;

        // 去掉常见词形后缀再试一次
        foreach (var candidate in GenerateStems(key))
        {
            if (_entries.TryGetValue(candidate, out entry)) return entry;
        }

        return null;
    }

    private static IEnumerable<string> GenerateStems(string word)
    {
        if (word.Length <= 3) yield break;

        if (word.EndsWith("ies", StringComparison.OrdinalIgnoreCase))
            yield return word[..^3] + "y";

        if (word.EndsWith("es", StringComparison.OrdinalIgnoreCase))
            yield return word[..^2];

        if (word.EndsWith('s'))
            yield return word[..^1];

        if (word.EndsWith("ed", StringComparison.OrdinalIgnoreCase))
        {
            yield return word[..^2];
            yield return word[..^1];
        }

        if (word.EndsWith("ing", StringComparison.OrdinalIgnoreCase))
        {
            yield return word[..^3];
            yield return word[..^3] + "e";
        }
    }
}
