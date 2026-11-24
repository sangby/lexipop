using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LexiPop.Models;
using LexiPop.Services;

namespace LexiPop.Views;

public enum LastResultToggleResult { Shown, Hidden, Unavailable }

/// <summary>
/// 划词浮窗：默认是一个小按钮，点击后展开成查询/翻译卡片。
/// 文本在弹按钮前已经取好并缓存，点击不会导致选区丢失。
/// </summary>
public partial class SelectionPopup : Window
{
    private string _selected = string.Empty;
    private int _requestVersion;
    private int _anchorX;
    private int _anchorY;
    private (string Source, string Body, (string Text, string Label, string Lcid)[] Speech)? _lastResult;

    public SelectionPopup()
    {
        InitializeComponent();
    }

    public void ShowAt(int screenX, int screenY, string text)
    {
        _selected = text;
        _requestVersion++;
        ShowButtonState();
        PositionAt(screenX, screenY);
    }

    public void ShowExpandedAt(int screenX, int screenY, string text)
    {
        _selected = text;
        _requestVersion++;
        ButtonState.Visibility = Visibility.Collapsed;
        CardState.Visibility = Visibility.Visible;
        PositionAt(screenX, screenY);
        _ = ShowCardAsync();
    }

    public LastResultToggleResult ToggleLastResultAt(int screenX, int screenY)
    {
        if (IsVisible && CardState.Visibility == Visibility.Visible)
        {
            Hide();
            return LastResultToggleResult.Hidden;
        }

        // Hide() 不销毁卡片；翻译尚未完成时也能恢复同一张卡片。
        if (!IsVisible && CardState.Visibility == Visibility.Visible &&
            !string.IsNullOrEmpty(SourceText.Text))
        {
            PositionAt(screenX, screenY);
            return LastResultToggleResult.Shown;
        }

        if (_lastResult is not { } result) return LastResultToggleResult.Unavailable;

        // 若另一次查询仍在后台进行，显示旧结果时让它停止改写当前卡片。
        _requestVersion++;
        ButtonState.Visibility = Visibility.Collapsed;
        CardState.Visibility = Visibility.Visible;
        SourceText.Text = result.Source;
        BodyText.Text = result.Body;
        SpeakPanel.Children.Clear();
        foreach (var (text, label, lcid) in result.Speech)
            AddSpeakButton(text, label, lcid);
        SourceScroll.ScrollToTop();
        BodyScroll.ScrollToTop();
        PositionAt(screenX, screenY);
        return LastResultToggleResult.Shown;
    }

    private void PositionAt(int screenX, int screenY)
    {
        _anchorX = screenX;
        _anchorY = screenY;
        var dpi = VisualTreeHelper.GetDpi(this);
        var bounds = System.Windows.Forms.Screen.FromPoint(
            new System.Drawing.Point(screenX, screenY)).WorkingArea;
        var maxHeight = Math.Max(120, bounds.Height / dpi.DpiScaleY - 24);
        CardState.MaxHeight = maxHeight;
        SourceScroll.MaxHeight = Math.Min(120, maxHeight * 0.25);
        BodyScroll.MaxHeight = Math.Max(24, maxHeight - SourceScroll.MaxHeight - 82);
        Left = screenX / dpi.DpiScaleX + 8;
        Top = screenY / dpi.DpiScaleY + 8;
        if (!IsVisible) Show();
        UpdateLayout();

        Left = Math.Clamp(Left, bounds.Left / dpi.DpiScaleX,
            Math.Max(bounds.Left / dpi.DpiScaleX,
                bounds.Right / dpi.DpiScaleX - ActualWidth));
        Top = Math.Clamp(Top, bounds.Top / dpi.DpiScaleY,
            Math.Max(bounds.Top / dpi.DpiScaleY,
                bounds.Bottom / dpi.DpiScaleY - ActualHeight));
    }

    private void ShowButtonState()
    {
        ButtonState.Visibility = Visibility.Visible;
        CardState.Visibility = Visibility.Collapsed;
    }

