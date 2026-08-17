using BLUnion.Models;
using BLUnion.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace BLUnion.UI;

/// <summary>
/// MVP-Fenster: deckt Punkt 11 (Phase 1) des Konzepts ab -
/// Party anzeigen, eigenen Status anzeigen, fehlende Spells + Fundort.
/// Party-Vergleich (Phase 2) und Lernplan (Phase 2/3) sind vorbereitet,
/// aber bewusst noch nicht der Fokus dieses Fensters.
/// </summary>
public sealed class MainWindow : Window
{
    private readonly PartyService partyService;
    private readonly SpellDataService spellDataService;
    private readonly ComparisonService comparisonService;
    private readonly LocalSpellUnlockService localSpellUnlockService;
    private readonly ManualCodeSyncProvider syncProvider;

    private string importCodeBuffer = string.Empty;
    private string? lastError;

    public MainWindow(
        PartyService partyService,
        SpellDataService spellDataService,
        ComparisonService comparisonService,
        LocalSpellUnlockService localSpellUnlockService,
        ManualCodeSyncProvider syncProvider)
        : base("BLUnion###BLUnion")
    {
        this.partyService = partyService;
        this.spellDataService = spellDataService;
        this.comparisonService = comparisonService;
        this.localSpellUnlockService = localSpellUnlockService;
        this.syncProvider = syncProvider;

        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new System.Numerics.Vector2(420, 300),
            MaximumSize = new System.Numerics.Vector2(1200, 1200),
        };
    }

    public override void Draw()
    {
        if (ImGui.BeginTabBar("BLUnionTabs"))
        {
            if (ImGui.BeginTabItem("Party"))
            {
                this.DrawPartyTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Spell Comparison"))
            {
                this.DrawComparisonTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Sync"))
            {
                this.DrawSyncTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawPartyTab()
    {
        var members = this.partyService.GetBlueMagePartyMembers();

        if (members.Count == 0)
        {
            ImGui.TextWrapped("Keine Blue Mages in der aktuellen Party gefunden.");
            return;
        }

        foreach (var member in members)
            ImGui.TextUnformatted($"{member.Name}  (Level {member.Level})");
    }

    private void DrawComparisonTab()
    {
        if (this.lastError is not null)
        {
            ImGui.TextColored(new System.Numerics.Vector4(1, 0.4f, 0.4f, 1), this.lastError);
            ImGui.Separator();
        }

        var allSpellIds = this.spellDataService.Spells.Keys;
        var partyStatus = this.syncProvider.GetKnownPartyStatus();

        if (partyStatus.Count == 0)
        {
            ImGui.TextWrapped(
                "Noch keine Spielerdaten geladen. Gehe zum 'Sync'-Tab, um deinen eigenen " +
                "Status zu ermitteln und Codes von Mitspielern zu importieren.");
            return;
        }

        var missing = this.comparisonService.GetCommonlyMissingSpells(allSpellIds, partyStatus);

        ImGui.TextUnformatted("Gemeinsam fehlend:");
        ImGui.Separator();

        if (missing.Count == 0)
        {
            ImGui.TextWrapped("Alle bekannten Spells sind bei allen geladenen Spielern vorhanden.");
            return;
        }

        const ImGuiTableFlags tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp;

        if (ImGui.BeginTable("MissingSpellsTable", 3, tableFlags))
        {
            ImGui.TableSetupColumn("Spell");
            ImGui.TableSetupColumn("Fehlt bei", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("Quelle(n)");
            ImGui.TableHeadersRow();

            foreach (var entry in missing)
            {
                var name = this.spellDataService.Spells.TryGetValue(entry.SpellId, out var spell)
                    ? spell.Name
                    : $"Spell #{entry.SpellId}";

                var sources = this.spellDataService.GetSourcesForSpell(entry.SpellId).ToList();

                ImGui.TableNextRow();
                this.HighlightRowByUrgency(entry.PlayersMissingIt.Count, partyStatus.Count);

                ImGui.TableSetColumnIndex(0);
                ImGui.Selectable(name, false, ImGuiSelectableFlags.SpanAllColumns);

                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted("Fehlt bei: " + string.Join(", ", entry.PlayersMissingIt));

                    foreach (var (monster, location, method) in sources)
                        ImGui.TextUnformatted($"Quelle: {monster.Name} ({method}) — {FormatLocation(location)}");

                    ImGui.EndTooltip();
                }

                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted(entry.PlayersMissingIt.Count.ToString());

                ImGui.TableSetColumnIndex(2);
                ImGui.TextUnformatted(FormatSourceSummary(sources));
            }

            ImGui.EndTable();
        }
    }

    /// <summary>Färbt die aktuelle Tabellenzeile nach Dringlichkeit ein: rot/orange, wenn allen
    /// geladenen Spielern der Spell fehlt (höchste Priorität), gelb, wenn einer Mehrheit
    /// (&gt;50%) fehlt, sonst unverändert. Muss direkt nach ImGui.TableNextRow() aufgerufen werden.</summary>
    private void HighlightRowByUrgency(int playersMissingCount, int totalPlayerCount)
    {
        if (totalPlayerCount == 0)
            return;

        if (playersMissingCount == totalPlayerCount)
        {
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(new System.Numerics.Vector4(0.8f, 0.2f, 0.15f, 0.55f)));
        }
        else if (playersMissingCount > totalPlayerCount / 2.0)
        {
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(new System.Numerics.Vector4(0.85f, 0.75f, 0.15f, 0.45f)));
        }
    }

    /// <summary>Kurzer Anzeigetext für einen Fundort: Zone + Koordinaten (falls vorhanden),
    /// sonst Dungeon/Trial-Name (falls vorhanden), sonst nur die Zone.</summary>
    private static string FormatLocation(Location? location)
    {
        if (location is null)
            return "unbekannt";

        if (location.Coordinates is not null)
            return $"{location.ZoneName} ({location.Coordinates})";

        if (location.DutyName is not null)
            return location.DutyName;

        return location.ZoneName;
    }

    /// <summary>Kurze Quellen-Zusammenfassung für die Tabellenspalte; Details gibt's im Zeilen-Tooltip.</summary>
    private static string FormatSourceSummary(IReadOnlyList<(Monster Monster, Location? Location, string Method)> sources)
    {
        if (sources.Count == 0)
            return "unbekannt";

        if (sources.Count == 1)
        {
            var (monster, location, _) = sources[0];
            return $"{monster.Name} ({FormatLocation(location)})";
        }

        return $"{sources.Count} Quellen";
    }

    private void DrawSyncTab()
    {
        ImGui.TextWrapped(
            "Sync-Option A (MVP): Exportiere deinen eigenen Status als Code und teile ihn " +
            "z.B. über Discord. Mitspieler importieren ihn hier.");

        ImGui.Separator();

        if (ImGui.Button("Eigenen Status ermitteln + exportieren"))
        {
            try
            {
                this.lastError = null;
                var localPlayerName = this.partyService.GetPartyMembers()
                    .FirstOrDefault(m => m.IsBlueMage)?.Name ?? "Du";

                var status = this.localSpellUnlockService.GetLocalPlayerStatus(localPlayerName);
                this.syncProvider.PublishLocalStatus(status);
                var code = this.syncProvider.ExportToCode(status);
                ImGui.SetClipboardText(code);
                this.lastError = "Code in Zwischenablage kopiert.";
            }
            catch (Exception ex)
            {
                this.lastError = $"Fehler: {ex.Message}";
            }
        }

        ImGui.Separator();
        ImGui.InputText("Code eines Mitspielers", ref this.importCodeBuffer, 4096);

        if (ImGui.Button("Importieren"))
        {
            try
            {
                this.syncProvider.ImportCode(this.importCodeBuffer);
                this.lastError = null;
                this.importCodeBuffer = string.Empty;
            }
            catch (Exception ex)
            {
                this.lastError = $"Import fehlgeschlagen: {ex.Message}";
            }
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Aktuell geladene Spieler:");

        var knownStatus = this.syncProvider.GetKnownPartyStatus();

        if (knownStatus.Count == 0)
        {
            ImGui.TextWrapped("Noch keine Spielerdaten geladen.");
        }
        else
        {
            string? playerToRemove = null;

            foreach (var status in knownStatus)
            {
                ImGui.TextUnformatted(
                    $"{status.CharacterName} ({status.LearnedSpellIds.Count} Spells)" +
                    (status.IsLocalPlayer ? " — Du" : string.Empty));

                ImGui.SameLine();

                if (ImGui.Button($"Entfernen##{status.CharacterName}"))
                    playerToRemove = status.CharacterName;
            }

            if (playerToRemove is not null)
                this.syncProvider.RemovePlayer(playerToRemove);
        }

        // Dev-Tool, absichtlich dauerhaft hier (siehe Services/DevTestFixtures.cs) -
        // keine echte Party-Funktion, nur zum Testen von Comparison/Lernplan ohne
        // eine echte zweite Person in der Party zu brauchen.
        ImGui.Separator();
        ImGui.TextColored(
            new System.Numerics.Vector4(0.3f, 0.75f, 1f, 1),
            "Dev-Tool (keine echte Party-Funktion):");

        this.DrawDevFixtureButton("Dev: Alice laden", DevTestFixtures.CreateAlice);
        ImGui.SameLine();
        this.DrawDevFixtureButton("Dev: Bob laden", DevTestFixtures.CreateBob);
        ImGui.SameLine();
        this.DrawDevFixtureButton("Dev: Charles laden", DevTestFixtures.CreateCharles);
    }

    /// <summary>Lädt einen der Dev-Test-Charaktere aus <see cref="DevTestFixtures"/> und
    /// speist ihn wie einen echten Sync-Import in den syncProvider ein. Alle drei können
    /// einzeln oder gleichzeitig aktiviert werden, um unterschiedliche Spieleranzahlen im
    /// Comparison-Tab zu testen.</summary>
    private void DrawDevFixtureButton(string label, Func<SpellDataService, PlayerSpellStatus> createFixture)
    {
        if (ImGui.Button(label))
        {
            var fixture = createFixture(this.spellDataService);
            this.syncProvider.PublishLocalStatus(fixture);
            this.lastError = $"Dev-Tool: '{fixture.CharacterName}' mit {fixture.LearnedSpellIds.Count} Spells geladen.";
        }
    }
}
