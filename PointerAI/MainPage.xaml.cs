using Microsoft.Maui.Controls.Shapes;
using PointerAI.Services;

namespace PointerAI;

public partial class MainPage : ContentPage
{
    private const int CharacterWindowWidth = 150;
    private const int CharacterWindowHeight = 150;
    private const int ChatWindowWidth = 1000;
    private const int ChatWindowHeight = 640;
    private const string ToggleHotkeyPreference = "ToggleChatHotkey";
    private const string ResumeHotkeyPreference = "ResumeChatHotkey";
    private const string SelectedCharacterPreference = "SelectedCharacter";
    private const string ThemePreference = "Theme";
    private readonly GeminiScreenAssistant screenAssistant;
    private byte[]? latestCapturePng;
    private bool isChatOpen;
    private bool isSending;
    private bool isTransitioning;
    private string pendingCharacterFile = "character.png";
    private bool isLightTheme;
    private bool pendingLightTheme;
    private CancellationTokenSource? idleAnimationCancellation;

    public static MainPage? Current { get; private set; }

    public MainPage(GeminiScreenAssistant screenAssistant)
    {
        InitializeComponent();
        this.screenAssistant = screenAssistant;
        Current = this;
        LoadHotkeySettings();
        LoadThemeSetting();
        LoadCharacterSetting();
    }

    public async void ToggleChat()
    {
        if (isTransitioning) return;
        var shouldOpen = !isChatOpen;
        if (shouldOpen) CaptureScreenForChatOpen();
        await SetChatOpenAsync(shouldOpen, showCapturePreview: shouldOpen);
    }

    public async void OpenChatWithoutCapture()
    {
        if (isTransitioning || isChatOpen) return;
        await SetChatOpenAsync(true, showCapturePreview: false);
    }

    private void LoadHotkeySettings()
    {
        var toggleHotkey = Preferences.Default.Get(ToggleHotkeyPreference, "Alt+Z");
        var resumeHotkey = Preferences.Default.Get(ResumeHotkeyPreference, "Alt+Shift+Z");
        ToggleHotkeyEntry.Text = toggleHotkey;
        ResumeHotkeyEntry.Text = resumeHotkey;
        HotkeyBadge.Text = toggleHotkey;
    }

    private void LoadCharacterSetting()
    {
        var fileName = Preferences.Default.Get(SelectedCharacterPreference, "character.png");
        pendingCharacterFile = fileName == "character2.png" ? "character2.png" : "character.png";
        UpdateCharacterSelectionUi();
        ApplyCharacter(fileName);
    }

    private void LoadThemeSetting()
    {
        isLightTheme = Preferences.Default.Get(ThemePreference, "Dark") == "Light";
        pendingLightTheme = isLightTheme;
        ApplyTheme(isLightTheme);
        UpdateThemeSelectionUi();
    }

