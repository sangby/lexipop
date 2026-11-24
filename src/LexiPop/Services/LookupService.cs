using System.IO;
using LexiPop.Models;

namespace LexiPop.Services;

/// <summary>
/// 全局共享的查询服务：词库只加载一次，翻译器复用同一个 HttpClient。
/// 主窗口和划词浮窗共用。
/// </summary>
public sealed class LookupService
{
    private static readonly Lazy<LookupService> LazyInstance =
        new(() => new LookupService(), LazyThreadSafetyMode.ExecutionAndPublication);

    public static LookupService Instance => LazyInstance.Value;

    private readonly DictionaryStore? _dictionary;
    private DeepSeekTranslator? _translator;
    private readonly TranslationCache _cache = new();

    private LookupService()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "dict.bin");
        _dictionary = DictionaryStore.TryLoad(path);

        App.Log(_dictionary is null
            ? $"dict: not found at {path}"
            : $"dict: loaded {_dictionary.Count} entries");

        var settings = AppSettings.Load();
        var key = settings.ResolveDeepSeekKey();

        if (string.IsNullOrEmpty(key))
        {
            App.Log("translator: no api key");
        }
        else
        {
            _translator = new DeepSeekTranslator(key, settings.DeepSeekModel, settings.DeepSeekEndpoint);
            App.Log($"translator: enabled model={settings.DeepSeekModel} endpoint={settings.DeepSeekEndpoint}");
        }
    }

    public bool HasDictionary => _dictionary is not null;

    public bool HasTranslator => _translator is not null;

    /// <summary>
    /// 重新读取设置并重建翻译器。
    /// 设置页保存后必须调用，否则填了新 Key 也要重启程序才生效。
    /// </summary>
    public void Reload()
    {
        var settings = AppSettings.Load();
        var key = settings.ResolveDeepSeekKey();

        if (string.IsNullOrEmpty(key))
        {
            _translator = null;
            App.Log("translator: reloaded, no api key");
            return;
        }

        _translator = new DeepSeekTranslator(key, settings.DeepSeekModel, settings.DeepSeekEndpoint);
        App.Log($"translator: reloaded model={settings.DeepSeekModel} endpoint={settings.DeepSeekEndpoint}");

        // 端点或 Key 变了，旧缓存可能来自另一套配置，清掉更稳妥
        _cache.Clear();
    }

    public int DictionaryCount => _dictionary?.Count ?? 0;

    public DictionaryEntry? Lookup(string text) => _dictionary?.Lookup(text);

    /// <summary>清除翻译缓存，返回清除条数。</summary>
    public int ClearTranslationCache()
    {
        return _cache.Clear();
    }

    public async Task<string> TranslateAsync(string text, CancellationToken cancellationToken = default)
    {
        if (_translator is null)
        {
            throw new InvalidOperationException("未配置 DeepSeek API key。");
        }

        if (_cache.TryGet(text, out var cached))
        {
            App.Log($"translate: cache hit, len={text.Length}");
            return cached;
        }

        var translation = await _translator.TranslateAsync(text, cancellationToken);
        _cache.Set(text, translation);
        App.Log($"translate: cached, len={text.Length}");
        return translation;
    }
}