    private async void OnButtonClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        await ShowCardAsync();
    }

    private async Task ShowCardAsync()
    {
        var text = _selected;
        var version = _requestVersion;
        ButtonState.Visibility = Visibility.Collapsed;
        CardState.Visibility = Visibility.Visible;

        SourceText.Text = text;
        BodyText.Text = "查询中…";
        SourceScroll.ScrollToTop();
        BodyScroll.ScrollToTop();
        SpeakPanel.Children.Clear();
        PositionAt(_anchorX, _anchorY);

        var service = LookupService.Instance;

        var entry = service.Lookup(text);
        if (entry is not null)
        {
            RenderEntry(entry);
            RememberResult(SourceText.Text, BodyText.Text,
                (entry.Word, "▶ 发音", SpeechService.EnglishLcid));
            PositionAt(_anchorX, _anchorY);
            return;
        }

        if (!service.TranslationEnabled)
        {
            BodyText.Text = "长句翻译已在设置中关闭。";
            RememberResult(SourceText.Text, BodyText.Text);
            PositionAt(_anchorX, _anchorY);
            return;
        }

        if (!service.HasTranslator)
        {
            BodyText.Text = "请在设置中配置模型和 API Key。";
            RememberResult(SourceText.Text, BodyText.Text);
            PositionAt(_anchorX, _anchorY);
            return;
        }

        try
        {
            var translation = await service.TranslateAsync(text);
            if (version != _requestVersion) return;
            RememberResult(text, translation,
                (text, "▶ 原文", SpeechService.EnglishLcid),
                (translation, "▶ 译文", SpeechService.ChineseLcid));
            BodyText.Text = translation;
            AddSpeakButton(text, "▶ 原文", SpeechService.EnglishLcid);
            AddSpeakButton(translation, "▶ 译文", SpeechService.ChineseLcid);
            if (IsVisible) PositionAt(_anchorX, _anchorY);
        }
        catch (Exception ex)
        {
            if (version == _requestVersion)
            {
                var error = "翻译失败：" + ex.Message;
                RememberResult(text, error);
                BodyText.Text = error;
                if (IsVisible) PositionAt(_anchorX, _anchorY);
            }
        }
    }

    private void RememberResult(string source, string body,
        params (string Text, string Label, string Lcid)[] speech)
    {
        _lastResult = (source, body, speech);
    }

    private void RenderEntry(DictionaryEntry entry)
    {
        SourceText.Text = string.IsNullOrEmpty(entry.Phonetic)
            ? entry.Word
            : $"{entry.Word}  /{entry.Phonetic}/";

        BodyText.Text = entry.SenseLines.Count > 0
            ? string.Join("\n", entry.SenseLines)
            : entry.Translation;

        AddSpeakButton(entry.Word, "▶ 发音", SpeechService.EnglishLcid);
    }

    private void AddSpeakButton(string text, string label, string lcid)
    {
        var button = new Border
        {
            Child = new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = ThemeService.Get("ThemeTextSecondary"),
            },
            Background = ThemeService.Get("ThemeSurfaceElevated"),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(9, 4, 9, 4),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand,
        };

        button.MouseLeftButtonUp += (_, _) => SpeechService.Speak(text, lcid);
        SpeakPanel.Children.Add(button);
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        Hide();
    }

    /// <summary>点击浮窗外部时收起。参数是物理像素坐标。</summary>
    public void HideIfOutside(int screenX, int screenY)
    {
        if (!IsVisible) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        var left = Left * dpi.DpiScaleX;
        var top = Top * dpi.DpiScaleY;
        var right = left + ActualWidth * dpi.DpiScaleX;
        var bottom = top + ActualHeight * dpi.DpiScaleY;

        var inside = screenX >= left && screenX <= right
                  && screenY >= top && screenY <= bottom;

        if (!inside)
        {
            Hide();
        }
    }
}
