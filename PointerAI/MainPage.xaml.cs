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
    private readonly GeminiScreenAssistant screenAssistant;
    private byte[]? latestCapturePng;
    private bool isChatOpen;
    private bool isSending;
    private bool isTransitioning;
    private string pendingCharacterFile = "character.png";
    private CancellationTokenSource? idleAnimationCancellation;

    public static MainPage? Current { get; private set; }

    public MainPage(GeminiScreenAssistant screenAssistant)
    {
        InitializeComponent();
        this.screenAssistant = screenAssistant;
        Current = this;
        LoadHotkeySettings();
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
        Character1Option.BackgroundColor = Color.FromArgb(secondSelected ? "#1F2128" : "#272A33");
        Character1Option.Stroke = new SolidColorBrush(Color.FromArgb(secondSelected ? "#3F4350" : "#7C3AED"));
        Character1Option.StrokeThickness = secondSelected ? 1 : 2;
        Character2Option.BackgroundColor = Color.FromArgb(secondSelected ? "#272A33" : "#1F2128");
        Character2Option.Stroke = new SolidColorBrush(Color.FromArgb(secondSelected ? "#7C3AED" : "#3F4350"));
        Character2Option.StrokeThickness = secondSelected ? 2 : 1;
    }

    private async void OnSettingsClicked(object sender, EventArgs e)
    {
        LoadHotkeySettings();
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
            BackgroundColor = Color.FromArgb("#292927"), Stroke = new SolidColorBrush(Color.FromArgb("#45443F")), HorizontalOptions = LayoutOptions.Start, MaximumWidthRequest = 820,
            Content = new VerticalStackLayout
            {
                Spacing = 10,
                Children = { new Label { Text = "Screen captured for this chat", FontFamily = "PoppinsSemiBold", FontSize = 15, TextColor = Color.FromArgb("#D8D6CE") }, image }
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
            Background = isUser ? new SolidColorBrush(Color.FromArgb("#2B2B29")) : new SolidColorBrush(Colors.Transparent),
            HorizontalOptions = isUser ? LayoutOptions.End : LayoutOptions.Start, MaximumWidthRequest = isUser ? 650 : 820,
            Content = new Label { Text = text, FontFamily = "PoppinsRegular", FontSize = 17, TextColor = Color.FromArgb(isUser ? "#F5F4ED" : "#F0EFE8"), LineBreakMode = LineBreakMode.WordWrap }
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