    private void ApplyTheme(bool light)
    {
        isLightTheme = light;
        var map = light
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["#1F1F1D"] = "#F7F6F2", ["#1A1C22"] = "#F7F6F2",
                ["#2B2B29"] = "#ECEAE4", ["#292927"] = "#FFFFFF",
                ["#1F2128"] = "#FFFFFF", ["#272A33"] = "#ECEAE4",
                ["#3A3935"] = "#D4D0C7", ["#494842"] = "#D4D0C7",
                ["#3F4350"] = "#D4D0C7", ["#F5F4ED"] = "#252522",
                ["#F4F4F5"] = "#252522", ["#F0EFE8"] = "#252522",
                ["#E4E4E7"] = "#3A3935", ["#D8D6CE"] = "#3A3935",
                ["#D4D4D8"] = "#3A3935", ["#C7C5BC"] = "#706E67",
                ["#A8A69D"] = "#706E67", ["#9CA3AF"] = "#706E67",
                ["#D97757"] = "#C96442"
            }
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["#F7F6F2"] = "#1F1F1D", ["#ECEAE4"] = "#2B2B29",
                ["#FFFFFF"] = "#292927", ["#D4D0C7"] = "#494842",
                ["#252522"] = "#F5F4ED", ["#3A3935"] = "#D8D6CE",
                ["#706E67"] = "#A8A69D", ["#C96442"] = "#D97757"
            };
        ApplyThemeToElement(this, map);
        UpdateThemeSelectionUi();
        UpdateCharacterSelectionUi();
    }

    private static void ApplyThemeToElement(IVisualTreeElement element, Dictionary<string, string> map)
    {
        if (element is VisualElement visual)
        {
            if (visual.BackgroundColor is Color background && map.TryGetValue(background.ToHex(), out var bg))
                visual.BackgroundColor = Color.FromArgb(bg);
        }
        if (element is Label label && map.TryGetValue(label.TextColor.ToHex(), out var labelColor))
            label.TextColor = Color.FromArgb(labelColor);
        if (element is Entry entry)
        {
            if (map.TryGetValue(entry.TextColor.ToHex(), out var entryColor)) entry.TextColor = Color.FromArgb(entryColor);
            if (map.TryGetValue(entry.PlaceholderColor.ToHex(), out var placeholder)) entry.PlaceholderColor = Color.FromArgb(placeholder);
        }
        if (element is Button button && map.TryGetValue(button.TextColor.ToHex(), out var buttonColor))
            button.TextColor = Color.FromArgb(buttonColor);
        if (element is Border border && border.Stroke is SolidColorBrush stroke &&
            map.TryGetValue(stroke.Color.ToHex(), out var strokeColor))
            border.Stroke = new SolidColorBrush(Color.FromArgb(strokeColor));
        foreach (var child in element.GetVisualChildren()) ApplyThemeToElement(child, map);
    }

    private async void OnDarkThemeSelected(object sender, TappedEventArgs e)
    {
        pendingLightTheme = false;
        UpdateThemeSelectionUi();
        await DarkThemeOption.ScaleTo(0.97, 80, Easing.CubicOut);
        await DarkThemeOption.ScaleTo(1, 100, Easing.CubicOut);
    }

    private async void OnLightThemeSelected(object sender, TappedEventArgs e)
    {
        pendingLightTheme = true;
        UpdateThemeSelectionUi();
        await LightThemeOption.ScaleTo(0.97, 80, Easing.CubicOut);
        await LightThemeOption.ScaleTo(1, 100, Easing.CubicOut);
    }

    private void UpdateThemeSelectionUi()
    {
        var selected = Color.FromArgb(isLightTheme ? "#DED9CF" : "#3A3834");
        var normal = Color.FromArgb(isLightTheme ? "#FFFFFF" : "#292927");
        var accent = new SolidColorBrush(Color.FromArgb(isLightTheme ? "#C96442" : "#D97757"));
        var border = new SolidColorBrush(Color.FromArgb(isLightTheme ? "#D4D0C7" : "#494842"));
        DarkThemeOption.BackgroundColor = pendingLightTheme ? normal : selected;
        DarkThemeOption.Stroke = pendingLightTheme ? border : accent;
        DarkThemeOption.StrokeThickness = pendingLightTheme ? 1 : 2;
        LightThemeOption.BackgroundColor = pendingLightTheme ? selected : normal;
        LightThemeOption.Stroke = pendingLightTheme ? accent : border;
        LightThemeOption.StrokeThickness = pendingLightTheme ? 2 : 1;
    }

    private void ApplyCharacter(string fileName)
    {
        var useSecondCharacter = fileName == "character2.png";
        Character1Sprite.IsVisible = !useSecondCharacter;
        Character2Sprite.IsVisible = useSecondCharacter;
        HeaderCharacter1Image.IsVisible = !useSecondCharacter;
        HeaderCharacter2Image.IsVisible = useSecondCharacter;
#if WINDOWS
        Platforms.Windows.OverlayWindow.SetCharacter(fileName);
#endif
    }

    private async void OnCharacter1Selected(object sender, TappedEventArgs e)
    {
        pendingCharacterFile = "character.png";
        UpdateCharacterSelectionUi();
        await Character1Option.ScaleTo(0.97, 80, Easing.CubicOut);
        await Character1Option.ScaleTo(1, 100, Easing.CubicOut);
    }

    private async void OnCharacter2Selected(object sender, TappedEventArgs e)
    {
        pendingCharacterFile = "character2.png";
        UpdateCharacterSelectionUi();
        await Character2Option.ScaleTo(0.97, 80, Easing.CubicOut);
        await Character2Option.ScaleTo(1, 100, Easing.CubicOut);
    }

    private void UpdateCharacterSelectionUi()
    {
        var secondSelected = pendingCharacterFile == "character2.png";
        var normalSurface = isLightTheme ? "#FFFFFF" : "#1F2128";
        var selectedSurface = isLightTheme ? "#E7E3DA" : "#272A33";
        var normalBorder = isLightTheme ? "#D4D0C7" : "#3F4350";
        var selectedBorder = isLightTheme ? "#C96442" : "#D97757";

        Character1Option.BackgroundColor = Color.FromArgb(secondSelected ? normalSurface : selectedSurface);
        Character1Option.Stroke = new SolidColorBrush(Color.FromArgb(secondSelected ? normalBorder : selectedBorder));
        Character1Option.StrokeThickness = secondSelected ? 1 : 2;
        Character2Option.BackgroundColor = Color.FromArgb(secondSelected ? selectedSurface : normalSurface);
        Character2Option.Stroke = new SolidColorBrush(Color.FromArgb(secondSelected ? selectedBorder : normalBorder));
        Character2Option.StrokeThickness = secondSelected ? 2 : 1;
    }

    private async void OnSettingsClicked(object sender, EventArgs e)
    {
        LoadHotkeySettings();
        pendingLightTheme = isLightTheme;
        UpdateThemeSelectionUi();
        LoadCharacterSetting();
        SettingsStatusLabel.IsVisible = false;
        SettingsPanel.Opacity = 0;
        SettingsPanel.Scale = 0.96;
        SettingsPanel.IsVisible = true;
        await Task.WhenAll(
            SettingsPanel.FadeTo(1, 180, Easing.CubicOut),
            SettingsPanel.ScaleTo(1, 180, Easing.CubicOut));
        ToggleHotkeyEntry.Focus();
    }

    private async void OnSettingsCancelClicked(object sender, EventArgs e) => await CloseSettingsAsync();

    private async void OnSettingsSaveClicked(object sender, EventArgs e)
    {
#if WINDOWS
        if (!Platforms.Windows.OverlayWindow.TryUpdateHotkeys(
            ToggleHotkeyEntry.Text ?? string.Empty,
            ResumeHotkeyEntry.Text ?? string.Empty,
            out var error))
        {
            SettingsStatusLabel.Text = error;
            SettingsStatusLabel.IsVisible = true;
            return;
        }
#endif
        var characterFile = pendingCharacterFile;
        Preferences.Default.Set(SelectedCharacterPreference, characterFile);
        Preferences.Default.Set(ThemePreference, pendingLightTheme ? "Light" : "Dark");
        ApplyTheme(pendingLightTheme);
        ApplyCharacter(characterFile);
        LoadHotkeySettings();
        await CloseSettingsAsync();
    }

    private async Task CloseSettingsAsync()
    {
        await Task.WhenAll(
            SettingsPanel.FadeTo(0, 150, Easing.CubicIn),
            SettingsPanel.ScaleTo(0.96, 150, Easing.CubicIn));
        SettingsPanel.IsVisible = false;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        idleAnimationCancellation?.Cancel();
        idleAnimationCancellation = new CancellationTokenSource();
        _ = RunCharacterIdleAnimationAsync(idleAnimationCancellation.Token);
    }

    protected override void OnDisappearing()
    {
        idleAnimationCancellation?.Cancel();
#if WINDOWS
        Platforms.Windows.OverlayWindow.SetCharacterBobOffset(0);
#endif
        base.OnDisappearing();
    }

    private void CaptureScreenForChatOpen()
    {
#if WINDOWS
        try { latestCapturePng = Platforms.Windows.ScreenCapture.CaptureDesktopPng(); }
        catch (Exception ex)
        {
            latestCapturePng = null;
            AddMessage($"I could not capture the screen yet: {ex.Message}", false);
        }
#endif
    }

    private async Task SetChatOpenAsync(bool isOpen, bool showCapturePreview)
    {
        isTransitioning = true;
        try
        {
            if (isOpen)
            {
                isChatOpen = true;
#if WINDOWS
                Platforms.Windows.OverlayWindow.SetCharacterBobOffset(0);
                Platforms.Windows.OverlayWindow.SetChatVisible(true);
#endif
                UpdateMauiWindowSize(true);
                CharacterOnlyView.IsVisible = false;
                ChatPanel.Opacity = 0;
                ChatPanel.Scale = 0.95;
                ChatPanel.IsVisible = true;
                if (showCapturePreview) ShowCapturePreview();
                await Task.WhenAll(ChatPanel.FadeTo(1, 220, Easing.CubicOut), ChatPanel.ScaleTo(1, 220, Easing.CubicOut));
                MessageEntry.Focus();
            }
            else
            {
                await Task.WhenAll(ChatPanel.FadeTo(0, 180, Easing.CubicIn), ChatPanel.ScaleTo(0.95, 180, Easing.CubicIn));
                ChatPanel.IsVisible = false;
                isChatOpen = false;
                CharacterOnlyView.IsVisible = true;
                UpdateMauiWindowSize(false);
#if WINDOWS
                Platforms.Windows.OverlayWindow.SetChatVisible(false);
#endif
            }
        }
        finally { isTransitioning = false; }
    }

    private async Task RunCharacterIdleAnimationAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (isChatOpen) { await Task.Delay(100, token); continue; }
