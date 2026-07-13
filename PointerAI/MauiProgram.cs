using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using PointerAI.Services;
#if WINDOWS
using Microsoft.Maui.Handlers;
#endif

namespace PointerAI;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
#if WINDOWS
        EntryHandler.Mapper.AppendToMapping("BorderlessComposer", (handler, view) =>
        {
            handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
            handler.PlatformView.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            handler.PlatformView.Resources["TextControlBorderThemeThickness"] = new Microsoft.UI.Xaml.Thickness(0);
            handler.PlatformView.Resources["TextControlBorderThemeThicknessFocused"] = new Microsoft.UI.Xaml.Thickness(0);
        });
#endif
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                fonts.AddFont("Inter-Regular.ttf", "InterRegular");
                fonts.AddFont("Inter-SemiBold.ttf", "InterSemiBold");
                fonts.AddFont("Poppins-Regular.ttf", "PoppinsRegular");
                fonts.AddFont("Poppins-SemiBold.ttf", "PoppinsSemiBold");
            })
            .ConfigureLifecycleEvents(events =>
            {
#if WINDOWS
                events.AddWindows(windows =>
                    windows.OnWindowCreated(window => Platforms.Windows.OverlayWindow.Configure(window)));
#endif
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(90) });
        builder.Services.AddSingleton<GeminiScreenAssistant>();
        builder.Services.AddSingleton<MainPage>();

        return builder.Build();
    }
}
