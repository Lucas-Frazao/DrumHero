using CommunityToolkit.Mvvm.ComponentModel;
using DrumHero.Infrastructure;

namespace DrumHero.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public NavigationService Navigation { get; }
    
    [ObservableProperty]
    private string _title = "Drum Hero";
    
    public MainViewModel(NavigationService navigation)
    {
        Navigation = navigation;
    }
    
    public async Task InitializeAsync()
    {
        await Navigation.NavigateToAsync<LibraryViewModel>();
    }
}
