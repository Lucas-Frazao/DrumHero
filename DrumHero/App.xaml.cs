using DrumHero.Analysis;
using DrumHero.Audio;
using DrumHero.Infrastructure;
using DrumHero.Persistence;
using DrumHero.Services;
using DrumHero.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace DrumHero;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Log to file for debugging
        var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DrumHero", "startup.log");
        var logDir = Path.GetDirectoryName(logPath);
        if (logDir != null && !Directory.Exists(logDir))
            Directory.CreateDirectory(logDir);

        File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] App startup began\n");

        // Handle unhandled exceptions
        this.DispatcherUnhandledException += (s, args) =>
        {
            Debug.WriteLine($"DispatcherUnhandledException: {args.Exception.Message}");
            MessageBox.Show($"Unhandled error:\n\n{args.Exception.Message}\n\n{args.Exception.StackTrace}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            Debug.WriteLine($"AppDomain.UnhandledException");
            var ex = (Exception)args.ExceptionObject;
            MessageBox.Show($"Critical error:\n\n{ex.Message}\n\n{ex.StackTrace}",
                "Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        try
        {
            LogToFile("Configuring services...");
            Debug.WriteLine("Configuring services...");
            var services = new ServiceCollection();
            ConfigureServices(services);

            LogToFile("Building service provider...");
            Debug.WriteLine("Building service provider...");
            _serviceProvider = services.BuildServiceProvider();

            // Register ViewModel factories with navigation service
            LogToFile("Registering view models...");
            Debug.WriteLine("Registering view models...");
            var navigation = _serviceProvider.GetRequiredService<NavigationService>();
            navigation.Register(() => _serviceProvider.GetRequiredService<LibraryViewModel>());
            navigation.Register(() => _serviceProvider.GetRequiredService<PracticeViewModel>());
            navigation.Register(() => _serviceProvider.GetRequiredService<SettingsViewModel>());
            navigation.Register(() => _serviceProvider.GetRequiredService<AnalysisProgressViewModel>());

            LogToFile("Creating main window...");
            Debug.WriteLine("Creating main window...");
            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();
            LogToFile("Main window shown");
            Debug.WriteLine("Main window shown");

            // Initialize navigation to library view asynchronously
            LogToFile("Starting async initialization...");
            Debug.WriteLine("Starting async initialization...");
            _ = InitializeAsync();
        }
        catch (Exception ex)
        {
            LogToFile($"Startup exception: {ex.Message}\n{ex.StackTrace}");
            Debug.WriteLine($"Startup exception: {ex.Message}\n{ex.StackTrace}");
            MessageBox.Show($"Failed to start application:\n\n{ex.Message}\n\n{ex.StackTrace}",
                "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private async Task InitializeAsync()
    {
        try
        {
            LogToFile("InitializeAsync started");
            Debug.WriteLine("InitializeAsync started");
            var mainVm = _serviceProvider!.GetRequiredService<MainViewModel>();
            LogToFile("Got MainViewModel, calling InitializeAsync...");
            Debug.WriteLine("Got MainViewModel, calling InitializeAsync...");
            await mainVm.InitializeAsync();
            LogToFile("InitializeAsync completed");
            Debug.WriteLine("InitializeAsync completed");
        }
        catch (Exception ex)
        {
            LogToFile($"InitializeAsync exception: {ex.Message}\n{ex.StackTrace}");
            Debug.WriteLine($"InitializeAsync exception: {ex.Message}\n{ex.StackTrace}");
            MessageBox.Show($"Initialization failed:\n\n{ex.Message}\n\n{ex.StackTrace}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void LogToFile(string message)
    {
        try
        {
            var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DrumHero", "startup.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\n");
        }
        catch { }
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Persistence
        var fileStorage = new FileStorageService();
        var dbManager = new DatabaseManager(fileStorage.GetDatabasePath());

        services.AddSingleton(fileStorage);
        services.AddSingleton(dbManager);
        services.AddSingleton<SongRepository>();
        services.AddSingleton<PracticeRunRepository>();
        services.AddSingleton<SettingsRepository>();

        // Analysis service (Demucs local - recommended)
        // Try to use virtual environment Python first, fall back to system Python
        // AppContext.BaseDirectory = C:\GeneralProjects\DrumHero\DrumHero\bin\Debug\net8.0-windows\
        // Need to go up 4 levels to reach solution folder, then access .venv
        var baseDir = AppContext.BaseDirectory;
        var venvPython = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".venv", "Scripts", "python.exe"));
        var pythonExists = File.Exists(venvPython);
        var pythonPath = pythonExists ? venvPython : "python";

        // Log the resolution
        LogToFile($"BaseDirectory: {baseDir}");
        LogToFile($"Resolved venv path: {venvPython}");
        LogToFile($"Venv Python exists: {pythonExists}");
        LogToFile($"Using Python: {pythonPath}");

        services.AddSingleton<IAnalysisService>(sp => new DemucsAnalysisService(pythonPath));

        // Audio/MIDI engines
        services.AddSingleton<AudioPlaybackEngine>();
        services.AddSingleton<MidiInputEngine>();
        services.AddSingleton<HitDetectionEngine>();

        // Application services
        services.AddSingleton<SongImportService>();
        services.AddSingleton<SongLibraryService>();
        services.AddSingleton<SettingsService>();
        services.AddTransient<PracticeSessionService>();

        // Navigation
        var navigation = new NavigationService();
        services.AddSingleton(navigation);

        // ViewModels
        services.AddTransient<LibraryViewModel>();
        services.AddTransient<PracticeViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<AnalysisProgressViewModel>();
        services.AddSingleton<MainViewModel>();

        // ViewModel factories for navigation
        services.AddSingleton<Func<PracticeViewModel>>(sp => () => sp.GetRequiredService<PracticeViewModel>());
        services.AddSingleton<Func<AnalysisProgressViewModel>>(sp => () => sp.GetRequiredService<AnalysisProgressViewModel>());

        // Main window
        services.AddSingleton<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
