using Microsoft.Maui.Controls.Shapes;
using PointerAI.Services;

namespace PointerAI;

public partial class MainPage : ContentPage
{
	private const int CharacterWindowWidth = 150;
	private const int CharacterWindowHeight = 150;
	private const int ChatWindowWidth = 1000;
	private const int ChatWindowHeight = 640;
	private bool isChatOpen;
	private byte[]? latestCapturePng;
	private readonly GeminiScreenAssistant screenAssistant;
	private bool isSending;

	public static MainPage? Current { get; private set; }

	public MainPage(GeminiScreenAssistant screenAssistant)
	{
		InitializeComponent();
		this.screenAssistant = screenAssistant;
		Current = this;
	}

	public void ToggleChat()
	{
		if (!isChatOpen)
		{
			CaptureScreenForChatOpen();
		}

		SetChatOpen(!isChatOpen);
	}

	private void CaptureScreenForChatOpen()
	{
#if WINDOWS
		try
		{
			latestCapturePng = Platforms.Windows.ScreenCapture.CaptureDesktopPng();
		}
		catch (Exception ex)
		{
			latestCapturePng = null;
			AddMessage($"I could not capture the screen yet: {ex.Message}", isUser: false);
		}
#endif
	}

	private void SetChatOpen(bool isOpen)
	{
		isChatOpen = isOpen;
		CharacterOnlyView.IsVisible = !isChatOpen;
		ChatPanel.IsVisible = isChatOpen;
		UpdateMauiWindowSize(isChatOpen);

#if WINDOWS
		Platforms.Windows.OverlayWindow.SetChatVisible(isChatOpen);
#endif

		if (isChatOpen)
		{
			ShowCapturePreview();
			MessageEntry.Focus();
		}
	}

	private void ShowCapturePreview()
	{
		if (latestCapturePng is null)
		{
			return;
		}

		AddCaptureMessage(latestCapturePng);
	}

	private void UpdateMauiWindowSize(bool chatOpen)
	{
		if (Window is null)
		{
			return;
		}

		var width = chatOpen ? ChatWindowWidth : CharacterWindowWidth;
		var height = chatOpen ? ChatWindowHeight : CharacterWindowHeight;

		Window.MinimumWidth = width;
		Window.MinimumHeight = height;
		Window.MaximumWidth = width;
		Window.MaximumHeight = height;
		Window.Width = width;
		Window.Height = height;
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
		var thinkingBubble = AddMessage("Looking at your screen…", false);
		try
		{
			var result = await screenAssistant.AskAsync(text, latestCapturePng);
			MessagesStack.Remove(thinkingBubble);
			AddMessage(result.Answer, false);
		}
		catch (Exception ex)
		{
			MessagesStack.Remove(thinkingBubble);
			AddMessage($"I couldn't analyze the screen: {ex.Message}", false);
		}
		finally
		{
			isSending = false;
			SetComposerEnabled(true);
			MessageEntry.Focus();
		}
	}

	private void SetComposerEnabled(bool enabled)
	{
		MessageEntry.IsEnabled = enabled;
		SendButton.IsEnabled = enabled;
		SendButton.Text = enabled ? "Send" : "Thinking…";
	}

	private void AddCaptureMessage(byte[] capturePng)
	{
		var image = new Image
		{
			Source = ImageSource.FromStream(() => new MemoryStream(capturePng)),
			Aspect = Aspect.AspectFit,
			HeightRequest = 260,
			HorizontalOptions = LayoutOptions.Fill
		};

		var bubble = new Border
		{
			Padding = new Thickness(14),
			StrokeThickness = 0,
			StrokeShape = new RoundRectangle { CornerRadius = 10 },
			BackgroundColor = Color.FromArgb("#E2E8F0"),
			HorizontalOptions = LayoutOptions.Start,
			MaximumWidthRequest = 760,
			Content = new VerticalStackLayout
			{
				Spacing = 10,
				Children =
				{
					new Label
					{
						Text = "Screen captured for this chat.",
						FontSize = 18,
						TextColor = Color.FromArgb("#334155")
					},
					image
				}
			}
		};

		MessagesStack.Add(bubble);
		ScrollToLatestMessage();
	}

	private View AddMessage(string text, bool isUser)
	{
		var bubble = new Border
		{
			Padding = new Thickness(16, 12),
			StrokeThickness = 0,
			StrokeShape = new RoundRectangle { CornerRadius = 10 },
			BackgroundColor = Color.FromArgb(isUser ? "#0F766E" : "#E2E8F0"),
			HorizontalOptions = isUser ? LayoutOptions.End : LayoutOptions.Start,
			MaximumWidthRequest = 760,
			Content = new Label
			{
				Text = text,
				FontSize = 18,
				TextColor = Color.FromArgb(isUser ? "#FFFFFF" : "#334155"),
				LineBreakMode = LineBreakMode.WordWrap
			}
		};

		MessagesStack.Add(bubble);
		ScrollToLatestMessage();

		return bubble;
	}

	private void ScrollToLatestMessage()
	{
		Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(50), async () =>
		{
			await MessagesScrollView.ScrollToAsync(0, MessagesStack.Height, animated: true);
		});
	}
}