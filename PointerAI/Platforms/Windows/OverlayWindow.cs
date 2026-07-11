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
	private const int HotkeyId = 9001;
	private const uint ModAlt = 0x0001;
	private const uint VkZ = 0x5A;
	private const int WmHotkey = 0x0312;
	private const int WmQuit = 0x0012;

	private static AppWindow? appWindow;
	private static IntPtr windowHandle;
	private static FrameworkElement? windowContent;
	private static Thread? hotkeyThread;
	private static uint hotkeyThreadId;
	private static bool hotkeyRegistered;

	public static void Configure(Microsoft.UI.Xaml.Window window)
	{
		window.ExtendsContentIntoTitleBar = true;
		window.SystemBackdrop = null;
		window.Closed += (_, _) => StopHotkeyThread();

		if (window.Content is FrameworkElement content)
		{
			windowContent = content;
			SetNativeContentSize(CharacterWindowWidth, CharacterWindowHeight);
			content.Loaded += OnWindowContentLoaded;
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
		var width = isVisible ? ChatWindowWidth : CharacterWindowWidth;
		var height = isVisible ? ChatWindowHeight : CharacterWindowHeight;
		SetWindowRgn(windowHandle, IntPtr.Zero, true);
		ResizeAndMove(width, height, centerOnScreen: isVisible);
		if (!isVisible)
		{
			ApplyCharacterRegion();
		}
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
		var x = centerOnScreen
			? workArea.X + (workArea.Width - pixelWidth) / 2
			: workArea.X + workArea.Width - pixelWidth - pixelMargin;
		var y = centerOnScreen
			? workArea.Y + (workArea.Height - pixelHeight) / 2
			: workArea.Y + workArea.Height - pixelHeight - pixelMargin;

		appWindow.Resize(new SizeInt32(pixelWidth, pixelHeight));
		appWindow.Move(new PointInt32(x, y));
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
		using var stream = typeof(OverlayWindow).Assembly.GetManifestResourceStream("PointerAI.character.png");
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

		hotkeyThread = new Thread(RunHotkeyMessageLoop)
		{
			IsBackground = true,
			Name = "Pointer AI Alt+Z Hotkey"
		};
		hotkeyThread.SetApartmentState(ApartmentState.STA);
		hotkeyThread.Start();
	}

	private static void StopHotkeyThread()
	{
		if (hotkeyThreadId != 0)
		{
			PostThreadMessage(hotkeyThreadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
		}

		hotkeyThread = null;
		hotkeyThreadId = 0;
	}

	private static void RunHotkeyMessageLoop()
	{
		hotkeyThreadId = GetCurrentThreadId();
		hotkeyRegistered = RegisterHotKey(IntPtr.Zero, HotkeyId, ModAlt, VkZ);

		while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
		{
			if (message.message == WmHotkey && message.wParam.ToInt32() == HotkeyId)
			{
				ToggleChatFromHotkey();
			}
		}

		if (hotkeyRegistered)
		{
			UnregisterHotKey(IntPtr.Zero, HotkeyId);
			hotkeyRegistered = false;
		}
	}

	private static void ToggleChatFromHotkey()
	{
		MainThread.BeginInvokeOnMainThread(() => MainPage.Current?.ToggleChat());
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

	[DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
	private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

	[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
	private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

	[DllImport("gdi32.dll")]
	private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

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

	[DllImport("kernel32.dll")]
	private static extern uint GetCurrentThreadId();
}
#endif
