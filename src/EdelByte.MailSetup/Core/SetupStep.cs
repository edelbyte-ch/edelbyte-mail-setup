namespace EdelByte.MailSetup.Core;

public enum StepState { Pending, Running, Done, Skipped, Failed }

/// <summary>Eine Zeile in der Fortschrittsliste. Text bleibt frei von Fachbegriffen.</summary>
public sealed class SetupStep
{
    public string Key { get; }
    public string Title { get; }
    public StepState State { get; set; } = StepState.Pending;
    /// <summary>Kurzer Zusatz, z. B. «bereits vorhanden» – nur im Expertenmodus sichtbar.</summary>
    public string? Note { get; set; }

    public SetupStep(string key, string title) { Key = key; Title = title; }
}

public sealed class SetupProgress
{
    public IReadOnlyList<SetupStep> Steps { get; }
    public event Action<SetupStep>? Changed;

    public SetupProgress()
    {
        Steps = new[]
        {
            new SetupStep("server",   "Server gefunden"),
            new SetupStep("outlook",  "Outlook gefunden"),
            new SetupStep("mail",     "E-Mail eingerichtet"),
            new SetupStep("addin",    "Kalender-Erweiterung bereit"),
            new SetupStep("calendar", "Kalender eingerichtet"),
            new SetupStep("contacts", "Kontakte eingerichtet"),
            new SetupStep("start",    "Outlook gestartet"),
        };
    }

    public SetupStep this[string key] => Steps.First(s => s.Key == key);

    public void Set(string key, StepState state, string? note = null)
    {
        var s = this[key];
        s.State = state;
        if (note is not null) s.Note = note;
        Changed?.Invoke(s);
    }
}
