using System.ComponentModel;
using System.Windows.Media;
using EdelByte.MailSetup.Core;

namespace EdelByte.MailSetup.Ui;

/// <summary>Anzeigemodell einer Fortschrittszeile. Übersetzt den Zustand in Zeichen und Farbe.</summary>
public sealed class StepRow : INotifyPropertyChanged
{
    private static readonly Brush Blau = new SolidColorBrush(Color.FromRgb(0x1F, 0x5B, 0xFF));
    private static readonly Brush Nacht = new SolidColorBrush(Color.FromRgb(0x0B, 0x0E, 0x1A));
    private static readonly Brush Muted = new SolidColorBrush(Color.FromArgb(0x9E, 0x0B, 0x0E, 0x1A));
    private static readonly Brush Rot = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B));

    private readonly SetupStep _step;
    private bool _expert;

    public StepRow(SetupStep step) { _step = step; }

    public string Title => _step.Title;
    public string? Note => _expert ? _step.Note : null;

    public string Glyph => _step.State switch
    {
        StepState.Done => "✓",       // ✓
        StepState.Running => "•",    // •
        StepState.Failed => "✕",     // ✕
        StepState.Skipped => "–",    // –
        _ => "○",                    // ○
    };

    public Brush GlyphBrush => _step.State switch
    {
        StepState.Done => Blau,
        StepState.Failed => Rot,
        StepState.Running => Nacht,
        _ => Muted,
    };

    public Brush TitleBrush => _step.State is StepState.Pending ? Muted : Nacht;

    public void SetExpert(bool on) { _expert = on; Changed(nameof(Note)); }

    public void Refresh()
    {
        Changed(nameof(Glyph)); Changed(nameof(GlyphBrush)); Changed(nameof(TitleBrush)); Changed(nameof(Note));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
