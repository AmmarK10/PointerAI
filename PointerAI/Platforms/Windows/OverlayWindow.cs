#if WINDOWS
using System.Runtime.InteropServices;
using Microsoft.Maui.ApplicationModel;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using WinRT.Interop;

namespace PointerAI.Platforms.Windows;

internal static class OverlayWindow
{
	private const int CharacterWindowWidth = 150;
	private const int CharacterWindowHeight = 150;
	private const int ChatWindowWidth = 1000;
	private const int ChatWindowHeight = 640;
	private const int ScreenMargin = 16;
	private const int GwlExStyle = -20;
	private const int WsExToolWindow = 0x00000080;
	private const int WsExLayered = 0x00080000;
	private const int RgnOr = 2;
	private const int DwmwaNcRenderingPolicy = 2;
	private const int DwmwaBorderColor = 34;
	private const int DwmncrpDisabled = 1;
	private const int DwmncrpEnabled = 2;
	private const int DwmColorDefault = -1;
	private const int DwmColorNone = -2;
	private const int WsExTransparent = 0x00000020;
	private const int WsExNoActivate = 0x08000000;
	private const int WsPopup = unchecked((int)0x80000000);
	private const int SsBlackRect = 0x00000004;
	private const uint LwaAlpha = 0x00000002;
	private const uint SwpNoActivate = 0x0010;
	private const uint SwpShowWindow = 0x0040;
	private const int SwHide = 0;
	private const int ToggleChatHotkeyId = 9001;
	private const int ResumeChatHotkeyId = 9002;
	private const uint ModAlt = 0x0001;
	private const uint ModControl = 0x0002;
	private const uint ModShift = 0x0004;
	private const uint ModWin = 0x0008;
	private const uint VkZ = 0x5A;
	private const int WmHotkey = 0x0312;
	private const int WmQuit = 0x0012;

	private static AppWindow? appWindow;
	private static IntPtr windowHandle;
	private static int characterBaseX;
	private static int characterBaseY;
	private static bool hasCharacterBasePosition;
	private static FrameworkElement? windowContent;
	private static Thread? hotkeyThread;
	private static uint hotkeyThreadId;
	private static bool toggleChatHotkeyRegistered;
	private static bool resumeChatHotkeyRegistered;
	private static bool isChatVisible;
	private static bool isDraggingCharacter;
	private static Point dragPointerStart;
	private static PointInt32 dragWindowStart;
	private static string characterResourceName = "PointerAI.character.png";
	private static readonly ManualResetEventSlim hotkeyRegistrationCompleted = new(false);
	private static string? hotkeyRegistrationError;
	private static readonly List<IntPtr> shadowWindows = new();

	public static void Configure(Microsoft.UI.Xaml.Window window)
	{
		var selectedCharacter = Preferences.Default.Get("SelectedCharacter", "character.png");
		characterResourceName = selectedCharacter == "character2.png"
			? "PointerAI.character2.png"
			: "PointerAI.character.png";
		window.ExtendsContentIntoTitleBar = true;
		window.SystemBackdrop = null;
		window.Closed += (_, _) =>
		{
			StopHotkeyThread();
			DestroyShadowWindows();
		};

		if (window.Content is FrameworkElement content)
		{
			windowContent = content;
			SetNativeContentSize(CharacterWindowWidth, CharacterWindowHeight);
			content.Loaded += OnWindowContentLoaded;
			content.PointerPressed += OnCharacterPointerPressed;
			content.PointerMoved += OnCharacterPointerMoved;
			content.PointerReleased += OnCharacterPointerReleased;
			content.PointerCanceled += OnCharacterPointerReleased;
		}

		ClearBackground(window.Content);

		var hwnd = WindowNative.GetWindowHandle(window);
		windowHandle = hwnd;
		EnableOverlayWindowStyle(hwnd);
		StartHotkeyThread();

		var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
		appWindow = AppWindow.GetFromWindowId(windowId);

		if (appWindow.Presenter is OverlappedPresenter presenter)
		{
			presenter.SetBorderAndTitleBar(false, false);
			presenter.IsAlwaysOnTop = true;
			presenter.IsResizable = false;
			presenter.IsMaximizable = false;
			presenter.IsMinimizable = false;
		}

		appWindow.IsShownInSwitchers = false;
		ResizeAndMove(CharacterWindowWidth, CharacterWindowHeight, centerOnScreen: false);
	}

