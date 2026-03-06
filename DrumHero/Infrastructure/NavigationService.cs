using CommunityToolkit.Mvvm.ComponentModel;

namespace DrumHero.Infrastructure;

public partial class NavigationService : ObservableObject
{
    private readonly Dictionary<Type, Func<ViewModelBase>> _viewModelFactories = new();
    
    [ObservableProperty]
    private ViewModelBase? _currentViewModel;
    
    public void Register<TViewModel>(Func<TViewModel> factory) where TViewModel : ViewModelBase
    {
        _viewModelFactories[typeof(TViewModel)] = () => factory();
    }
    
    public async Task NavigateToAsync<TViewModel>(Action<TViewModel>? configure = null) where TViewModel : ViewModelBase
    {
        if (CurrentViewModel != null)
        {
            await CurrentViewModel.OnNavigatedFromAsync();
        }
        
        if (_viewModelFactories.TryGetValue(typeof(TViewModel), out var factory))
        {
            var vm = (TViewModel)factory();
            configure?.Invoke(vm);
            CurrentViewModel = vm;
            await vm.OnNavigatedToAsync();
        }
    }
    
    public async Task NavigateToAsync(ViewModelBase viewModel)
    {
        if (CurrentViewModel != null)
        {
            await CurrentViewModel.OnNavigatedFromAsync();
        }
        
        CurrentViewModel = viewModel;
        await viewModel.OnNavigatedToAsync();
    }
}
