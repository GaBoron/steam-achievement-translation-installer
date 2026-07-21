using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Satl_Gui.Models;
using Satl_Gui.ViewModels;

namespace Satl_Gui.Pages;

public sealed partial class CloudGamesPage : Page
{
    public GameInventoryViewModel ViewModel { get; } = new(GameInventoryScope.Cloud);

    public CloudGamesPage()
    {
        InitializeComponent();
        Loaded += CloudGamesPage_Loaded;
    }

    private async void CloudGamesPage_Loaded(object sender, RoutedEventArgs e) =>
        await ViewModel.InitializeAsync();

    private async void Refresh_Click(object sender, RoutedEventArgs e) =>
        await ViewModel.RefreshAsync();
}
