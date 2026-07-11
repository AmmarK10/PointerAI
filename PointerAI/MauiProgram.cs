using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using PointerAI.Services;

namespace PointerAI;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
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