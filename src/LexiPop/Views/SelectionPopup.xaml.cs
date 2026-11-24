using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LexiPop.Models;
using LexiPop.Services;

namespace LexiPop.Views;

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

    public bool HasLastResult => _lastResult is not null;

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

    public bool ShowLastResultAt(int screenX, int screenY)
    {
        if (_lastResult is not { } result) return false;

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
        return true;
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
            RememberResult((entry.Word, "▶ 发音", SpeechService.EnglishLcid));
            PositionAt(_anchorX, _anchorY);
            return;
        }

        if (!service.TranslationEnabled)
        {
            BodyText.Text = "长句翻译已在设置中关闭。";
            PositionAt(_anchorX, _anchorY);
            return;
        }

        if (!service.HasTranslator)
        {
            BodyText.Text = "请在设置中配置模型和 API Key。";
            PositionAt(_anchorX, _anchorY);
            return;
        }

        try
        {
            var translation = await service.TranslateAsync(text);
            if (version != _requestVersion || !IsVisible) return;
            BodyText.Text = translation;
            AddSpeakButton(text, "▶ 原文", SpeechService.EnglishLcid);
            AddSpeakButton(translation, "▶ 译文", SpeechService.ChineseLcid);
            RememberResult(
                (text, "▶ 原文", SpeechService.EnglishLcid),
                (translation, "▶ 译文", SpeechService.ChineseLcid));
            PositionAt(_anchorX, _anchorY);
        }
        catch (Exception ex)
        {
            if (version == _requestVersion && IsVisible)
            {
                BodyText.Text = "翻译失败：" + ex.Message;
                PositionAt(_anchorX, _anchorY);
            }
        }
    }

    private void RememberResult(params (string Text, string Label, string Lcid)[] speech)
    {
        _lastResult = (SourceText.Text, BodyText.Text, speech);
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
        _requestVersion++;
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
            _requestVersion++;
            Hide();
        }
    }
}
