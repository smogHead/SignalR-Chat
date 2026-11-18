using Chat.Client.Services;
using Chat.Client.ViewModels;
using Chat.Client.Views;
using Microsoft.Extensions.Logging;

namespace Chat.Client;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        builder.Services.AddSingleton<ChatService>();

        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<ChatViewModel>();
        builder.Services.AddTransient<ChatPage>();

        return builder.Build();
    }
}
