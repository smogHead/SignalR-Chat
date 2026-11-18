namespace Chat.Client;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new AppShell())
        {
            Title = "SignalR Chat",
#if WINDOWS || MACCATALYST
            Width = 480,
            Height = 760,
#endif
        };
}
