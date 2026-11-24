using System.IO;
using LexiPop.Models;

namespace LexiPop.Services;

/// <summary>
/// 全局共享的查询服务：词库只加载一次，翻译器复用同一个 HttpClient。
/// 热键和拖选操作共用。
/// </summary>
public sealed class LookupService
{
    private static readonly Lazy<LookupService> LazyInstance =
        new(() => new LookupService(), LazyThreadSafetyMode.ExecutionAndPublication);

    public static LookupService Instance => LazyInstance.Value;

    private readonly DictionaryStore? _dictionary;
    private ChatTranslator? _translator;
    private bool _translationEnabled;
    private readonly TranslationCache _cache = new();

    private LookupService()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "dict.bin");
        _dictionary = DictionaryStore.TryLoad(path);

        App.Log(_dictionary is null
            ? $"dict: not found at {path}"
            : $"dict: loaded {_dictionary.Count} entries");

        ConfigureTranslator(clearCache: false);
    }

    public bool HasDictionary => _dictionary is not null;

    public bool HasTranslator => _translationEnabled && _translator is not null;
    public bool TranslationEnabled => _translationEnabled;

    /// <summary>
    /// 重新读取设置并重建翻译器。
    /// 设置页保存后调用，立即更新提供方、Key 和模型。
    /// </summary>
    public void Reload() => ConfigureTranslator(clearCache: true);

    private void ConfigureTranslator(bool clearCache)
    {
        var settings = AppSettings.Load();
        var key = settings.ResolveApiKey();
        _translationEnabled = settings.EnableTranslation;

        if (string.IsNullOrEmpty(key) || string.IsNullOrWhiteSpace(settings.Model) ||
            !ModelProviders.IsValidEndpoint(settings.Endpoint))
        {
            _translator = null;
            App.Log("translator: unavailable (key, model or endpoint missing)");
            return;
        }

        _translator = new ChatTranslator(settings.ProviderId, key, settings.Model, settings.Endpoint);
        App.Log($"translator: reloaded provider={settings.ProviderId} model={settings.Model} endpoint={settings.Endpoint}");

        // 端点或 Key 变了，旧缓存可能来自另一套配置，清掉更稳妥
        if (clearCache) _cache.Clear();
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
        if (!_translationEnabled)
            throw new InvalidOperationException("长句翻译已在设置中关闭。");

        if (_translator is null)
        {
            throw new InvalidOperationException("请配置模型、接口端点和 API Key。");
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
