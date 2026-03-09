using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DrumHero.Models;

namespace DrumHero.Views.Controls;

/// <summary>
/// Guitar Hero-style drum highway renderer using WPF Canvas + CompositionTarget.Rendering.
/// Notes scroll from top to bottom toward a fixed hit line.
/// </summary>
public partial class DrumHighwayControl : UserControl
{
    // Lane configuration - maps DrumLane to visual position and color.
    // Ordered to mirror the physical drum kit from the drummer's perspective
    // (left to right): hi-hats → crash 1 → snare/kick (center) → toms → floor → crash 2 → ride → crash 3
    private static readonly LaneConfig[] LaneConfigs = new LaneConfig[]
    {
        new(DrumLane.ClosedHiHat,  "HH-C",   "#4ECDC4"), // Teal       — far left (left foot/hand)
        new(DrumLane.OpenHiHat,    "HH-O",   "#45B7D1"), // Light blue — next to closed HH
        new(DrumLane.Crash1,       "CR1",    "#DDA0DD"), // Plum       — left crash, above hi-hat area
        new(DrumLane.Snare,        "SNARE",  "#FFD700"), // Gold       — center left (right in front of drummer)
        new(DrumLane.LeftKick,     "KICK",   "#FF6B35"), // Orange     — center (pedal, under snare)
        new(DrumLane.RackTom1,     "TOM1",   "#96CEB4"), // Sage       — center right (mounted above kick)
        new(DrumLane.RackTom2,     "TOM2",   "#88D8B0"), // Mint       — right of tom 1
        new(DrumLane.FloorTom,     "FLOOR",  "#FFEAA7"), // Light gold — right side
        new(DrumLane.Crash2,       "CR2",    "#DA70D6"), // Orchid     — right crash, above floor tom
        new(DrumLane.Ride,         "RIDE",   "#87CEEB"), // Sky blue   — far right
        new(DrumLane.Crash3,       "CR3",    "#BA55D3"), // Med orchid — far right (extra crash / china)
    };

    // Rendering state
    private List<HighwayNote> _notes = new();
    private double _currentTimeSeconds;
    private double _lookAheadSeconds = 3.0; // How far ahead to show notes
    private bool _isRendering;

    // Lane flash state for hit/miss feedback
    private readonly Dictionary<DrumLane, (bool IsHit, DateTime Time)> _laneFlashes = new();
    private static readonly TimeSpan FlashDuration = TimeSpan.FromMilliseconds(200);

    // Visual constants
    private const double HitLineYPercent = 0.85; // Hit line at 85% down
    private const double LaneHeaderHeight = 40;
    private const double NoteHeight = 14;
    private const double NoteCornerRadius = 4;

