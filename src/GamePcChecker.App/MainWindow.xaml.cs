using System.Windows;
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
}