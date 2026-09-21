using System.Windows;
using EdelByte.MailSetup.Diagnostics;

namespace EdelByte.MailSetup.Ui;

public partial class DiagnoseWindow : Window
{
    private readonly string? _errorCode;

    public DiagnoseWindow(string? errorCode = null)
    {
        InitializeComponent();
        _errorCode = errorCode;
    }

    public async Task RunAsync()
    {
        try { Report.Text = await DiagnoseReport.BuildAsync(_errorCode); }
        catch (Exception ex) { Report.Text = "Diagnose fehlgeschlagen: " + ex.Message; }
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(Report.Text); } catch { }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
