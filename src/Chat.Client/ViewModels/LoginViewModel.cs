using Chat.Client.Services;
using Chat.Client.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chat.Client.ViewModels;

public partial class LoginViewModel(ChatService chat) : ObservableObject
{
    [ObservableProperty]
    private string _serverUrl = Preferences.Get("server", ChatService.DefaultServerUrl);

    [ObservableProperty]
    private string _userName = Preferences.Get("user", "");

    [ObservableProperty]
    private string _room = Preferences.Get("room", "general");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _error;

    public bool IsNotBusy => !IsBusy;

    [RelayCommand]
    private async Task ConnectAsync()
    {
        Error = null;
        if (string.IsNullOrWhiteSpace(UserName)) { Error = "Enter a user name."; return; }
        if (string.IsNullOrWhiteSpace(Room)) { Error = "Enter a room."; return; }
        if (!Uri.TryCreate(ServerUrl, UriKind.Absolute, out _)) { Error = "Server URL is invalid."; return; }

        IsBusy = true;
        try
        {
            await chat.ConnectAsync(ServerUrl.Trim(), UserName.Trim());

            Preferences.Set("server", ServerUrl.Trim());
            Preferences.Set("user", UserName.Trim());
            Preferences.Set("room", Room.Trim());

            await Shell.Current.GoToAsync(nameof(ChatPage), new Dictionary<string, object>
            {
                ["room"] = Room.Trim().ToLowerInvariant(),
            });
        }
        catch (Exception ex)
        {
            Error = $"Could not connect: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
