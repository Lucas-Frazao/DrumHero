using DrumHero.Infrastructure;
using DrumHero.ViewModels;
using System.Windows;

namespace DrumHero;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel, NavigationService navigation)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
