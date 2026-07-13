using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Satl_Gui.Pages;
using Satl_Gui.ViewModels;

namespace Satl_Gui;

public sealed partial class MainPage : Page
{
    private bool _initialized;
    public MainViewModel ViewModel => App.ViewModel;

    public MainPage()
    {
        InitializeComponent();
        Loaded += MainPage_Loaded;
        ContentFrame.Navigate(typeof(GamesPage));
        Navigation.SelectedItem = GamesItem;
    }

    private async void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }
        _initialized = true;
        await ViewModel.InitializeAsync();
    }

    private void Navigation_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        var tag = (args.InvokedItemContainer as NavigationViewItem)?.Tag?.ToString();
        var destination = tag switch
        {
            "managed" => typeof(ManagedPage),
            "settings" => typeof(SettingsPage),
            _ => typeof(GamesPage),
        };
        if (ContentFrame.CurrentSourcePageType != destination)
        {
            ContentFrame.Navigate(destination);
        }
        if (Navigation.DisplayMode == NavigationViewDisplayMode.Minimal)
        {
            Navigation.IsPaneOpen = false;
        }
    }
}
