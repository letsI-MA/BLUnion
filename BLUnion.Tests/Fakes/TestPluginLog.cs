using Dalamud.Plugin.Services;
using Serilog;
using Serilog.Events;

namespace BLUnion.Tests.Fakes;

/// <summary>
/// Minimaler No-Op-Fake für <see cref="IPluginLog"/> (Dalamud) - alle Member sind in Dalamud
/// ABSTRACT deklariert (kein Default-Interface-Method-Chaining, per Reflection gegen die lokal
/// installierte Dalamud.dll verifiziert), müssen also vollständig implementiert werden. Für die
/// hier getesteten Services (SpellDataService) wird nur Information/Warning/Error tatsächlich
/// aufgerufen - alle anderen Member sind reine No-Ops/Platzhalter, die im Testlauf nie erreicht
/// werden.
///
/// Siehe TEST_REPORT.md ("nicht isoliert testbare Services") für die Begründung, warum dieser
/// simple Fake für SpellDataService ausreicht, aber NICHT für LocalSpellUnlockService/
/// PartyService/LiveSyncService verwendet wird (die brauchen echte Dalamud-Laufzeitdienste wie
/// IDataManager/IObjectTable/IPartyList bzw. eine echte HTTP-Gegenstelle, kein reines Logging-
/// Interface).
/// </summary>
public sealed class TestPluginLog : IPluginLog
{
    public ILogger Logger => Serilog.Log.Logger;

    public LogEventLevel MinimumLogLevel { get; set; } = LogEventLevel.Verbose;

    public void Fatal(string messageTemplate, params object[] values)
    {
    }

    public void Fatal(Exception? exception, string messageTemplate, params object[] values)
    {
    }

    public void Error(string messageTemplate, params object[] values)
    {
    }

    public void Error(Exception? exception, string messageTemplate, params object[] values)
    {
    }

    public void Warning(string messageTemplate, params object[] values)
    {
    }

    public void Warning(Exception? exception, string messageTemplate, params object[] values)
    {
    }

    public void Information(string messageTemplate, params object[] values)
    {
    }

    public void Information(Exception? exception, string messageTemplate, params object[] values)
    {
    }

    public void Info(string messageTemplate, params object[] values)
    {
    }

    public void Info(Exception? exception, string messageTemplate, params object[] values)
    {
    }

    public void Debug(string messageTemplate, params object[] values)
    {
    }

    public void Debug(Exception? exception, string messageTemplate, params object[] values)
    {
    }

    public void Verbose(string messageTemplate, params object[] values)
    {
    }

    public void Verbose(Exception? exception, string messageTemplate, params object[] values)
    {
    }

    public void Write(LogEventLevel level, Exception? exception, string messageTemplate, params object[] values)
    {
    }
}
