using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace DrumHero.Views;

/// <summary>
/// A two-panel import dialog that clearly distinguishes between
/// the FLAC backing track and the Songsterr drum MIDI file.
/// Supports both click-to-browse and drag-and-drop for each panel.
/// </summary>
public partial class ImportSongDialog : Window
{
    private static readonly Brush DefaultBorder = new SolidColorBrush(Color.FromRgb(0x2A, 0x3A, 0x5E));
    private static readonly Brush DragOverBorder = new SolidColorBrush(Color.FromRgb(0x4E, 0xCD, 0xC4));
    private static readonly Brush SelectedBorder = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
    private static readonly Brush SelectedBg = new SolidColorBrush(Color.FromRgb(0x1A, 0x2B, 0x45));

    static ImportSongDialog()
    {
        DefaultBorder.Freeze();
        DragOverBorder.Freeze();
        SelectedBorder.Freeze();
        SelectedBg.Freeze();
    }

    /// <summary>
    /// The selected FLAC file path, or null if not selected.
    /// </summary>
    public string? FlacFilePath { get; private set; }

    /// <summary>
    /// The selected MIDI file path, or null if not selected.
    /// </summary>
    public string? MidiFilePath { get; private set; }

    public ImportSongDialog()
    {
        InitializeComponent();
    }

    #region FLAC Panel

    private void FlacPanel_Click(object sender, MouseButtonEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a FLAC audio file (backing track)",
            Filter = "FLAC files (*.flac)|*.flac",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == true)
        {
            SetFlacFile(dialog.FileName);
        }
    }

    private void FlacPanel_DragEnter(object sender, DragEventArgs e)
    {
        if (HasFileWithExtension(e, ".flac"))
        {
            e.Effects = DragDropEffects.Copy;
            FlacPanel.BorderBrush = DragOverBorder;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void FlacPanel_DragLeave(object sender, DragEventArgs e)
    {
        FlacPanel.BorderBrush = FlacFilePath != null ? SelectedBorder : DefaultBorder;
    }

    private void FlacPanel_Drop(object sender, DragEventArgs e)
    {
        FlacPanel.BorderBrush = DefaultBorder;

        var file = GetFirstFileWithExtension(e, ".flac");
        if (file != null)
        {
            SetFlacFile(file);
        }
        else
        {
            MessageBox.Show("Please drop a .flac file.", "Wrong File Type",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        e.Handled = true;
    }

    private void SetFlacFile(string path)
    {
        FlacFilePath = path;
        FlacStatusText.Text = Path.GetFileName(path);
        FlacStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
        FlacPanel.BorderBrush = SelectedBorder;
        FlacPanel.Background = SelectedBg;
        FlacIcon.Text = "✅";
        UpdateImportButton();
    }

    #endregion

    #region MIDI Panel

    private void MidiPanel_Click(object sender, MouseButtonEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select the drum MIDI file (from Songsterr)",
            Filter = "MIDI files (*.mid;*.midi)|*.mid;*.midi",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == true)
        {
            SetMidiFile(dialog.FileName);
        }
    }

    private void MidiPanel_DragEnter(object sender, DragEventArgs e)
    {
        if (HasFileWithExtension(e, ".mid", ".midi"))
        {
            e.Effects = DragDropEffects.Copy;
            MidiPanel.BorderBrush = DragOverBorder;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void MidiPanel_DragLeave(object sender, DragEventArgs e)
    {
        MidiPanel.BorderBrush = MidiFilePath != null ? SelectedBorder : DefaultBorder;
    }

    private void MidiPanel_Drop(object sender, DragEventArgs e)
    {
        MidiPanel.BorderBrush = DefaultBorder;

        var file = GetFirstFileWithExtension(e, ".mid", ".midi");
        if (file != null)
        {
            SetMidiFile(file);
        }
        else
        {
            MessageBox.Show("Please drop a .mid or .midi file.", "Wrong File Type",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        e.Handled = true;
    }

    private void SetMidiFile(string path)
    {
        MidiFilePath = path;
        MidiStatusText.Text = Path.GetFileName(path);
        MidiStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
        MidiPanel.BorderBrush = SelectedBorder;
        MidiPanel.Background = SelectedBg;
        MidiIcon.Text = "✅";
        UpdateImportButton();
    }

    #endregion

    #region Buttons

    private void UpdateImportButton()
    {
        ImportButton.IsEnabled = FlacFilePath != null && MidiFilePath != null;
    }

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    #endregion

    #region Drag-Drop Helpers

    private static bool HasFileWithExtension(DragEventArgs e, params string[] extensions)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return false;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        return files?.Any(f => extensions.Any(ext =>
            f.EndsWith(ext, StringComparison.OrdinalIgnoreCase))) == true;
    }

    private static string? GetFirstFileWithExtension(DragEventArgs e, params string[] extensions)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return null;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        return files?.FirstOrDefault(f => extensions.Any(ext =>
            f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)));
    }

    #endregion
}
