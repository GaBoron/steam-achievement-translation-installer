using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Satl_Gui.Models;
using Satl_Gui.ViewModels;

namespace Satl_Gui.Pages;

public sealed partial class LocalGamesPage : Page
{
    public GameInventoryViewModel ViewModel { get; } = new(GameInventoryScope.Local);

    public LocalGamesPage()
    {
        InitializeComponent();
        Loaded += LocalGamesPage_Loaded;
    }

    private async void LocalGamesPage_Loaded(object sender, RoutedEventArgs e) =>
        await ViewModel.InitializeAsync();

    private async void Refresh_Click(object sender, RoutedEventArgs e) =>
        await ViewModel.RefreshAsync();
}
