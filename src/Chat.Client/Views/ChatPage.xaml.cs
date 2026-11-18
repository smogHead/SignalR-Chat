using Chat.Client.ViewModels;

namespace Chat.Client.Views;

public partial class ChatPage : ContentPage
{
    private readonly ChatViewModel _vm;
    private bool _started;

    public ChatPage(ChatViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _vm.MessageAdded += item => MessagesView.ScrollTo(item, position: ScrollToPosition.End, animate: false);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_started) return;
        _started = true;
        await _vm.StartAsync();
    }

    protected override async void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        base.OnNavigatedFrom(args);
        // Leaving via the back button => tear down. (Navigating *forward* would keep it.)
        if (args.NavigationType == NavigationType.Pop)
            await _vm.StopAsync();
    }
}
