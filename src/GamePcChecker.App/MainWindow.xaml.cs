using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using GamePcChecker.App.Services;
using GamePcChecker.App.ViewModels;

namespace GamePcChecker.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel(new HardwareProbeService());
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    private void RuntimeDownload_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Не удалось открыть ссылку: {ex.Message}",
                "Game PC Checker",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}