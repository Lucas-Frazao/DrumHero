using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DrumHero.ViewModels;

namespace DrumHero.Views;

public partial class PracticeView : UserControl
{
    private DispatcherTimer? _renderTimer;
    
    public PracticeView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }
    
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        HighwayControl.StartRendering();
        
        _renderTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _renderTimer.Tick += OnRenderTick;
        _renderTimer.Start();
    }
    
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        HighwayControl.StopRendering();
        _renderTimer?.Stop();
        
        if (DataContext is PracticeViewModel vm)
        {
            vm.LaneFlash -= OnLaneFlash;
            vm.HighwayDataChanged -= OnHighwayDataChanged;
        }
    }
    
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is PracticeViewModel oldVm)
        {
            oldVm.LaneFlash -= OnLaneFlash;
            oldVm.HighwayDataChanged -= OnHighwayDataChanged;
        }
        
        if (e.NewValue is PracticeViewModel newVm)
        {
            newVm.LaneFlash += OnLaneFlash;
            newVm.HighwayDataChanged += OnHighwayDataChanged;
            HighwayControl.SetNotes(newVm.HighwayNotes);
        }
    }
    
    private void OnRenderTick(object? sender, EventArgs e)
    {
        if (DataContext is PracticeViewModel vm)
        {
            HighwayControl.SetCurrentTime(vm.CurrentTimeSeconds);
        }
    }
    
    private void OnLaneFlash(Models.DrumLane lane, bool isHit)
    {
        Dispatcher.Invoke(() => HighwayControl.FlashLane(lane, isHit));
    }
    
    private void OnHighwayDataChanged()
    {
        if (DataContext is PracticeViewModel vm)
        {
            Dispatcher.Invoke(() => HighwayControl.SetNotes(vm.HighwayNotes));
        }
    }
    
    private void DismissHistoryClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is PracticeViewModel vm)
        {
            vm.ToggleHistoryCommand.Execute(null);
        }
    }
}
