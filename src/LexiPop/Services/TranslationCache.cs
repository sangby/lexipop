using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LexiPop.Services;

/// <summary>
/// 翻译缓存：内存 + 磁盘（%LOCALAPPDATA%\LexiPop\translation-cache.json）。
/// 以原文的 SHA-256 为 key，避免直接把长句当键。
/// </summary>
public sealed class TranslationCache
{
    private const int MaxEntries = 2000;

    private readonly string _path;
    private readonly Dictionary<string, string> _entries = new();
    private readonly object _lock = new();

    public TranslationCache()
    {
        _path = Path.Combine(AppSettings.DataDirectory, "translation-cache.json");
        Load();
    }

    public int Count
    {
        get { lock (_lock) return _entries.Count; }
    }

    public bool TryGet(string source, out string translation)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(Key(source), out var value))
            {
                translation = value;
                return true;
            }
        }

        translation = string.Empty;
        return false;
    }

    public void Set(string source, string translation)
    {
        lock (_lock)
        {
            if (_entries.Count >= MaxEntries)
            {
                foreach (var key in _entries.Keys.Take(MaxEntries / 2).ToList())
                {
                    _entries.Remove(key);
                }
            }

            _entries[Key(source)] = translation;
            SaveLocked();
        }
    }

    public int Clear()
    {
        lock (_lock)
        {
            var count = _entries.Count;
            _entries.Clear();

            try
            {
                if (File.Exists(_path)) File.Delete(_path);
            }
            catch
            {
                // 删除失败不影响内存缓存已清空
            }

            return count;
        }
    }

    private static string Key(string source)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.Trim())));

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;

            var json = File.ReadAllText(_path);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (loaded is null) return;

            lock (_lock)
            {
                foreach (var pair in loaded)
                {
                    _entries[pair.Key] = pair.Value;
                }
            }
        }
        catch
        {
            // 缓存损坏时直接忽略，重新积累
        }
    }

    private void SaveLocked()
    {
        try
        {
            Directory.CreateDirectory(AppSettings.DataDirectory);
            var json = JsonSerializer.Serialize(_entries);
            File.WriteAllText(_path, json);
        }
        catch
        {
            // 写盘失败不影响本次翻译结果
        }
    }
}
