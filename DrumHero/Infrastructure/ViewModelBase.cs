using CommunityToolkit.Mvvm.ComponentModel;

namespace DrumHero.Infrastructure;

public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    private bool _isBusy;
    
    [ObservableProperty]
    private string _busyMessage = string.Empty;
    
    /// <summary>
    /// Called when navigated to.
    /// </summary>
    public virtual Task OnNavigatedToAsync() => Task.CompletedTask;
    
    /// <summary>
    /// Called when navigated away from.
    /// </summary>
    public virtual Task OnNavigatedFromAsync() => Task.CompletedTask;
}