    // Cached brushes and pens for performance
    private static readonly Brush BackgroundBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E));
    private static readonly Brush LaneFillBrushA = new SolidColorBrush(Color.FromArgb(25, 255, 255, 255));
    private static readonly Brush LaneFillBrushB = new SolidColorBrush(Color.FromArgb(12, 255, 255, 255));
    private static readonly Brush HitLineBrush = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255));
    private static readonly Brush LaneSeparatorBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255));
    private static readonly Brush HitFlashBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
    private static readonly Brush MissFlashBrush = new SolidColorBrush(Color.FromRgb(0xE9, 0x45, 0x60));
    private static readonly Brush HeaderBgBrush = new SolidColorBrush(Color.FromArgb(180, 0x16, 0x21, 0x3E));
    private static readonly Brush ReceptorBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));
    private static readonly Pen HitLinePen = new(HitLineBrush, 3);
    private static readonly Pen LaneSeparatorPen = new(LaneSeparatorBrush, 1);
    private static readonly Typeface LabelTypeface = new("Segoe UI");

    static DrumHighwayControl()
    {
        // Freeze brushes for performance
        BackgroundBrush.Freeze();
        LaneFillBrushA.Freeze();
        LaneFillBrushB.Freeze();
        HitLineBrush.Freeze();
        LaneSeparatorBrush.Freeze();
        HitFlashBrush.Freeze();
        MissFlashBrush.Freeze();
        HeaderBgBrush.Freeze();
        ReceptorBrush.Freeze();
        HitLinePen.Freeze();
        LaneSeparatorPen.Freeze();
    }

    public DrumHighwayControl()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Sets the highway notes to render.
    /// </summary>
    public void SetNotes(List<HighwayNote> notes)
    {
        _notes = notes;
    }

    /// <summary>
    /// Updates the current playback position.
    /// </summary>
    public void SetCurrentTime(double timeSeconds)
    {
        _currentTimeSeconds = timeSeconds;
    }

    /// <summary>
    /// Triggers a lane flash for hit/miss feedback.
    /// </summary>
    public void FlashLane(DrumLane lane, bool isHit)
    {
        _laneFlashes[lane] = (isHit, DateTime.UtcNow);
    }

    /// <summary>
    /// Starts the rendering loop.
    /// </summary>
    public void StartRendering()
    {
        if (!_isRendering)
        {
            CompositionTarget.Rendering += OnRendering;
            _isRendering = true;
        }
    }

    /// <summary>
    /// Stops the rendering loop.
    /// </summary>
    public void StopRendering()
    {
        CompositionTarget.Rendering -= OnRendering;
        _isRendering = false;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        RenderToCanvas();
        InvalidateVisual();
    }

    private void RenderToCanvas()
    {
        if (HighwayCanvas == null)
            return;

        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0)
            return;

        HighwayCanvas.Children.Clear();
        HighwayCanvas.Background = BackgroundBrush;

        var laneCount = LaneConfigs.Length;
        var laneWidth = width / laneCount;
        var hitLineY = height * HitLineYPercent;
        var highwayTop = LaneHeaderHeight;
        var highwayHeight = hitLineY - highwayTop;

        for (int i = 0; i < laneCount; i++)
        {
            var laneRect = new Rectangle
            {
                Width = laneWidth,
                Height = height - highwayTop,
                Fill = i % 2 == 0 ? LaneFillBrushA : LaneFillBrushB
            };
            Canvas.SetLeft(laneRect, i * laneWidth);
            Canvas.SetTop(laneRect, highwayTop);
            HighwayCanvas.Children.Add(laneRect);

            if (i > 0)
            {
                var separator = new Rectangle
                {
                    Width = 1,
                    Height = height - highwayTop,
                    Fill = LaneSeparatorBrush
                };
                Canvas.SetLeft(separator, i * laneWidth);
                Canvas.SetTop(separator, highwayTop);
                HighwayCanvas.Children.Add(separator);
            }
        }

        var hitLine = new Rectangle
        {
            Width = width,
            Height = 3,
            Fill = HitLineBrush
        };
        Canvas.SetLeft(hitLine, 0);
        Canvas.SetTop(hitLine, hitLineY - 1.5);
        HighwayCanvas.Children.Add(hitLine);

        var receptorSize = Math.Min(16, laneWidth * 0.45);
        for (int i = 0; i < laneCount; i++)
        {
            var receptor = new Border
            {
                Width = receptorSize,
                Height = receptorSize,
                CornerRadius = new CornerRadius(3),
                Background = ReceptorBrush,
                BorderBrush = LaneSeparatorBrush,
                BorderThickness = new Thickness(1)
            };
            Canvas.SetLeft(receptor, i * laneWidth + (laneWidth - receptorSize) / 2);
            Canvas.SetTop(receptor, hitLineY - receptorSize / 2);
            HighwayCanvas.Children.Add(receptor);
        }

        var now = DateTime.UtcNow;
        foreach (var (lane, (isHit, flashTime)) in _laneFlashes)
        {
            var elapsed = now - flashTime;
            if (elapsed >= FlashDuration)
                continue;

            var laneIndex = GetLaneIndex(lane);
            if (laneIndex < 0)
                continue;

            var alpha = (byte)(200 * (1.0 - elapsed / FlashDuration));
            var flashFill = new SolidColorBrush(
                isHit
                    ? Color.FromArgb(alpha, 0x00, 0xE6, 0x76)
                    : Color.FromArgb(alpha, 0xE9, 0x45, 0x60));

            var flashRect = new Rectangle
            {
                Width = laneWidth,
                Height = 60,
                Fill = flashFill
            };
            Canvas.SetLeft(flashRect, laneIndex * laneWidth);
            Canvas.SetTop(flashRect, hitLineY - 30);
            HighwayCanvas.Children.Add(flashRect);
        }

        var windowStart = _currentTimeSeconds - 0.5;
        var windowEnd = _currentTimeSeconds + _lookAheadSeconds;

        foreach (var note in _notes)
        {
            if (note.TimeSeconds < windowStart || note.TimeSeconds > windowEnd)
                continue;

            var laneIndex = GetLaneIndex(note.Lane);
            if (laneIndex < 0)
                continue;

            var timeDelta = note.TimeSeconds - _currentTimeSeconds;
            var normalizedPosition = timeDelta / _lookAheadSeconds;
            var noteY = hitLineY - (normalizedPosition * highwayHeight);
            if (noteY < highwayTop - NoteHeight || noteY > height + NoteHeight)
                continue;

            Brush noteBrush = note.HitState switch
            {
                NoteHitState.Hit => HitFlashBrush,
                NoteHitState.Missed => MissFlashBrush,
                _ => LaneConfigs[laneIndex].CachedBrush
            };

            var noteRect = new Border
            {
                Width = laneWidth - 8,
                Height = NoteHeight,
                CornerRadius = new CornerRadius(NoteCornerRadius),
                Background = noteBrush
            };

            Canvas.SetLeft(noteRect, laneIndex * laneWidth + 4);
            Canvas.SetTop(noteRect, noteY - NoteHeight / 2);
            HighwayCanvas.Children.Add(noteRect);
        }

        var headerBg = new Rectangle
        {
            Width = width,
            Height = LaneHeaderHeight,
            Fill = HeaderBgBrush
        };
        Canvas.SetLeft(headerBg, 0);
        Canvas.SetTop(headerBg, 0);
        HighwayCanvas.Children.Add(headerBg);

        for (int i = 0; i < laneCount; i++)
        {
            var label = new TextBlock
            {
                Text = LaneConfigs[i].Label,
                Foreground = LaneConfigs[i].CachedBrush,
                FontSize = 11,
                FontFamily = new FontFamily("Segoe UI"),
                FontWeight = FontWeights.SemiBold
            };

            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var x = i * laneWidth + (laneWidth - label.DesiredSize.Width) / 2;
            var y = (LaneHeaderHeight - label.DesiredSize.Height) / 2;
            Canvas.SetLeft(label, x);
            Canvas.SetTop(label, y);
            HighwayCanvas.Children.Add(label);
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0) return;

        // Background
        dc.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, width, height));

        var laneCount = LaneConfigs.Length;
        var laneWidth = width / laneCount;
        var hitLineY = height * HitLineYPercent;
        var highwayTop = LaneHeaderHeight;
        var highwayHeight = hitLineY - highwayTop;

        // Draw lane backgrounds so the highway is visible even when there are no notes yet.
        for (int i = 0; i < laneCount; i++)
        {
            var laneX = i * laneWidth;
            var laneBrush = i % 2 == 0 ? LaneFillBrushA : LaneFillBrushB;
            dc.DrawRectangle(laneBrush, null, new Rect(laneX, highwayTop, laneWidth, height - highwayTop));
        }

        // Draw lane separators
        for (int i = 1; i < laneCount; i++)
        {
            var x = i * laneWidth;
            dc.DrawLine(LaneSeparatorPen, new Point(x, highwayTop), new Point(x, height));
        }

        // Draw hit line
        dc.DrawLine(HitLinePen, new Point(0, hitLineY), new Point(width, hitLineY));

        // Draw lane receptors on the hit line for visibility.
        var receptorSize = Math.Min(16, laneWidth * 0.45);
        for (int i = 0; i < laneCount; i++)
        {
            var centerX = i * laneWidth + laneWidth / 2;
            var receptorRect = new Rect(centerX - receptorSize / 2, hitLineY - receptorSize / 2, receptorSize, receptorSize);
            dc.DrawRoundedRectangle(ReceptorBrush, LaneSeparatorPen, receptorRect, 3, 3);
        }

        // Draw lane flashes
        var now = DateTime.UtcNow;
        foreach (var (lane, (isHit, flashTime)) in _laneFlashes)
        {
            var elapsed = now - flashTime;
            if (elapsed < FlashDuration)
            {
                var laneIndex = GetLaneIndex(lane);
                if (laneIndex < 0) continue;

                var alpha = (byte)(200 * (1.0 - elapsed / FlashDuration));
                var flashBrush = isHit
                    ? new SolidColorBrush(Color.FromArgb(alpha, 0x00, 0xE6, 0x76))
                    : new SolidColorBrush(Color.FromArgb(alpha, 0xE9, 0x45, 0x60));

                dc.DrawRectangle(flashBrush, null,
                    new Rect(laneIndex * laneWidth, hitLineY - 30, laneWidth, 60));
            }
        }

        // Draw notes
        var windowStart = _currentTimeSeconds - 0.5; // Show slightly past notes
        var windowEnd = _currentTimeSeconds + _lookAheadSeconds;

        foreach (var note in _notes)
        {
            if (note.TimeSeconds < windowStart || note.TimeSeconds > windowEnd)
                continue;

            var laneIndex = GetLaneIndex(note.Lane);
            if (laneIndex < 0) continue;

            // Calculate Y position: notes at currentTime should be at hitLineY
            // Notes in the future should be above (smaller Y)
            var timeDelta = note.TimeSeconds - _currentTimeSeconds;
            var normalizedPosition = timeDelta / _lookAheadSeconds; // 0 = at hit line, 1 = top
            var noteY = hitLineY - (normalizedPosition * highwayHeight);

            // Skip if off-screen
            if (noteY < highwayTop - NoteHeight || noteY > height + NoteHeight)
                continue;

            var noteX = laneIndex * laneWidth + 4;
            var noteWidth = laneWidth - 8;

            // Color based on hit state
            Brush noteBrush;
            switch (note.HitState)
            {
                case NoteHitState.Hit:
                    noteBrush = HitFlashBrush;
                    break;
                case NoteHitState.Missed:
                    noteBrush = MissFlashBrush;
                    break;
                default:
                    var config = LaneConfigs[laneIndex];
                    noteBrush = config.CachedBrush;
                    break;
            }

            // Draw rounded rectangle note
            var noteRect = new Rect(noteX, noteY - NoteHeight / 2, noteWidth, NoteHeight);
            dc.DrawRoundedRectangle(noteBrush, null, noteRect, NoteCornerRadius, NoteCornerRadius);
        }

        // Draw lane headers (on top of everything)
        dc.DrawRectangle(HeaderBgBrush, null, new Rect(0, 0, width, LaneHeaderHeight));

        for (int i = 0; i < laneCount; i++)
        {
            var config = LaneConfigs[i];
            var text = new FormattedText(
                config.Label,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                11,
                config.CachedBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            var x = i * laneWidth + (laneWidth - text.Width) / 2;
            dc.DrawText(text, new Point(x, (LaneHeaderHeight - text.Height) / 2));
        }
    }

    private static int GetLaneIndex(DrumLane lane)
    {
        for (int i = 0; i < LaneConfigs.Length; i++)
        {
            if (LaneConfigs[i].Lane == lane) return i;
        }
        return -1;
    }

    private record LaneConfig
    {
        public DrumLane Lane { get; }
        public string Label { get; }
        public string ColorHex { get; }
        public Brush CachedBrush { get; }

        public LaneConfig(DrumLane lane, string label, string colorHex)
        {
            Lane = lane;
            Label = label;
            ColorHex = colorHex;
            var color = (Color)ColorConverter.ConvertFromString(colorHex);
            CachedBrush = new SolidColorBrush(color);
            CachedBrush.Freeze();
        }
    }
}
