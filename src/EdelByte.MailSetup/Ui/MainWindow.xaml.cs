using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using EdelByte.MailSetup.Core;
using EdelByte.MailSetup.Diagnostics;
using EdelByte.MailSetup.Update;

namespace EdelByte.MailSetup.Ui;

public partial class MainWindow : Window
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly List<StepRow> _rows = new();
    private SetupOrchestrator? _orchestrator;
    private bool _expert;
    private string? _lastErrorCode;

    public MainWindow()
    {
        InitializeComponent();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("EdelByteMailSetup/" + UpdateChecker.CurrentVersion);
        VersionText.Text = $"Version {UpdateChecker.CurrentVersion}";
        Loaded += async (_, _) => await CheckForUpdateAsync();
    }

    private async void OnStart(object sender, RoutedEventArgs e)
    {
        HideError();
        var email = EmailBox.Text.Trim();
        var password = PasswordBox.Password;

        FormPanel.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Visible;
        DonePanel.Visibility = Visibility.Collapsed;
        FailPanel.Visibility = Visibility.Collapsed;

        _orchestrator = new SetupOrchestrator(_http);
        _rows.Clear();
        foreach (var s in _orchestrator.Progress.Steps) _rows.Add(new StepRow(s));
        foreach (var r in _rows) r.SetExpert(_expert);
        StepList.ItemsSource = _rows;
        _orchestrator.Progress.Changed += OnStepChanged;

        var status = new Progress<string>(m => Dispatcher.Invoke(() => StatusText.Text = m));
        try
        {
            await _orchestrator.RunAsync(email, password, status, CancellationToken.None);
            StatusText.Text = "";
            DonePanel.Visibility = Visibility.Visible;
        }
        catch (SetupException ex)
        {
            Log.Error($"Einrichtung abgebrochen [{ex.Code}]", ex);
            _lastErrorCode = ex.Code;
            ShowFailure(ex.UserMessage, ex.Code);
        }
        catch (Exception ex)
        {
            Log.Error("Einrichtung abgebrochen", ex);
            _lastErrorCode = Codes.Unexpected;
            ShowFailure("Die Einrichtung konnte nicht abgeschlossen werden.", Codes.Unexpected);
        }
        finally
        {
            // Passwortfeld nach dem Lauf leeren – nichts bleibt im Speicher der Oberfläche
            PasswordBox.Clear();
        }
    }

    private void OnStepChanged(SetupStep _)
    {
        Dispatcher.Invoke(() => { foreach (var r in _rows) r.Refresh(); });
    }

    private void ShowFailure(string message, string code)
    {
        StatusText.Text = "";
        FailPanel.Visibility = Visibility.Visible;
        FailText.Text = message;
        FailCode.Text = $"Fehlercode: {code}";
    }

    private void OnRetry(object sender, RoutedEventArgs e)
    {
        ProgressPanel.Visibility = Visibility.Collapsed;
        FormPanel.Visibility = Visibility.Visible;
        HideError();
    }

    private async void OnShowDiagnose(object sender, RoutedEventArgs e)
    {
        var win = new DiagnoseWindow(_lastErrorCode) { Owner = this };
        win.Show();
        await win.RunAsync();
    }

    private async void OnCopySupport(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = await DiagnoseReport.BuildAsync(_lastErrorCode);
            Clipboard.SetText(text);
            MessageBox.Show("Die Supportinformationen wurden kopiert. Sie enthalten keine Passwörter.",
                "EdelByte Mail Setup", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { Log.Error("Support kopieren fehlgeschlagen", ex); }
    }

    private void OnToggleExpert(object sender, RoutedEventArgs e)
    {
        _expert = !_expert;
        ExpertToggle.Content = _expert ? "Details ausblenden" : "Details";
        Hint.Visibility = _expert ? Visibility.Visible : Visibility.Visible;
        foreach (var r in _rows) r.SetExpert(_expert);
        DiagnoseLink.Visibility = _expert ? Visibility.Visible : DiagnoseLink.Visibility;
    }

    private async Task CheckForUpdateAsync()
    {
        var info = await UpdateChecker.CheckAsync(_http);
        if (info is null) return;
        var r = MessageBox.Show($"Eine neuere Version ({info.version}) ist verfügbar. Jetzt laden?",
            "EdelByte Mail Setup", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r == MessageBoxResult.Yes && await UpdateChecker.DownloadAndRunAsync(_http, info))
            Close();
    }

    private void HideError() { ErrorText.Visibility = Visibility.Collapsed; DiagnoseLink.Visibility = Visibility.Collapsed; }
    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