#if WINDOWS
                for (var frame = 0; frame < 72 && !token.IsCancellationRequested && !isChatOpen; frame++)
                {
                    Platforms.Windows.OverlayWindow.SetCharacterBobOffset((int)Math.Round(-4 * Math.Sin(frame * Math.PI / 36)));
                    await Task.Delay(28, token);
                }
#else
                await CharacterOnlyView.TranslateTo(0, -4, 900, Easing.SinInOut);
                await CharacterOnlyView.TranslateTo(0, 0, 900, Easing.SinInOut);
#endif
            }
        }
        catch (OperationCanceledException) { }
    }

    private void ShowCapturePreview()
    {
        if (latestCapturePng is not null) AddCaptureMessage(latestCapturePng);
    }

    private void UpdateMauiWindowSize(bool chatOpen)
    {
        if (Window is null) return;
        var width = chatOpen ? ChatWindowWidth : CharacterWindowWidth;
        var height = chatOpen ? ChatWindowHeight : CharacterWindowHeight;
        Window.MinimumWidth = Window.MaximumWidth = Window.Width = width;
        Window.MinimumHeight = Window.MaximumHeight = Window.Height = height;
    }

    private async void OnSendClicked(object sender, EventArgs e) => await SendCurrentMessageAsync();
    private async void OnMessageEntryCompleted(object sender, EventArgs e) => await SendCurrentMessageAsync();

    private async Task SendCurrentMessageAsync()
    {
        if (isSending) return;
        var text = MessageEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;
        AddMessage(text, true);
        MessageEntry.Text = string.Empty;
        if (latestCapturePng is null)
        {
            AddMessage("I don't have a screen capture to inspect. Close and reopen the chat to capture the screen again.", false);
            return;
        }

        isSending = true;
        SetComposerEnabled(false);
        var thinkingBubble = AddMessage("Thinking", false);
        var thinkingLabel = ((Border)thinkingBubble).Content as Label
            ?? throw new InvalidOperationException("Thinking bubble content is missing.");
        using var cancellation = new CancellationTokenSource();
        var animation = AnimateThinkingAsync(thinkingLabel, cancellation.Token);
        string answer;
        try { answer = (await screenAssistant.AskAsync(text, latestCapturePng)).Answer; }
        catch (Exception ex) { answer = $"I couldn't analyze the screen: {ex.Message}"; }
        finally
        {
            cancellation.Cancel();
            await animation;
            MessagesStack.Remove(thinkingBubble);
            isSending = false;
            SetComposerEnabled(true);
            MessageEntry.Focus();
        }
        AddMessage(answer, false);
    }

    private void SetComposerEnabled(bool enabled)
    {
        MessageEntry.IsEnabled = enabled;
        SendButton.IsEnabled = enabled;
        SendButton.Text = "↑";
    }

    private static async Task AnimateThinkingAsync(Label label, CancellationToken token)
    {
        var dots = 0;
        while (!token.IsCancellationRequested)
        {
            dots = dots % 3 + 1;
            label.Text = $"Thinking{new string((char)46, dots)}";
            await label.FadeTo(0.5, 280, Easing.SinInOut);
            await label.FadeTo(1, 280, Easing.SinInOut);
        }
    }

    private void AddCaptureMessage(byte[] capturePng)
    {
        var image = new Image { Source = ImageSource.FromStream(() => new MemoryStream(capturePng)), Aspect = Aspect.AspectFit, HeightRequest = 260, HorizontalOptions = LayoutOptions.Fill };
        var bubble = new Border
        {
            Padding = 16, StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            BackgroundColor = Color.FromArgb(isLightTheme ? "#FFFFFF" : "#292927"), Stroke = new SolidColorBrush(Color.FromArgb(isLightTheme ? "#D4D0C7" : "#45443F")), HorizontalOptions = LayoutOptions.Start, MaximumWidthRequest = 820,
            Content = new VerticalStackLayout
            {
                Spacing = 10,
                Children = { new Label { Text = "Screen captured for this chat", FontFamily = "PoppinsSemiBold", FontSize = 15, TextColor = Color.FromArgb(isLightTheme ? "#3A3935" : "#D8D6CE") }, image }
            }
        };
        MessagesStack.Add(bubble);
        AnimateBubbleEntrance(bubble);
        ScrollToLatestMessage();
    }

    private View AddMessage(string text, bool isUser)
    {
        var bubble = new Border
        {
            Padding = isUser ? new Thickness(18, 14) : new Thickness(0, 8), StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Background = isUser ? new SolidColorBrush(Color.FromArgb(isLightTheme ? "#E7E4DC" : "#2B2B29")) : new SolidColorBrush(Colors.Transparent),
            HorizontalOptions = isUser ? LayoutOptions.End : LayoutOptions.Start, MaximumWidthRequest = isUser ? 650 : 820,
            Content = new Label { Text = text, FontFamily = "PoppinsRegular", FontSize = 17, TextColor = Color.FromArgb(isLightTheme ? "#252522" : (isUser ? "#F5F4ED" : "#F0EFE8")), LineBreakMode = LineBreakMode.WordWrap }
        };
        MessagesStack.Add(bubble);
        AnimateBubbleEntrance(bubble);
        ScrollToLatestMessage();
        return bubble;
    }

    private void AnimateBubbleEntrance(View bubble)
    {
        bubble.Opacity = 0;
        bubble.TranslationY = 20;
        Dispatcher.Dispatch(async () => await Task.WhenAll(
            bubble.FadeTo(1, 230, Easing.CubicOut),
            bubble.TranslateTo(0, 0, 230, Easing.CubicOut)));
    }

    private void ScrollToLatestMessage()
    {
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(50), async () =>
            await MessagesScrollView.ScrollToAsync(0, MessagesStack.Height, animated: true));
    }
}
