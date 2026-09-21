using System.Windows;
using System.Windows.Threading;
using EdelByte.MailSetup.Diagnostics;

namespace EdelByte.MailSetup;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandled;
        base.OnStartup(e);
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unerwarteter Fehler in der Oberfläche", e.Exception);
        MessageBox.Show("Es ist ein unerwarteter Fehler aufgetreten. Das Protokoll finden Sie unter:\n" + Log.CurrentFile,
            "EdelByte Mail Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