	private static void OnWindowContentLoaded(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement content)
		{
			content.Loaded -= OnWindowContentLoaded;
			content.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
			{
				ResizeAndMove(CharacterWindowWidth, CharacterWindowHeight, centerOnScreen: false);
				ApplyCharacterRegion();
			});
		}
	}

	public static void SetChatVisible(bool isVisible)
	{
		isChatVisible = isVisible;
		var width = isVisible ? ChatWindowWidth : CharacterWindowWidth;
		var height = isVisible ? ChatWindowHeight : CharacterWindowHeight;
		SetWindowRgn(windowHandle, IntPtr.Zero, true);
		ResizeAndMove(width, height, centerOnScreen: isVisible);
		if (isVisible)
		{
			ApplyChatRegion();
			SetChatShadow(true);
		}
		else
		{
			SetChatShadow(false);
			ApplyCharacterRegion();
		}
	}

	public static void SetCharacter(string fileName)
	{
		characterResourceName = fileName == "character2.png"
			? "PointerAI.character2.png"
			: "PointerAI.character.png";
		if (!isChatVisible && windowHandle != IntPtr.Zero) ApplyCharacterRegion();
	}

	private static void SetChatShadow(bool enabled)
	{
		var policy = DwmncrpDisabled;
		DwmSetWindowAttribute(windowHandle, DwmwaNcRenderingPolicy, ref policy, sizeof(int));
		var borderColor = DwmColorNone;
		DwmSetWindowAttribute(windowHandle, DwmwaBorderColor, ref borderColor, sizeof(int));
		var margins = new DwmMargins();
		DwmExtendFrameIntoClientArea(windowHandle, ref margins);

		if (!enabled)
		{
			foreach (var shadow in shadowWindows) ShowWindow(shadow, SwHide);
			return;
		}

		EnsureShadowWindows();
		if (appWindow is null) return;
		var spreads = new[] { 5, 10, 16 };
		var alphas = new byte[] { 58, 34, 18 };
		for (var index = shadowWindows.Count - 1; index >= 0; index--)
		{
			var spread = LogicalToPixels(spreads[index]);
			var verticalOffset = LogicalToPixels(5);
			var x = appWindow.Position.X - spread;
			var y = appWindow.Position.Y - spread + verticalOffset;
			var width = appWindow.Size.Width + spread * 2;
			var height = appWindow.Size.Height + spread * 2;
			var cornerDiameter = LogicalToPixels(40) + spread * 2;
			var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, cornerDiameter, cornerDiameter);
			SetWindowRgn(shadowWindows[index], region, false);
			SetLayeredWindowAttributes(shadowWindows[index], 0, alphas[index], LwaAlpha);
			SetWindowPos(shadowWindows[index], windowHandle, x, y, width, height, SwpNoActivate | SwpShowWindow);
		}
	}

	private static void EnsureShadowWindows()
	{
		while (shadowWindows.Count < 3)
		{
			var shadow = CreateWindowEx(
				WsExLayered | WsExToolWindow | WsExTransparent | WsExNoActivate,
				"STATIC", null, WsPopup | SsBlackRect,
				0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
			if (shadow == IntPtr.Zero) break;
			shadowWindows.Add(shadow);
		}
	}

	private static void DestroyShadowWindows()
	{
		foreach (var shadow in shadowWindows) DestroyWindow(shadow);
		shadowWindows.Clear();
	}

	private static void ApplyChatRegion()
	{
		var width = LogicalToPixels(ChatWindowWidth);
		var height = LogicalToPixels(ChatWindowHeight);
		var cornerDiameter = LogicalToPixels(40);
		var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, cornerDiameter, cornerDiameter);
		SetWindowRgn(windowHandle, region, true);
	}

	public static void SetCharacterBobOffset(int logicalOffset)
	{
		if (appWindow is null || !hasCharacterBasePosition || isDraggingCharacter) return;
		var scale = windowContent?.XamlRoot?.RasterizationScale ?? 1.0;
		var pixelOffset = (int)Math.Round(logicalOffset * scale);
		appWindow.Move(new PointInt32(characterBaseX, characterBaseY + pixelOffset));
	}

	private static void OnCharacterPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
	{
		if (isChatVisible || appWindow is null || sender is not FrameworkElement content) return;
		var point = e.GetCurrentPoint(content);
		if (!point.Properties.IsLeftButtonPressed) return;
		if (!GetCursorPos(out dragPointerStart)) return;

		isDraggingCharacter = true;
		dragWindowStart = appWindow.Position;
		content.CapturePointer(e.Pointer);
		e.Handled = true;
	}

	private static void OnCharacterPointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
	{
		if (!isDraggingCharacter || isChatVisible || appWindow is null) return;
		if (!GetCursorPos(out var currentPointer)) return;

		var x = dragWindowStart.X + currentPointer.x - dragPointerStart.x;
		var y = dragWindowStart.Y + currentPointer.y - dragPointerStart.y;
		var displayArea = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
		var workArea = displayArea.WorkArea;
		x = Math.Clamp(x, workArea.X, workArea.X + workArea.Width - appWindow.Size.Width);
		y = Math.Clamp(y, workArea.Y, workArea.Y + workArea.Height - appWindow.Size.Height);
		characterBaseX = x;
		characterBaseY = y;
		hasCharacterBasePosition = true;
		appWindow.Move(new PointInt32(x, y));
		e.Handled = true;
	}

	private static void OnCharacterPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
	{
		if (!isDraggingCharacter) return;
		isDraggingCharacter = false;
		if (sender is FrameworkElement content) content.ReleasePointerCapture(e.Pointer);
		e.Handled = true;
	}

	public static bool TryUpdateHotkeys(string toggleText, string resumeText, out string error)
	{
		if (!TryParseHotkey(toggleText, out var toggleModifiers, out var toggleKey, out var toggleNormalized, out error)) return false;
		if (!TryParseHotkey(resumeText, out var resumeModifiers, out var resumeKey, out var resumeNormalized, out error)) return false;
		if (toggleModifiers == resumeModifiers && toggleKey == resumeKey)
		{
			error = "The two shortcuts must be different.";
			return false;
		}

		var oldToggle = Preferences.Default.Get("ToggleChatHotkey", "Alt+Z");
		var oldResume = Preferences.Default.Get("ResumeChatHotkey", "Alt+Shift+Z");
		Preferences.Default.Set("ToggleChatHotkey", toggleNormalized);
		Preferences.Default.Set("ResumeChatHotkey", resumeNormalized);
		StopHotkeyThread();
		StartHotkeyThread();
		if (string.IsNullOrEmpty(hotkeyRegistrationError))
		{
			error = string.Empty;
			return true;
		}

		error = hotkeyRegistrationError;
		Preferences.Default.Set("ToggleChatHotkey", oldToggle);
		Preferences.Default.Set("ResumeChatHotkey", oldResume);
		StopHotkeyThread();
		StartHotkeyThread();
		return false;
	}

	private static bool TryParseHotkey(string text, out uint modifiers, out uint key, out string normalized, out string error)
	{
		modifiers = 0;
		key = 0;
		normalized = string.Empty;
		error = string.Empty;
		var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (parts.Length < 2)
		{
			error = "Use at least one modifier and one key, for example Alt+Z.";
			return false;
		}

		var names = new List<string>();
		for (var index = 0; index < parts.Length - 1; index++)
		{
			switch (parts[index].ToUpperInvariant())
			{
				case "CTRL": case "CONTROL": modifiers |= ModControl; break;
				case "ALT": modifiers |= ModAlt; break;
				case "SHIFT": modifiers |= ModShift; break;
				case "WIN": case "WINDOWS": modifiers |= ModWin; break;
				default: error = $"Unknown modifier: {parts[index]}."; return false;
			}
		}

		var keyName = parts[^1].ToUpperInvariant();
		if (keyName.Length == 1 && ((keyName[0] >= 'A' && keyName[0] <= 'Z') || (keyName[0] >= '0' && keyName[0] <= '9')))
			key = keyName[0];
		else if (keyName.StartsWith("F") && int.TryParse(keyName[1..], out var functionKey) && functionKey is >= 1 and <= 12)
			key = (uint)(0x70 + functionKey - 1);
		else
		{
			error = "The key must be A-Z, 0-9, or F1-F12.";
			return false;
		}

		if ((modifiers & ModControl) != 0) names.Add("Ctrl");
		if ((modifiers & ModAlt) != 0) names.Add("Alt");
		if ((modifiers & ModShift) != 0) names.Add("Shift");
		if ((modifiers & ModWin) != 0) names.Add("Win");
		names.Add(keyName);
		normalized = string.Join("+", names);
		return true;
	}

	private static void ClearBackground(UIElement? element)
	{
		var transparent = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);

		switch (element)
		{
			case Panel panel:
				panel.Background = transparent;
				break;
			case Control control:
				control.Background = transparent;
				break;
			case Microsoft.UI.Xaml.Controls.Border border:
				border.Background = transparent;
				break;
		}
	}

	private static void ResizeAndMove(int width, int height, bool centerOnScreen)
	{
		SetNativeContentSize(width, height);

		if (appWindow is null)
		{
			return;
		}

		var pixelWidth = LogicalToPixels(width);
		var pixelHeight = LogicalToPixels(height);
		var pixelMargin = LogicalToPixels(ScreenMargin);
		var displayArea = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
		var workArea = displayArea.WorkArea;
		var preserveCharacterPosition = !centerOnScreen &&
			width == CharacterWindowWidth && height == CharacterWindowHeight &&
			hasCharacterBasePosition;
		var x = centerOnScreen
			? workArea.X + (workArea.Width - pixelWidth) / 2
			: preserveCharacterPosition ? characterBaseX : workArea.X + workArea.Width - pixelWidth - pixelMargin;
		var y = centerOnScreen
			? workArea.Y + (workArea.Height - pixelHeight) / 2
			: preserveCharacterPosition ? characterBaseY : workArea.Y + workArea.Height - pixelHeight - pixelMargin;

		appWindow.Resize(new SizeInt32(pixelWidth, pixelHeight));
		appWindow.Move(new PointInt32(x, y));
		if (!centerOnScreen && width == CharacterWindowWidth && height == CharacterWindowHeight)
		{
			characterBaseX = x;
			characterBaseY = y;
			hasCharacterBasePosition = true;
		}
	}

	private static int LogicalToPixels(int value)
	{
		var scale = windowContent?.XamlRoot?.RasterizationScale ?? 1.0;
		return Math.Max(1, (int)Math.Round(value * scale));
	}

	private static void SetNativeContentSize(int width, int height)
	{
		if (windowContent is null)
		{
			return;
		}

		windowContent.Width = width;
		windowContent.Height = height;
		windowContent.MinWidth = width;
		windowContent.MinHeight = height;
		windowContent.MaxWidth = width;
		windowContent.MaxHeight = height;
	}

	private static void EnableOverlayWindowStyle(IntPtr windowHandle)
	{
		var exStyle = GetWindowLongPtr(windowHandle, GwlExStyle).ToInt64();
		var overlayStyle = new IntPtr(exStyle | WsExToolWindow | WsExLayered);
		SetWindowLongPtr(windowHandle, GwlExStyle, overlayStyle);
	}

	private static void ApplyCharacterRegion()
	{
		using var stream = typeof(OverlayWindow).Assembly.GetManifestResourceStream(characterResourceName);
		if (stream is null) return;
		using var bitmap = new System.Drawing.Bitmap(stream);
		var windowWidth = LogicalToPixels(CharacterWindowWidth);
		var windowHeight = LogicalToPixels(CharacterWindowHeight);
		var scale = Math.Min((double)windowWidth / bitmap.Width, (double)windowHeight / bitmap.Height);
		var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
		var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
		var offsetX = (windowWidth - width) / 2;
		var offsetY = (windowHeight - height) / 2;
		var region = CreateRectRgn(0, 0, 0, 0);

		for (var y = 0; y < height; y++)
		{
			var sourceY = Math.Min(bitmap.Height - 1, (int)(y / scale));
			var runStart = -1;
			for (var x = 0; x <= width; x++)
			{
				var visible = x < width && bitmap.GetPixel(Math.Min(bitmap.Width - 1, (int)(x / scale)), sourceY).A > 16;
				if (visible && runStart < 0) runStart = x;
				if (!visible && runStart >= 0)
				{
					var run = CreateRectRgn(offsetX + runStart, offsetY + y, offsetX + x, offsetY + y + 1);
					CombineRgn(region, region, run, RgnOr);
					DeleteObject(run);
					runStart = -1;
				}
			}
		}

		SetWindowRgn(windowHandle, region, true);
	}

	private static void StartHotkeyThread()
	{
		if (hotkeyThread is not null)
		{
			return;
		}

		hotkeyRegistrationError = null;
		hotkeyRegistrationCompleted.Reset();
		hotkeyThread = new Thread(RunHotkeyMessageLoop)
		{
			IsBackground = true,
			Name = "Pointer AI Alt+Z Hotkey"
		};
		hotkeyThread.SetApartmentState(ApartmentState.STA);
		hotkeyThread.Start();
		hotkeyRegistrationCompleted.Wait(TimeSpan.FromSeconds(1));
	}

	private static void StopHotkeyThread()
	{
		var thread = hotkeyThread;
		if (hotkeyThreadId != 0)
		{
			PostThreadMessage(hotkeyThreadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
		}

		if (thread is not null && thread != Thread.CurrentThread) thread.Join(TimeSpan.FromSeconds(1));
		hotkeyThread = null;
		hotkeyThreadId = 0;
	}

	private static void RunHotkeyMessageLoop()
	{
		hotkeyThreadId = GetCurrentThreadId();
		var toggleText = Preferences.Default.Get("ToggleChatHotkey", "Alt+Z");
		var resumeText = Preferences.Default.Get("ResumeChatHotkey", "Alt+Shift+Z");
		TryParseHotkey(toggleText, out var toggleModifiers, out var toggleKey, out _, out _);
		TryParseHotkey(resumeText, out var resumeModifiers, out var resumeKey, out _, out _);
		toggleChatHotkeyRegistered = RegisterHotKey(IntPtr.Zero, ToggleChatHotkeyId, toggleModifiers, toggleKey);
		if (!toggleChatHotkeyRegistered) hotkeyRegistrationError = $"Windows could not register {toggleText}; it may be used by another app.";
		resumeChatHotkeyRegistered = RegisterHotKey(IntPtr.Zero, ResumeChatHotkeyId, resumeModifiers, resumeKey);
		if (!resumeChatHotkeyRegistered) hotkeyRegistrationError = $"Windows could not register {resumeText}; it may be used by another app.";
		hotkeyRegistrationCompleted.Set();

		while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
		{
			if (message.message != WmHotkey) continue;
			if (message.wParam.ToInt32() == ToggleChatHotkeyId)
			{
				ToggleChatFromHotkey();
			}
			else if (message.wParam.ToInt32() == ResumeChatHotkeyId)
			{
				OpenChatWithoutCaptureFromHotkey();
			}
		}

		if (toggleChatHotkeyRegistered)
		{
			UnregisterHotKey(IntPtr.Zero, ToggleChatHotkeyId);
			toggleChatHotkeyRegistered = false;
		}
		if (resumeChatHotkeyRegistered)
		{
			UnregisterHotKey(IntPtr.Zero, ResumeChatHotkeyId);
			resumeChatHotkeyRegistered = false;
		}
	}

	private static void ToggleChatFromHotkey()
	{
		MainThread.BeginInvokeOnMainThread(() => MainPage.Current?.ToggleChat());
	}

	private static void OpenChatWithoutCaptureFromHotkey()
	{
		MainThread.BeginInvokeOnMainThread(() => MainPage.Current?.OpenChatWithoutCapture());
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct NativeMessage
	{
		public IntPtr hwnd;
		public uint message;
		public IntPtr wParam;
		public IntPtr lParam;
		public uint time;
		public Point point;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct Point
	{
		public int x;
		public int y;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct DwmMargins
	{
		public int Left;
		public int Right;
		public int Top;
		public int Bottom;
	}

	[DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
	private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

	[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
	private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

	[DllImport("dwmapi.dll")]
	private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref DwmMargins margins);

	[DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern IntPtr CreateWindowEx(int exStyle, string className, string? windowName, int style,
		int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

	[DllImport("user32.dll")]
	private static extern bool ShowWindow(IntPtr hwnd, int command);

	[DllImport("user32.dll")]
	private static extern bool DestroyWindow(IntPtr hwnd);

	[DllImport("gdi32.dll")]
	private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

	[DllImport("gdi32.dll")]
	private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);

	[DllImport("gdi32.dll")]
	private static extern int CombineRgn(IntPtr destination, IntPtr source1, IntPtr source2, int mode);

	[DllImport("gdi32.dll")]
	private static extern bool DeleteObject(IntPtr handle);

	[DllImport("user32.dll")]
	private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern int GetMessage(out NativeMessage lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool PostThreadMessage(uint idThread, int msg, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll")]
	private static extern bool GetCursorPos(out Point point);

	[DllImport("kernel32.dll")]
	private static extern uint GetCurrentThreadId();
}
#endif
