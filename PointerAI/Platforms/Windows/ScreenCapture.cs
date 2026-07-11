#if WINDOWS
using System.Runtime.InteropServices;

namespace PointerAI.Platforms.Windows;

internal static class ScreenCapture
{
	private const int SmXVirtualScreen = 76;
	private const int SmYVirtualScreen = 77;
	private const int SmCxVirtualScreen = 78;
	private const int SmCyVirtualScreen = 79;
	private const int Srccopy = 0x00CC0020;

	public static byte[] CaptureDesktopPng()
	{
		var x = GetSystemMetrics(SmXVirtualScreen);
		var y = GetSystemMetrics(SmYVirtualScreen);
		var width = GetSystemMetrics(SmCxVirtualScreen);
		var height = GetSystemMetrics(SmCyVirtualScreen);

		var screenDc = GetDC(IntPtr.Zero);
		if (screenDc == IntPtr.Zero)
		{
			throw new InvalidOperationException("Could not get the desktop device context.");
		}

		var memoryDc = CreateCompatibleDC(screenDc);
		if (memoryDc == IntPtr.Zero)
		{
			ReleaseDC(IntPtr.Zero, screenDc);
			throw new InvalidOperationException("Could not create a memory device context.");
		}

		var bitmapHandle = CreateCompatibleBitmap(screenDc, width, height);
		if (bitmapHandle == IntPtr.Zero)
		{
			DeleteDC(memoryDc);
			ReleaseDC(IntPtr.Zero, screenDc);
			throw new InvalidOperationException("Could not create a compatible bitmap.");
		}

		var previousObject = SelectObject(memoryDc, bitmapHandle);

		try
		{
			if (!BitBlt(memoryDc, 0, 0, width, height, screenDc, x, y, Srccopy))
			{
				throw new InvalidOperationException("Desktop capture failed.");
			}

			using var bitmap = System.Drawing.Image.FromHbitmap(bitmapHandle);
			using var stream = new MemoryStream();
			bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
			return stream.ToArray();
		}
		finally
		{
			SelectObject(memoryDc, previousObject);
			DeleteObject(bitmapHandle);
			DeleteDC(memoryDc);
			ReleaseDC(IntPtr.Zero, screenDc);
		}
	}

	[DllImport("user32.dll")]
	private static extern int GetSystemMetrics(int nIndex);

	[DllImport("user32.dll")]
	private static extern IntPtr GetDC(IntPtr hWnd);

	[DllImport("user32.dll")]
	private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

	[DllImport("gdi32.dll")]
	private static extern IntPtr CreateCompatibleDC(IntPtr hDc);

	[DllImport("gdi32.dll")]
	private static extern bool DeleteDC(IntPtr hDc);

	[DllImport("gdi32.dll")]
	private static extern IntPtr CreateCompatibleBitmap(IntPtr hDc, int width, int height);

	[DllImport("gdi32.dll")]
	private static extern IntPtr SelectObject(IntPtr hDc, IntPtr hObject);

	[DllImport("gdi32.dll")]
	private static extern bool DeleteObject(IntPtr hObject);

	[DllImport("gdi32.dll")]
	private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int width, int height, IntPtr hdcSrc, int xSrc, int ySrc, int rasterOperation);
}
#endif
