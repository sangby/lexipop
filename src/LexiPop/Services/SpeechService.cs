namespace LexiPop.Services;

/// <summary>
/// 离线发音：通过 COM 调用 Windows SAPI5 的 SpVoice。
/// 不依赖额外 NuGet 包；优先选择 en-US 语音，缺少英语语音包时静默失败。
/// </summary>
public static class SpeechService
{
    public const string EnglishLcid = "409"; // 0x0409 = en-US
    public const string ChineseLcid = "804";  // 0x0804 = zh-CN
    private static readonly object Sync = new();

    public static void Speak(string text, string lcid = EnglishLcid)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        _ = TestSpeakAsync(text, lcid);
    }

    /// <summary>试听并报告 SAPI 是否完成调用；不判断扬声器音量或实际听感。</summary>
    public static Task<bool> TestSpeakAsync(string text = "Hello, LexiPop", string lcid = EnglishLcid)
    {
        if (string.IsNullOrWhiteSpace(text)) return Task.FromResult(false);
        return Task.Run(() =>
        {
            try
            {
                var type = Type.GetTypeFromProgID("SAPI.SpVoice");
                if (type is null) return false;

                lock (Sync)
                {
                    dynamic sapi = Activator.CreateInstance(type)!;

                    try
                    {
                        dynamic voices = sapi.GetVoices();
                        for (var i = 0; i < (int)voices.Count; i++)
                        {
                            dynamic candidate = voices.Item(i);
                            var language = (string)candidate.GetAttribute("Language");
                            if (language == lcid)
                            {
                                sapi.Voice = candidate;
                                break;
                            }
                        }
                    }
                    catch
                    {
                        // 选语音失败就沿用默认语音
                    }

                    sapi.Speak(text, 0); // 0 = 同步
                }
                return true;
            }
            catch
            {
                return false;
            }
        });
    }
}
