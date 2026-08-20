using System.Diagnostics;
using BLUnion.Models;
using BLUnion.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Game;
using Dalamud.Game.Chat;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using EcChat = ECommons.Automation.Chat;

namespace BLUnion.UI;

/// <summary>
/// MVP-Fenster: deckt Punkt 11 (Phase 1) des Konzepts ab -
/// Party anzeigen, eigenen Status anzeigen, fehlende Spells + Fundort.
/// Party-Vergleich (Phase 2) und Lernplan (Phase 2/3) sind vorbereitet,
/// aber bewusst noch nicht der Fokus dieses Fensters.
///
/// Implementiert <see cref="IDisposable"/> (anders als die meisten Dalamud-<see cref="Window"/>-
/// Ableitungen) einzig wegen Feature 3 (siehe <see cref="autoImportAsPartyLeader"/>/
/// <see cref="OnChatMessage"/>): der dort registrierte Chat-Hook muss beim Entladen des Plugins
/// zuverlässig wieder abgemeldet werden, siehe <see cref="Dispose"/>.
/// </summary>
public sealed class MainWindow : Window, IDisposable
{
    private readonly PartyService partyService;
    private readonly SpellDataService spellDataService;
    private readonly ComparisonService comparisonService;
    private readonly LocalSpellUnlockService localSpellUnlockService;
    private readonly ManualCodeSyncProvider syncProvider;
    private readonly Configuration configuration;
    private readonly LiveSyncService liveSyncService;
    private readonly ITextureProvider textureProvider;
    private readonly IChatGui chatGui;
    private readonly IPluginLog log;

    /// <summary>Grüne Signalfarbe für reine Erfolgs-/Statusmeldungen (siehe <see cref="DrawLastMessage"/>) -
    /// EINE gemeinsame Konstante statt des Literals an mehreren Stellen, damit z.B. die
    /// Gruppenfinder-Sichtbarkeits-Bestätigung in <see cref="DrawGroupFinderTab"/> garantiert
    /// exakt dieselbe Farbe wie "Code in Zwischenablage kopiert." & Co. verwendet, nicht nur
    /// zufällig einen ähnlichen Farbwert.</summary>
    private static readonly System.Numerics.Vector4 SuccessMessageColor = new(0.3f, 0.85f, 0.4f, 1);

    /// <summary>Mindestabstand zwischen zwei automatischen Party-Chat-Posts desselben Spielers
    /// (Feature 2, siehe <see cref="TryAutoShareToPartyChat"/>) - verhindert Chat-Spam, wenn
    /// mehrfach kurz hintereinander auf "exportieren" geklickt wird, OHNE das sofortige
    /// Zwischenablage-Kopieren selbst zu verzögern (das läuft davon unabhängig bei jedem Klick).</summary>
    private static readonly TimeSpan AutoShareCooldown = TimeSpan.FromSeconds(10);

    /// <summary>URL des Web Companion (siehe DrawWebCompanionTab) - dieselbe Adresse wie im
    /// README-Abschnitt "Sync without a server" verlinkt.</summary>
    private const string WebCompanionUrl = "https://letsi-ma.github.io/BLUnion/";

    private string importCodeBuffer = string.Empty;
    private string comparisonFilterText = string.Empty;
    private string learningPlanFilterText = string.Empty;
    private string? lastError;

    /// <summary>Steuert NUR die Farbe, in der <see cref="lastError"/> angezeigt wird (siehe
    /// <see cref="DrawLastMessage"/>) - true für echte Fehler (rot), false für reine Erfolgs-/
    /// Statusmeldungen (grün). Bewusst als zweites Feld statt zwei getrennter string?-Felder:
    /// es gibt an jeder Stelle im Code ohnehin immer nur GENAU eine aktuelle Meldung, nie beide
    /// gleichzeitig - ein zusätzliches bool ist da einfacher als zwei Nullable-Strings synchron
    /// zu halten.</summary>
    private bool lastMessageIsError;

    /// <summary>Feature 2: wenn aktiviert (Default AN, siehe Konstruktor-Kommentar unten), wird
    /// der beim Klick auf "exportieren" erzeugte Sync-Code zusätzlich zum Zwischenablage-Kopieren
    /// automatisch per <see cref="TryAutoShareToPartyChat"/> in den Party-Chat gepostet. Wie
    /// <see cref="excludeTotems"/>/<see cref="displayLanguage"/> bewusst NICHT persistiert.</summary>
    private bool autoShareToPartyChat = true;

    /// <summary>Zeitpunkt des letzten automatischen Party-Chat-Posts (Feature 2) - null, solange
    /// noch nie automatisch gepostet wurde. Siehe <see cref="AutoShareCooldown"/>/
    /// <see cref="TryAutoShareToPartyChat"/>.</summary>
    private DateTimeOffset? lastAutoShareAt;

    /// <summary>Feature 3: wenn aktiviert, ist <see cref="OnChatMessage"/> an
    /// <see cref="IChatGui.ChatMessage"/> angemeldet und übernimmt automatisch jeden im Chat
    /// gefundenen "BLU:"-Sync-Code. Default AUS (anders als <see cref="autoShareToPartyChat"/>) -
    /// automatisches Übernehmen fremder Daten ist ein deutlich größerer Eingriff als automatisches
    /// Teilen der eigenen, das soll der Spieler bewusst erst aktivieren.</summary>
    private bool autoImportAsPartyLeader;

    /// <summary>"Totems ausblenden"-Filter (Comparison-/Lernplan-Tab, siehe DrawComparisonTab/
    /// DrawLearningPlanTab) - EIN gemeinsamer Zustand für beide Tabs, kein separater Toggle pro
    /// Tab. Default aus, wie gefordert.</summary>
    private bool excludeTotems;

    /// <summary>Sprache, in der aktuell die GESAMTE Oberfläche angezeigt wird (Spell-Namen über
    /// <see cref="GetSpellName"/>, alle sonstigen Texte über <see cref="UiStrings"/>). Nur
    /// In-Memory für die laufende Sitzung, wird bewusst nicht persistiert (siehe
    /// Aufgabenstellung zur Mehrsprachigkeit) - beim nächsten Fensteröffnen greift wieder der
    /// Default aus dem Konstruktor.</summary>
    private DisplayLanguage displayLanguage;

    /// <summary>Ob der Gruppenfinder-Tab im VORHERIGEN Frame aktiv war (siehe Draw()) - erkennt
    /// den Übergang "gerade erst geöffnet", um GENAU dann automatisch
    /// <see cref="LiveSyncService.TriggerBrowse"/> auszulösen (siehe Aufgabenstellung: "beim
    /// Öffnen des Tabs", NICHT bei jedem Draw-Call, während der Tab bereits offen ist).</summary>
    private bool groupFinderTabWasActive;

    /// <summary>True, sobald <see cref="groupFinderVisible"/>/<see cref="groupFinderTags"/>/
    /// <see cref="groupFinderNoteBuffer"/>/<see cref="groupFinderWantedPlayerCountBuffer"/>
    /// einmalig aus <see cref="LiveSyncService.LastKnownOwnProfile"/> vorbelegt wurden (siehe
    /// DrawGroupFinderTab) - NUR einmal pro Session, sonst würde ein weiterer automatischer
    /// Push (z.B. durch einen neu gelernten Spell mitten in der Bearbeitung) die gerade
    /// eingegebenen, noch nicht abgeschickten Änderungen wieder überschreiben.</summary>
    private bool groupFinderVisibilityInitialized;

    private bool groupFinderVisible;
    private HashSet<AvailabilityTag> groupFinderTags = new();
    private string groupFinderNoteBuffer = string.Empty;
    private string groupFinderWantedPlayerCountBuffer = "0";

    public MainWindow(
        PartyService partyService,
        SpellDataService spellDataService,
        ComparisonService comparisonService,
        LocalSpellUnlockService localSpellUnlockService,
        ManualCodeSyncProvider syncProvider,
        Configuration configuration,
        LiveSyncService liveSyncService,
        ITextureProvider textureProvider,
        IClientState clientState,
        IChatGui chatGui,
        IPluginLog log)
        : base("BLUnion###BLUnion")
    {
        this.partyService = partyService;
        this.spellDataService = spellDataService;
        this.comparisonService = comparisonService;
        this.localSpellUnlockService = localSpellUnlockService;
        this.syncProvider = syncProvider;
        this.configuration = configuration;
        this.liveSyncService = liveSyncService;
        this.textureProvider = textureProvider;
        this.chatGui = chatGui;
        this.log = log;

        // Default anhand der Client-Sprache vorbelegen, falls eine der 4 unterstützten -
        // ClientLanguage kennt aktuell ohnehin nur genau diese 4 Werte, der Fallback greift
        // also nur defensiv, falls Dalamud das Enum jemals erweitert.
        this.displayLanguage = clientState.ClientLanguage switch
        {
            ClientLanguage.German => DisplayLanguage.German,
            ClientLanguage.English => DisplayLanguage.English,
            ClientLanguage.French => DisplayLanguage.French,
            ClientLanguage.Japanese => DisplayLanguage.Japanese,
            _ => DisplayLanguage.English,
        };

        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new System.Numerics.Vector2(420, 300),
            MaximumSize = new System.Numerics.Vector2(1200, 1200),
        };

        // Ko-fi-Support-Button als Titelleisten-Icon, analog zu anderen Dalamud-Plugins (Herz
        // oben rechts neben dem Schließen-Button). Bewusst über die eingebaute
        // Window.TitleBarButtons-API (List<TitleBarButton>) statt selbst in Draw() gezeichnet -
        // TitleBarButton ist dabei KEIN verschachtelter Typ von Window, sondern der
        // eigenständige Dalamud.Interface.Windowing.TitleBarButton (per Reflection gegen die
        // installierte Dalamud.dll 15.0.3.2 verifiziert, siehe csproj-Kommentar zur API-Version).
        this.TitleBarButtons.Add(new TitleBarButton
        {
            Icon = FontAwesomeIcon.Heart,
            IconOffset = new System.Numerics.Vector2(2, 1),
            IconColor = new System.Numerics.Vector4(0.92f, 0.35f, 0.48f, 1f),
            Click = _ => Util.OpenLink("https://ko-fi.com/galderia"),
            ShowTooltip = () => ImGui.SetTooltip("Support on Ko-fi"),
        });
    }

    public override void Draw()
    {
        // Läuft JEDEN Frame, unabhängig vom aktuell sichtbaren Tab - Live-Sync-Push/-Fetch sollen
        // z.B. auch weiterlaufen, während der Party- oder Comparison-Tab offen ist, nicht nur bei
        // geöffnetem Sync-/Settings-Tab (siehe LiveSyncService.Tick-Doc). Rein zeitgesteuert/
        // lokal, nie blockierend - siehe dortigen Klassendoc zum Cross-Thread-Zugriff.
        this.liveSyncService.Tick();
        if (this.liveSyncService.TryTakePendingResult(out var liveSyncEventKind, out var liveSyncDetail))
            this.ApplyLiveSyncResult(liveSyncEventKind, liveSyncDetail);

        // WindowName trägt neben dem sichtbaren Titel (vor "###") auch die STABILE ImGui-Id
        // (nach "###") - die muss über Sprachwechsel hinweg gleich bleiben (sonst verliert ImGui
        // z.B. Fenstergröße/-position), nur der sichtbare Teil wird pro Frame neu übersetzt.
        this.WindowName = UiStrings.Get(UiStrings.Key.WindowTitle, this.displayLanguage) + "###BLUnion";

        if (ImGui.BeginTabBar("BLUnionTabs"))
        {
            // "###<stabile Id>"-Suffix an jedem Tab-Label ist hier Pflicht, nicht nur Stil: ImGui
            // leitet die interne Tab-Identität standardmäßig aus dem sichtbaren Label-Text ab.
            // Ohne den Suffix ändert sich bei jedem Sprachwechsel die ID ALLER Tabs gleichzeitig,
            // ImGui erkennt den bisher aktiven Tab dadurch nicht wieder und springt auf den
            // ersten zurück (siehe gemeldeter Bug: Sprung auf "Party" bei Sprachwechsel).
            if (ImGui.BeginTabItem(UiStrings.Get(UiStrings.Key.TabParty, this.displayLanguage) + "###TabParty"))
            {
                this.DrawPartyTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem(UiStrings.Get(UiStrings.Key.TabSpellComparison, this.displayLanguage) + "###TabSpellComparison"))
            {
                this.DrawComparisonTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem(UiStrings.Get(UiStrings.Key.TabLearningPlan, this.displayLanguage) + "###TabLearningPlan"))
            {
                this.DrawLearningPlanTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem(UiStrings.Get(UiStrings.Key.TabSync, this.displayLanguage) + "###TabSync"))
            {
                this.DrawSyncTab();
                ImGui.EndTabItem();
            }

            // Erkennung "Tab gerade erst geöffnet" (siehe groupFinderTabWasActive-Doc): der
            // BeginTabItem-Rückgabewert wird hier bewusst in eine lokale Variable statt direkt in
            // die if-Bedingung geschrieben, damit er nach dem Block noch zum Aktualisieren von
            // groupFinderTabWasActive zur Verfügung steht.
            var groupFinderTabActiveThisFrame = ImGui.BeginTabItem(UiStrings.Get(UiStrings.Key.TabGroupFinder, this.displayLanguage) + "###TabGroupFinder");
            if (groupFinderTabActiveThisFrame)
            {
                if (!this.groupFinderTabWasActive)
                    this.liveSyncService.TriggerBrowse();

                this.DrawGroupFinderTab();
                ImGui.EndTabItem();
            }

            this.groupFinderTabWasActive = groupFinderTabActiveThisFrame;

            if (ImGui.BeginTabItem(UiStrings.Get(UiStrings.Key.TabWebCompanion, this.displayLanguage) + "###TabWebCompanion"))
            {
                this.DrawWebCompanionTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem(UiStrings.Get(UiStrings.Key.TabSettings, this.displayLanguage) + "###TabSettings"))
            {
                this.DrawSettingsTab();
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
            ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.NoBlueMagesInParty, this.displayLanguage));
            return;
        }

        foreach (var member in members)
            ImGui.TextUnformatted(UiStrings.Format(UiStrings.Key.PartyMemberEntry, this.displayLanguage, member.Name, member.Level));
    }

    private void DrawComparisonTab()
    {
        this.DrawLastMessage();

        var allSpellIds = this.spellDataService.Spells.Keys;
        var partyStatus = this.syncProvider.GetKnownPartyStatus();

        if (partyStatus.Count == 0)
        {
            ImGui.TextWrapped(UiStrings.Format(
                UiStrings.Key.NoPlayerDataLoaded, this.displayLanguage, UiStrings.Get(UiStrings.Key.TabSync, this.displayLanguage)));
            return;
        }

        // Priorität 1 bleibt die "fehlt bei"-Anzahl (von GetCommonlyMissingSpells schon
        // absteigend sortiert); SpellbookOrder dient hier nur als stabiler, für Spieler
        // nachvollziehbarer Tie-Breaker innerhalb gleicher Dringlichkeit - deshalb hier in
        // der UI-Schicht nachsortiert statt im reinen ComparisonService, der bewusst keine
        // Spell-Metadaten kennt.
        var missing = this.comparisonService.GetCommonlyMissingSpells(allSpellIds, partyStatus)
            .OrderByDescending(m => m.PlayersMissingIt.Count)
            .ThenBy(m => this.spellDataService.Spells.TryGetValue(m.SpellId, out var s) ? s.SpellbookOrder : int.MaxValue)
            .ToList();

        ImGui.TextUnformatted(UiStrings.Get(UiStrings.Key.CommonlyMissingHeader, this.displayLanguage));
        ImGui.Separator();

        if (missing.Count == 0)
        {
            ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.AllSpellsKnownByAll, this.displayLanguage));
            return;
        }

        // Auflösen der Spell-Metadaten passiert VOR dem Filtern, da SpellFilter sowohl Name
        // als auch SpellbookOrder braucht. Der Filter selbst ändert nichts an obiger
        // Vergleichsberechnung/Sortierung, er blendet nur Zeilen der bereits fertigen
        // Ergebnisliste aus.
        var rows = missing.Select(entry =>
        {
            var hasSpell = this.spellDataService.Spells.TryGetValue(entry.SpellId, out var spell);
            return new
            {
                Entry = entry,
                Name = hasSpell ? this.GetSpellName(spell!) : UiStrings.Format(UiStrings.Key.SpellFallback, this.displayLanguage, entry.SpellId),
                SpellbookOrder = hasSpell ? spell!.SpellbookOrder : int.MaxValue,
                IconId = hasSpell ? spell!.IconId : 0u,
            };
        }).ToList();

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint(
            "##ComparisonFilter", UiStrings.Get(UiStrings.Key.SpellFilterHint, this.displayLanguage), ref this.comparisonFilterText, 128);
        ImGui.Checkbox(UiStrings.Get(UiStrings.Key.HideTotemsToggle, this.displayLanguage), ref this.excludeTotems);
        ImGui.Separator();

        // Totem-Filter blendet nur Spells aus, die NUR über ein Totem lernbar sind (siehe
        // SpellDataService.IsOnlyLearnableViaTotem) - Spells mit gemischten Quellen (Totem +
        // z.B. Open World) bleiben sichtbar, deren Tooltip/Quellenspalte zeigt dann weiterhin
        // die verbleibenden Nicht-Totem-Quellen (über den bereits gefilterten GetSourcesForSpell-
        // Aufruf weiter unten). Bewusst hier in der UI-Schicht gefiltert, nicht in
        // ComparisonService.GetCommonlyMissingSpells - die bleibt neutral/ungefiltert.
        var filteredRows = rows
            .Where(r => SpellFilter.Matches(r.Name, r.SpellbookOrder, this.comparisonFilterText))
            .Where(r => !this.excludeTotems || !this.spellDataService.IsOnlyLearnableViaTotem(r.Entry.SpellId))
            .ToList();

        const ImGuiTableFlags tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp;

        if (ImGui.BeginTable("MissingSpellsTable", 5, tableFlags))
        {
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 28);
            ImGui.TableSetupColumn(UiStrings.Get(UiStrings.Key.ColumnNumber, this.displayLanguage), ImGuiTableColumnFlags.WidthFixed, 40);
            ImGui.TableSetupColumn(UiStrings.Get(UiStrings.Key.ColumnSpell, this.displayLanguage));
            ImGui.TableSetupColumn(UiStrings.Get(UiStrings.Key.ColumnMissingFor, this.displayLanguage), ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn(UiStrings.Get(UiStrings.Key.ColumnSources, this.displayLanguage));
            ImGui.TableHeadersRow();

            foreach (var row in filteredRows)
            {
                var entry = row.Entry;
                var orderText = row.SpellbookOrder == int.MaxValue ? "—" : $"#{row.SpellbookOrder:D3}";
                var sources = this.spellDataService.GetSourcesForSpell(entry.SpellId, this.excludeTotems).ToList();

                ImGui.TableNextRow();
                this.HighlightRowByUrgency(entry.PlayersMissingIt.Count, partyStatus.Count);

                ImGui.TableSetColumnIndex(0);
                this.DrawSpellIcon(row.IconId);

                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted(orderText);

                ImGui.TableSetColumnIndex(2);
                ImGui.Selectable(row.Name, false, ImGuiSelectableFlags.SpanAllColumns);

                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted(UiStrings.Format(
                        UiStrings.Key.TooltipMissingFor, this.displayLanguage, string.Join(", ", entry.PlayersMissingIt)));

                    foreach (var (monster, location, method) in sources)
                    {
                        ImGui.TextUnformatted(UiStrings.Format(
                            UiStrings.Key.TooltipSourceLine, this.displayLanguage, this.GetMonsterName(monster), method.GetDisplayName(), this.FormatLocation(location)));
                    }

                    ImGui.EndTooltip();
                }

                ImGui.TableSetColumnIndex(3);
                ImGui.TextUnformatted(entry.PlayersMissingIt.Count.ToString());

                ImGui.TableSetColumnIndex(4);
                ImGui.TextUnformatted(this.FormatSourceSummary(sources));
            }

            ImGui.EndTable();
        }
    }

    /// <summary>Lernplan-Tab (Konzept-Punkt "ein Monster besuchen, mehrere Spells gleichzeitig
    /// lernen"): gruppiert die bereits berechneten fehlenden Spells nach Monster, über die
    /// bisher ungenutzte <see cref="ComparisonService.GroupMissingSpellsByMonster"/>. Zeigt nur
    /// Monster mit mindestens 2 abgedeckten fehlenden Spells - bei nur 1 Spell bringt die
    /// Gruppierung keinen Mehrwert gegenüber der normalen Comparison-Tabelle. Der Service
    /// selbst liefert bewusst alle Gruppen; die ≥2-Schwelle wird hier in der UI gefiltert.</summary>
    private void DrawLearningPlanTab()
    {
        var allSpellIds = this.spellDataService.Spells.Keys;
        var partyStatus = this.syncProvider.GetKnownPartyStatus();

        if (partyStatus.Count == 0)
        {
            ImGui.TextWrapped(UiStrings.Format(
                UiStrings.Key.NoPlayerDataLoaded, this.displayLanguage, UiStrings.Get(UiStrings.Key.TabSync, this.displayLanguage)));
            return;
        }

        var missing = this.comparisonService.GetCommonlyMissingSpells(allSpellIds, partyStatus);

        if (missing.Count == 0)
        {
            ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.AllSpellsKnownByAll, this.displayLanguage));
            return;
        }

        // Checkbox bewusst VOR GroupMissingSpellsByMonster gezeichnet (nicht erst weiter unten
        // bei den anderen Filterelementen) - sonst würde ein Klick erst im nächsten Frame
        // wirken, weil die Gruppierung mit dem noch alten excludeTotems-Wert berechnet worden
        // wäre. Analog zum Comparison-Tab, wo das Textfilter-Feld aus demselben Grund vor der
        // Filterung sitzt.
        ImGui.Checkbox(UiStrings.Get(UiStrings.Key.HideTotemsToggle, this.displayLanguage), ref this.excludeTotems);

        var groups = this.comparisonService.GroupMissingSpellsByMonster(missing, this.spellDataService, this.excludeTotems)
            .Where(g => g.CoveredMissingSpellIds.Count >= 2)
            .ToList();

        ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.LearnableAtMonstersHeader, this.displayLanguage));
        ImGui.Separator();

        if (groups.Count == 0)
        {
            ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.NoMonsterCoversTwoMissing, this.displayLanguage));
            return;
        }

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint(
            "##LearningPlanFilter",
            UiStrings.Get(UiStrings.Key.SpellFilterHint, this.displayLanguage),
            ref this.learningPlanFilterText,
            128);
        ImGui.Separator();

        foreach (var group in groups)
        {
            this.spellDataService.Monsters.TryGetValue(group.MonsterId, out var monster);
            var monsterName = monster is not null
                ? this.GetMonsterName(monster)
                : UiStrings.Format(UiStrings.Key.MonsterFallback, this.displayLanguage, group.MonsterId);

            Location? location = null;
            if (monster is not null)
                this.spellDataService.Locations.TryGetValue(monster.LocationId, out location);

            var spellRows = group.CoveredMissingSpellIds
                .Select(spellId =>
                {
                    var hasSpell = this.spellDataService.Spells.TryGetValue(spellId, out var spell);
                    return new
                    {
                        Name = hasSpell ? this.GetSpellName(spell!) : UiStrings.Format(UiStrings.Key.SpellFallback, this.displayLanguage, spellId),
                        SpellbookOrder = hasSpell ? spell!.SpellbookOrder : int.MaxValue,
                        IconId = hasSpell ? spell!.IconId : 0u,
                    };
                })
                .Where(r => SpellFilter.Matches(r.Name, r.SpellbookOrder, this.learningPlanFilterText))
                .OrderBy(r => r.SpellbookOrder)
                .ToList();

            // Aktiver Filter kann eine Gruppe komplett leer ziehen - dann die ganze
            // Monster-Zeile ausblenden statt eine leere Liste anzuzeigen.
            if (spellRows.Count == 0)
                continue;

            ImGui.TextUnformatted($"{monsterName} — {this.FormatLocation(location)}");
            ImGui.TextWrapped(UiStrings.Format(UiStrings.Key.LearnableAtMonsterCount, this.displayLanguage, spellRows.Count));

            foreach (var row in spellRows)
            {
                var orderText = row.SpellbookOrder == int.MaxValue ? "—" : $"#{row.SpellbookOrder:D3}";

                this.DrawSpellIcon(row.IconId);
                ImGui.SameLine();
                ImGui.TextUnformatted($"{orderText}  {row.Name}");
            }

            ImGui.Separator();
        }
    }

    /// <summary>Rendert ein 24x24-Spell-Icon aus den lokalen Spieldateien über
    /// <see cref="ITextureProvider"/>. Wenn keine IconId bekannt ist, das Icon (noch) nicht
    /// geladen werden konnte oder das Laden fehlschlägt, wird stattdessen nur ein leerer
    /// Platzhalter derselben Größe gezeichnet - nie eine Exception nach außen geworfen, die
    /// die gesamte Tabelle zum Absturz bringen würde.</summary>
    private void DrawSpellIcon(uint iconId)
    {
        var size = new System.Numerics.Vector2(24, 24);

        if (iconId != 0)
        {
            try
            {
                var texture = this.textureProvider.GetFromGameIcon(new GameIconLookup(iconId));
                if (texture.TryGetWrap(out var wrap, out _))
                {
                    ImGui.Image(wrap.Handle, size);
                    return;
                }
            }
            catch
            {
                // Icon konnte nicht geladen werden (z.B. ungültige/unbekannte Id) -
                // Zeile trotzdem ohne Icon anzeigen, siehe Doc-Kommentar oben.
            }
        }

        ImGui.Dummy(size);
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
    /// sonst Dungeon/Trial-Name (falls vorhanden), sonst nur die Zone. Zonenname folgt
    /// <see cref="displayLanguage"/>; <see cref="Location.DutyName"/> ist bewusst einsprachig
    /// (nicht Teil dieser Aufgabe, siehe Models/Location.cs).</summary>
    private string FormatLocation(Location? location)
    {
        if (location is null)
            return UiStrings.Get(UiStrings.Key.UnknownLocation, this.displayLanguage);

        var zoneName = location.GetZoneName(this.displayLanguage);

        if (location.Coordinates is not null)
            return $"{zoneName} ({location.Coordinates})";

        if (location.DutyName is not null)
            return location.DutyName;

        return zoneName;
    }

    /// <summary>Kurze Quellen-Zusammenfassung für die Tabellenspalte; Details gibt's im Zeilen-Tooltip.</summary>
    private string FormatSourceSummary(IReadOnlyList<(Monster Monster, Location? Location, SourceMethod Method)> sources)
    {
        if (sources.Count == 0)
            return UiStrings.Get(UiStrings.Key.UnknownLocation, this.displayLanguage);

        if (sources.Count == 1)
        {
            var (monster, location, _) = sources[0];
            return $"{this.GetMonsterName(monster)} ({this.FormatLocation(location)})";
        }

        return UiStrings.Format(UiStrings.Key.SourceCountSummary, this.displayLanguage, sources.Count);
    }

    private void DrawSyncTab()
    {
        // Zeigt insbesondere die Erfolgsmeldung nach dem Export-Button an (siehe unten) - vorher
        // stand dieser Aufruf hier NICHT, wodurch "Code in Zwischenablage kopiert." erst sichtbar
        // wurde, wenn man danach zufällig in den Comparison- oder Web-Companion-Tab wechselte
        // (die einzigen beiden Tabs, die lastError bisher anzeigten). Jetzt konsistent wie dort.
        this.DrawLastMessage();

        ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.SyncIntro, this.displayLanguage));

        ImGui.Separator();

        if (ImGui.Button(UiStrings.Get(UiStrings.Key.DetermineAndExportButton, this.displayLanguage)))
        {
            try
            {
                // Bewusst direkt der eigene Charaktername (nicht länger "erster Blue Mage in
                // der Party") - siehe Doc-Kommentar an PartyService.GetLocalPlayerName().
                var localPlayerName = this.partyService.GetLocalPlayerName()
                    ?? UiStrings.Get(UiStrings.Key.LocalPlayerFallbackName, this.displayLanguage);

                var status = this.localSpellUnlockService.GetLocalPlayerStatus(localPlayerName);
                this.syncProvider.PublishLocalStatus(status);
                var code = this.syncProvider.ExportToCode(status);

                // Zwischenablage-Kopie passiert IMMER sofort bei jedem Klick, unabhängig vom
                // automatischen Party-Chat-Post weiter unten (der kann wegen Cooldown/fehlender
                // Party übersprungen werden) - siehe Aufgabenstellung zu Feature 2.
                ImGui.SetClipboardText(code);

                var sharedToPartyChat = this.TryAutoShareToPartyChat(code);
                this.SetSuccessMessage(sharedToPartyChat
                    ? UiStrings.Get(UiStrings.Key.ClipboardCopiedAndSharedMessage, this.displayLanguage)
                    : UiStrings.Get(UiStrings.Key.ClipboardCopiedMessage, this.displayLanguage));
            }
            catch (Exception ex)
            {
                this.SetErrorMessage(UiStrings.Format(UiStrings.Key.GenericError, this.displayLanguage, ex.Message));
            }
        }

        ImGui.Separator();
        ImGui.InputText(UiStrings.Get(UiStrings.Key.ImportCodeLabel, this.displayLanguage), ref this.importCodeBuffer, 4096);

        if (ImGui.Button(UiStrings.Get(UiStrings.Key.ImportButton, this.displayLanguage)))
        {
            try
            {
                this.syncProvider.ImportCode(this.importCodeBuffer);
                this.ClearMessage();
                this.importCodeBuffer = string.Empty;
            }
            catch (Exception ex)
            {
                this.SetErrorMessage(UiStrings.Format(UiStrings.Key.ImportFailed, this.displayLanguage, ex.Message));
            }
        }

        ImGui.Separator();

        // Feature 3: An-/Abmelden des Chat-Hooks passiert NUR im Moment der tatsächlichen
        // Änderung (ImGui.Checkbox liefert true nur im Frame des Klicks, ref-Wert ist zu diesem
        // Zeitpunkt schon aktualisiert) - kein +=/-= bei jedem Frame, siehe OnChatMessage.
        if (ImGui.Checkbox(UiStrings.Get(UiStrings.Key.AutoImportAsLeaderToggle, this.displayLanguage), ref this.autoImportAsPartyLeader))
        {
            if (this.autoImportAsPartyLeader)
                this.chatGui.ChatMessage += this.OnChatMessage;
            else
                this.chatGui.ChatMessage -= this.OnChatMessage;
        }

        ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.AutoImportAsLeaderHint, this.displayLanguage));

        ImGui.Separator();
        ImGui.TextUnformatted(UiStrings.Get(UiStrings.Key.CurrentlyLoadedPlayersHeader, this.displayLanguage));

        var knownStatus = this.syncProvider.GetKnownPartyStatus();

        if (knownStatus.Count == 0)
        {
            ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.NoPlayerDataLoadedShort, this.displayLanguage));
        }
        else
        {
            string? playerToRemove = null;

            foreach (var status in knownStatus)
            {
                var line = UiStrings.Format(
                    UiStrings.Key.PlayerSpellCount, this.displayLanguage, status.CharacterName, status.LearnedSpellIds.Count);

                if (status.IsLocalPlayer)
                    line += UiStrings.Get(UiStrings.Key.YouSuffix, this.displayLanguage);

                ImGui.TextUnformatted(line);

                ImGui.SameLine();

                if (ImGui.Button($"{UiStrings.Get(UiStrings.Key.RemoveButton, this.displayLanguage)}##{status.CharacterName}"))
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
            UiStrings.Get(UiStrings.Key.DevToolHeader, this.displayLanguage));

        this.DrawDevFixtureButton(UiStrings.Get(UiStrings.Key.DevLoadAliceButton, this.displayLanguage), DevTestFixtures.CreateAlice);
        ImGui.SameLine();
        this.DrawDevFixtureButton(UiStrings.Get(UiStrings.Key.DevLoadBobButton, this.displayLanguage), DevTestFixtures.CreateBob);
        ImGui.SameLine();
        this.DrawDevFixtureButton(UiStrings.Get(UiStrings.Key.DevLoadCharlesButton, this.displayLanguage), DevTestFixtures.CreateCharles);
        ImGui.SameLine();

        // Wie die drei Buttons oben unkonditioniert sichtbar (siehe Kommentar über diesem
        // Dev-Tool-Abschnitt: "absichtlich dauerhaft hier", kein #if DEBUG/Konfigurationsflag in
        // diesem Projekt) - veröffentlicht dieselben drei Fixtures zusätzlich als ECHTE, im
        // Gruppenfinder sichtbare Testprofile beim Live-Sync-Worker (siehe
        // LiveSyncService.PublishDevTestProfiles), um Phase 2 (Browse/"In Vergleich aufnehmen")
        // ohne einen dritten echten Mitspieler testen zu können.
        if (ImGui.Button(UiStrings.Get(UiStrings.Key.DevPublishTestProfilesButton, this.displayLanguage)))
            this.liveSyncService.PublishDevTestProfiles();
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
            this.SetSuccessMessage(UiStrings.Format(
                UiStrings.Key.DevFixtureLoaded, this.displayLanguage, fixture.CharacterName, fixture.LearnedSpellIds.Count));
        }
    }

    /// <summary>Zeigt <see cref="lastError"/> an, falls gesetzt - Rot für echte Fehler, Grün
    /// (Signalfarbe) für reine Erfolgs-/Statusmeldungen (siehe <see cref="lastMessageIsError"/>).
    /// Zentral hier statt an jeder Anzeigestelle dupliziert (Sync-, Comparison- und Web-Companion-
    /// Tab teilen sich alle dieselbe lastError-Anzeige).</summary>
    private void DrawLastMessage()
    {
        if (this.lastError is null)
            return;

        var color = this.lastMessageIsError
            ? new System.Numerics.Vector4(1, 0.4f, 0.4f, 1)
            : SuccessMessageColor;

        ImGui.TextColored(color, this.lastError);
        ImGui.Separator();
    }

    /// <summary>Setzt eine reine Erfolgs-/Statusmeldung (wird grün angezeigt, siehe
    /// <see cref="DrawLastMessage"/>) - für alles, was KEIN Fehler ist (Zwischenablage kopiert,
    /// Link kopiert, Browser geöffnet, Dev-Fixture geladen, automatischer Import).</summary>
    private void SetSuccessMessage(string message)
    {
        this.lastError = message;
        this.lastMessageIsError = false;
    }

    /// <summary>Setzt eine echte Fehlermeldung (wird rot angezeigt, siehe
    /// <see cref="DrawLastMessage"/>) - für alles aus einem catch-Block bzw. sonstige
    /// tatsächliche Fehlschläge (kaputter Import-Code, Exception beim Export/Browser-Öffnen).</summary>
    private void SetErrorMessage(string message)
    {
        this.lastError = message;
        this.lastMessageIsError = true;
    }

    /// <summary>Blendet die aktuelle Meldung wieder aus, ohne eine neue zu setzen (z.B. nach
    /// erfolgreichem manuellem Import, der bewusst keine eigene Erfolgsmeldung zeigt).</summary>
    private void ClearMessage() => this.lastError = null;

    /// <summary>Übersetzt ein von <see cref="LiveSyncService.TryTakePendingResult"/> geliefertes
    /// Ergebnis in eine lokalisierte Meldung über das bestehende SetSuccessMessage/SetErrorMessage-
    /// Muster (siehe Aufgabenstellung: "konsistent über das bestehende lastMessageIsError-Muster").
    /// <paramref name="detail"/> ist bewusst unlokalisiert (HTTP-Status/Exception-Text vom Server/
    /// Netzwerk) - wird nur bei Fehlern über UiStrings.Key.GenericError-artige Format-Keys mit
    /// eingesetzt, analog zu den bestehenden catch-Blöcken in DrawSyncTab.</summary>
    private void ApplyLiveSyncResult(LiveSyncEventKind kind, string? detail)
    {
        switch (kind)
        {
            case LiveSyncEventKind.PushSucceeded:
                this.SetSuccessMessage(UiStrings.Get(UiStrings.Key.LiveSyncPushSucceeded, this.displayLanguage));
                break;
            case LiveSyncEventKind.PushFailed:
                this.SetErrorMessage(UiStrings.Format(UiStrings.Key.LiveSyncPushFailed, this.displayLanguage, detail ?? "?"));
                break;
            case LiveSyncEventKind.FetchFailed:
                this.SetErrorMessage(UiStrings.Format(UiStrings.Key.LiveSyncFetchFailed, this.displayLanguage, detail ?? "?"));
                break;
            case LiveSyncEventKind.DeleteSucceeded:
                this.SetSuccessMessage(UiStrings.Get(UiStrings.Key.LiveSyncDeleteSucceeded, this.displayLanguage));
                break;
            case LiveSyncEventKind.DeleteFailed:
                this.SetErrorMessage(UiStrings.Format(UiStrings.Key.LiveSyncDeleteFailed, this.displayLanguage, detail ?? "?"));
                break;
            case LiveSyncEventKind.BrowseFailed:
                this.SetErrorMessage(UiStrings.Format(UiStrings.Key.LiveSyncBrowseFailed, this.displayLanguage, detail ?? "?"));
                break;
            case LiveSyncEventKind.DevTestProfilesPublished:
                this.SetSuccessMessage(UiStrings.Format(UiStrings.Key.DevTestProfilesPublished, this.displayLanguage, detail ?? "?"));
                break;
            case LiveSyncEventKind.DevTestProfilesFailed:
                this.SetErrorMessage(UiStrings.Format(UiStrings.Key.DevTestProfilesFailed, this.displayLanguage, detail ?? "?"));
                break;
        }
    }

    /// <summary>Feature 2: postet <paramref name="code"/> automatisch als "/p "-Chatnachricht,
    /// falls <see cref="autoShareToPartyChat"/> aktiviert ist, der Spieler aktuell in einer Party
    /// ist (<see cref="PartyService.IsInParty"/>) und seit dem letzten automatischen Post
    /// mindestens <see cref="AutoShareCooldown"/> vergangen ist. Liefert true, wenn tatsächlich
    /// gepostet wurde (der Aufrufer wählt danach die passende Erfolgsmeldung) - das Zwischenablage-
    /// Kopieren selbst läuft in DrawSyncTab komplett unabhängig davon weiter, auch wenn hier
    /// übersprungen wird.
    ///
    /// Cooldown statt z.B. den Button zu deaktivieren: mehrfaches Klicken soll weiterhin sofort
    /// wieder in die Zwischenablage kopieren (z.B. nachdem man versehentlich woanders hin
    /// geklickt hat und die Zwischenablage überschrieben wurde) - nur der Chat-Post selbst soll
    /// nicht bei jedem Klick erneut spammen.</summary>
    private bool TryAutoShareToPartyChat(string code)
    {
        if (!this.autoShareToPartyChat || !this.partyService.IsInParty)
            return false;

        var now = DateTimeOffset.UtcNow;
        if (this.lastAutoShareAt is { } lastShare && now - lastShare < AutoShareCooldown)
            return false;

        EcChat.SendMessage("/p " + code);
        this.lastAutoShareAt = now;
        return true;
    }

    /// <summary>Feature 3: Handler für <see cref="IChatGui.ChatMessage"/>, nur angemeldet
    /// während <see cref="autoImportAsPartyLeader"/> aktiviert ist (siehe DrawSyncTab). Deckt
    /// BEWUSST alle Chat-Kanäle/-Typen ab (kein Filtern nach XivChatType) - Sync-Codes können in
    /// jedem Kanal gepostet werden, nicht nur im Party-Chat, und Spieler schreiben oft noch Text
    /// drumherum, daher die Suche nach dem Teilstring "BLU:" statt einem Vergleich der kompletten
    /// Nachricht.</summary>
    private void OnChatMessage(IHandleableChatMessage message)
    {
        var senderName = message.Sender.TextValue;

        // Eigene, selbst gesendete Codes NICHT re-importieren - sonst würde der eigene Eintrag
        // durch den automatischen Import mit IsLocalPlayer=false überschrieben (siehe
        // ManualCodeSyncProvider.ImportCode) und das "(Du)"-Suffix ginge verloren. Vergleich des
        // Chat-Absenders VOR dem Import-Versuch ist einfacher, als den Namen erst aus dem
        // decodierten Code selbst zu extrahieren - genau dieser einfachere Weg wurde hier bewusst
        // gewählt (siehe Aufgabenstellung).
        var localPlayerName = this.partyService.GetLocalPlayerName();
        if (localPlayerName is not null && string.Equals(senderName, localPlayerName, StringComparison.Ordinal))
            return;

        var text = message.Message.TextValue;
        var codeStart = text.IndexOf(ManualCodeSyncProvider.CurrentPrefix, StringComparison.Ordinal);
        if (codeStart < 0)
            return;

        // Ab dem gefundenen "BLU:" bis zum nächsten Whitespace (oder Nachrichtenende) extrahieren -
        // der Code kann irgendwo mitten in einer sonst frei formulierten Chatnachricht stehen.
        var codeEnd = codeStart;
        while (codeEnd < text.Length && !char.IsWhiteSpace(text[codeEnd]))
            codeEnd++;

        var code = text[codeStart..codeEnd];

        try
        {
            this.syncProvider.ImportCode(code);

            // Die Dictionary-basierte Ablage in ManualCodeSyncProvider (known[CharacterName] = ...)
            // überschreibt bei wiederholtem Import automatisch denselben Eintrag statt einen
            // zweiten anzulegen - mehrfaches Posten/Lesen desselben Codes erzeugt hier bewusst
            // KEINE separate Historie, nur diese eine, flüchtige Erfolgsmeldung.
            this.SetSuccessMessage(UiStrings.Format(UiStrings.Key.AutoImportedMessage, this.displayLanguage, senderName));
        }
        catch (Exception ex)
        {
            // Bewusst NICHT als UI-Fehlermeldung angezeigt (würde bei jedem kaputten/fremden
            // "BLU:"-Vorkommen im Chat-Rauschen nerven, siehe Aufgabenstellung) - nur geloggt.
            this.log.Debug(ex, $"Automatischer Sync-Code-Import fehlgeschlagen (Absender \"{senderName}\").");
        }
    }

    /// <summary>Phase 2 "Live-Sync": öffentlicher Gruppenfinder. KEIN separates Profil/Login
    /// (siehe Aufgabenstellung) - erweitert nur das bestehende Live-Sync-Profil um Sichtbarkeit/
    /// Verfügbarkeit/Notiz/gewünschte Mitspieleranzahl, daher die Sperre auf
    /// <see cref="Configuration.LiveSyncEnabled"/> gleich zu Beginn.</summary>
    private void DrawGroupFinderTab()
    {
        this.DrawLastMessage();

        if (!this.configuration.LiveSyncEnabled)
        {
            ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.GroupFinderInactiveHint, this.displayLanguage));

            // Kein echter Tab-Wechsel per Code (siehe Aufgabenstellung: "oder zumindest sagt, wo
            // die zu finden ist") - zeigt stattdessen bewusst nur einen Hinweis über das
            // bestehende Meldungs-System, statt fragil in die interne ImGui-Tab-Auswahl
            // einzugreifen.
            if (ImGui.Button(UiStrings.Get(UiStrings.Key.GroupFinderGoToSettingsButton, this.displayLanguage)))
                this.SetSuccessMessage(UiStrings.Get(UiStrings.Key.GroupFinderGoToSettingsMessage, this.displayLanguage));

            return;
        }

        // Einmalige Vorbelegung der lokalen Eingabepuffer aus dem zuletzt vom Server bestätigten
        // Stand (siehe groupFinderVisibilityInitialized-Doc) - VOR dem allerersten Push in dieser
        // Session ist LastKnownOwnProfile noch null, dann bleiben die Puffer-Defaults (unsichtbar,
        // keine Tags, leere Notiz, 0) stehen, bis der erste automatische Push abgeschlossen ist.
        if (!this.groupFinderVisibilityInitialized && this.liveSyncService.LastKnownOwnProfile is { } ownProfile)
        {
            this.groupFinderVisible = ownProfile.VisibleInGroupFinder;
            this.groupFinderTags = new HashSet<AvailabilityTag>(ownProfile.AvailabilityTags);
            this.groupFinderNoteBuffer = ownProfile.Note;
            this.groupFinderWantedPlayerCountBuffer = ownProfile.WantedPlayerCount.ToString();
            this.groupFinderVisibilityInitialized = true;
        }

        ImGui.TextUnformatted(UiStrings.Get(UiStrings.Key.GroupFinderMyEntryHeader, this.displayLanguage));
        ImGui.Separator();

        // Checkbox pusht SOFORT bei Klick (siehe LiveSyncService.SetGroupFinderVisibility-Doc) -
        // kein Debounce, anders als bei den Textfeldern weiter unten.
        if (ImGui.Checkbox(UiStrings.Get(UiStrings.Key.GroupFinderVisibleToggle, this.displayLanguage), ref this.groupFinderVisible))
            this.liveSyncService.SetGroupFinderVisibility(this.groupFinderVisible);

        // Fünf anklickbare Tags, Mehrfachauswahl möglich (siehe Aufgabenstellung) - jeder Klick
        // pusht sofort den kompletten aktuellen Auswahlstand (Toggle-Buttons, keine
        // Tastatureingabe, daher kein Debounce - siehe LiveSyncService.SetGroupFinderAvailabilityTags-Doc).
        // Enum.GetValues<T>() liefert die Werte in Deklarationsreihenfolge (dasselbe Muster wie
        // bei den Sprach-Radio-Buttons in DrawSettingsTab) - stabile, vorhersehbare Anzeigereihenfolge.
        foreach (var tag in Enum.GetValues<AvailabilityTag>())
        {
            var selected = this.groupFinderTags.Contains(tag);
            if (ImGui.Checkbox($"{UiStrings.Get(GetAvailabilityTagLabelKey(tag), this.displayLanguage)}##GroupFinderTag{tag}", ref selected))
            {
                if (selected)
                    this.groupFinderTags.Add(tag);
                else
                    this.groupFinderTags.Remove(tag);

                this.liveSyncService.SetGroupFinderAvailabilityTags(this.groupFinderTags);
            }

            ImGui.SameLine();
        }

        ImGui.NewLine();

        // Notiz UND Mitspieleranzahl werden NICHT bei jedem Tastendruck gepusht, sondern erst bei
        // Fokusverlust (siehe Aufgabenstellung, analoges Debounce-Denken wie beim
        // Auto-Share-Cooldown aus Feature 2) - IsItemDeactivatedAfterEdit() feuert genau einmal,
        // im Frame des Fokusverlusts NACH einer tatsächlichen Änderung (kein Push bei Fokusverlust
        // ohne Änderung).
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText(UiStrings.Get(UiStrings.Key.GroupFinderNoteLabel, this.displayLanguage), ref this.groupFinderNoteBuffer, 60);
        var notePushNeeded = ImGui.IsItemDeactivatedAfterEdit();

        // ImGuiInputTextFlags.CharsDecimal filtert bereits die meisten Nicht-Ziffern beim Tippen
        // heraus (siehe Aufgabenstellung "nur Ziffern akzeptieren") - der anschließende
        // int.TryParse-Fallback unten fängt den ImGui-seitig weiterhin erlaubten Rest ('.', '+',
        // '-') zusätzlich ab, damit daraus nie ein für den Worker ungültiger Wert gepusht wird
        // (siehe worker/src/index.ts isValidWantedPlayerCount: lehnt Nicht-Ganzzahlen mit 400 ab).
        ImGui.SetNextItemWidth(80);
        ImGui.InputText(
            UiStrings.Get(UiStrings.Key.GroupFinderWantedPlayerCountLabel, this.displayLanguage),
            ref this.groupFinderWantedPlayerCountBuffer, 2, ImGuiInputTextFlags.CharsDecimal);
        var wantedPlayerCountPushNeeded = ImGui.IsItemDeactivatedAfterEdit();

        if (notePushNeeded || wantedPlayerCountPushNeeded)
        {
            if (!int.TryParse(this.groupFinderWantedPlayerCountBuffer, out var wantedPlayerCount))
                wantedPlayerCount = 0;

            wantedPlayerCount = Math.Clamp(wantedPlayerCount, 0, 8);
            this.groupFinderWantedPlayerCountBuffer = wantedPlayerCount.ToString();

            this.liveSyncService.SetGroupFinderNoteAndWantedPlayerCount(this.groupFinderNoteBuffer, wantedPlayerCount);
        }

        // Sichtbare Bestätigung, dass "Im Gruppenfinder sichtbar" tatsächlich funktioniert hat -
        // ohne diese Zeile hätte der Nutzer sonst keine Möglichkeit, das zu sehen, weil der
        // eigene Eintrag bewusst aus der "Andere Spieler"-Liste weiter unten herausgefiltert wird
        // (siehe dort). Zeigt bewusst den zuletzt vom WORKER bestätigten Stand
        // (LastKnownOwnProfile, aus der Push-Response) statt der ggf. noch ungespeicherten
        // Eingabefelder oben - eine Checkbox/ein Tag kann angeklickt sein, während der zugehörige
        // Push noch unterwegs oder fehlgeschlagen ist. Dieselbe grüne Signalfarbe wie
        // DrawLastMessage (siehe SuccessMessageColor-Doc) - KEINE neue Farbe definiert.
        if (this.liveSyncService.LastKnownOwnProfile is { VisibleInGroupFinder: true } confirmedProfile)
        {
            var tagsText = confirmedProfile.AvailabilityTags.Count > 0
                ? string.Join(", ", confirmedProfile.AvailabilityTags.Select(tag => UiStrings.Get(GetAvailabilityTagLabelKey(tag), this.displayLanguage)))
                : "–";
            var noteText = string.IsNullOrEmpty(confirmedProfile.Note) ? "–" : $"\"{confirmedProfile.Note}\"";
            var wantedPlayerCountText = confirmedProfile.WantedPlayerCount == 0
                ? UiStrings.Get(UiStrings.Key.GroupFinderWantedPlayerCountAny, this.displayLanguage)
                : confirmedProfile.WantedPlayerCount.ToString();

            ImGui.TextColored(SuccessMessageColor, UiStrings.Format(
                UiStrings.Key.GroupFinderOwnVisibleConfirmation, this.displayLanguage, tagsText, noteText, wantedPlayerCountText));
        }

        ImGui.Separator();

        // Data Center kommt AUSSCHLIESSLICH aus der zuletzt bekannten eigenen Profil-Antwort
        // (siehe LiveSyncService.LastKnownOwnProfile-Doc), NICHT erneut lokal aus der World
        // hergeleitet (siehe Aufgabenstellung: würde die World->DC-Zuordnung aus
        // worker/src/worlds.ts im C#-Code duplizieren). Vor dem allerersten Push ist das noch
        // unbekannt - dann Platzhalter statt eines leeren/falschen Zustands.
        var dataCenter = this.liveSyncService.LastKnownOwnProfile?.DataCenter;
        if (dataCenter is null)
        {
            ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.GroupFinderDeterminingDataCenter, this.displayLanguage));
            return;
        }

        ImGui.TextUnformatted(UiStrings.Format(UiStrings.Key.GroupFinderOthersHeader, this.displayLanguage, dataCenter));
        ImGui.SameLine();

        // Manueller Refresh zusätzlich zum automatischen Abruf beim Öffnen des Tabs (siehe
        // Draw()/groupFinderTabWasActive) - beides zusammen deckt "nicht bei jedem Draw-Call"
        // aus der Aufgabenstellung ab.
        if (ImGui.Button(UiStrings.Get(UiStrings.Key.GroupFinderRefreshButton, this.displayLanguage)))
            this.liveSyncService.TriggerBrowse();

        ImGui.Separator();

        // Eigenfilterung ("Andere Spieler" soll wirklich nur ANDERE zeigen) passiert BEWUSST erst
        // hier beim Rendern, nicht in LiveSyncService.TriggerBrowseAsync: dort läuft der Code nach
        // dem await auf einem Threadpool-Thread, und this.partyService.GetLocalPlayerName() ist
        // eine Dalamud-Service-API (IObjectTable) mit Thread-Affinität - ein früherer Versuch,
        // dort zu filtern, führte zur Laufzeit-Exception "Not on main thread!". Hier in Draw()
        // läuft der Code garantiert auf dem Framework-Thread (siehe MainWindow.Draw()), der
        // Zugriff auf partyService ist also unproblematisch. LiveSyncService.LastBrowseResults
        // liefert deshalb absichtlich die ungefilterten Rohdaten (siehe dortiger Doc-Kommentar).
        var localPlayerName = this.partyService.GetLocalPlayerName();
        var entries = this.liveSyncService.LastBrowseResults
            .Where(entry => !string.Equals(entry.CharacterName, localPlayerName, StringComparison.Ordinal))
            .ToList();

        if (entries.Count == 0)
        {
            ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.GroupFinderNoEntries, this.displayLanguage));
            return;
        }

        var totalSpellCount = this.spellDataService.Spells.Count;

        foreach (var entry in entries)
        {
            ImGui.TextUnformatted($"{entry.CharacterName} ({entry.World})");
            ImGui.SameLine();
            ImGui.TextUnformatted(UiStrings.Format(
                UiStrings.Key.GroupFinderProgressFormat, this.displayLanguage, entry.LearnedSpellIds.Count, totalSpellCount));

            if (entry.AvailabilityTags.Count > 0)
            {
                var tagLabels = entry.AvailabilityTags.Select(tag => UiStrings.Get(GetAvailabilityTagLabelKey(tag), this.displayLanguage));
                ImGui.TextUnformatted(string.Join(", ", tagLabels));
            }

            if (!string.IsNullOrEmpty(entry.Note))
                ImGui.TextWrapped(entry.Note);

            var wantedPlayerCountText = entry.WantedPlayerCount == 0
                ? UiStrings.Get(UiStrings.Key.GroupFinderWantedPlayerCountAny, this.displayLanguage)
                : entry.WantedPlayerCount.ToString();
            ImGui.TextUnformatted(UiStrings.Format(UiStrings.Key.GroupFinderWantedPlayerCountEntryFormat, this.displayLanguage, wantedPlayerCountText));

            if (ImGui.Button($"{UiStrings.Get(UiStrings.Key.GroupFinderAddToComparisonButton, this.displayLanguage)}##GroupFinderAdd{entry.CharacterName}"))
            {
                var status = new PlayerSpellStatus
                {
                    CharacterName = entry.CharacterName,
                    LearnedSpellIds = entry.LearnedSpellIds,
                    IsLocalPlayer = false,
                };

                // Dieselbe Merge-/Dedup-Logik wie beim Live-Sync-Party-Fetch (siehe
                // LiveSyncService.FetchPartyMemberProfilesAsync) - bewusst hier direkt über
                // syncProvider.PublishLocalStatus wiederverwendet statt eines zweiten,
                // eigenständigen Merge-Pfads, damit Party-Auto-Sync und Gruppenfinder-Funde im
                // selben Comparison-Tab-Datenbestand zusammenlaufen und sich bei gleichem Namen
                // nicht duplizieren.
                this.syncProvider.PublishLocalStatus(status);
                this.SetSuccessMessage(UiStrings.Format(UiStrings.Key.GroupFinderAddedToComparisonMessage, this.displayLanguage, entry.CharacterName));
            }

            ImGui.Separator();
        }
    }

    /// <summary>Zentrale Zuordnung AvailabilityTag -> UiStrings.Key (siehe DrawGroupFinderTab,
    /// sowohl für die eigenen Tag-Checkboxen als auch für die Anzeige fremder Einträge) - EINE
    /// Stelle statt zweier unabhängiger switch-Ausdrücke, die bei einem künftigen sechsten Tag
    /// sonst leicht auseinanderlaufen könnten.</summary>
    private static UiStrings.Key GetAvailabilityTagLabelKey(AvailabilityTag tag) => tag switch
    {
        AvailabilityTag.Morning => UiStrings.Key.GroupFinderTagMorning,
        AvailabilityTag.Afternoon => UiStrings.Key.GroupFinderTagAfternoon,
        AvailabilityTag.Evening => UiStrings.Key.GroupFinderTagEvening,
        AvailabilityTag.Weekend => UiStrings.Key.GroupFinderTagWeekend,
        AvailabilityTag.Flexible => UiStrings.Key.GroupFinderTagFlexible,
        _ => throw new ArgumentOutOfRangeException(nameof(tag), tag, null),
    };

    /// <summary>Verweis auf die Browser-Version des Sync-Codes (siehe README-Abschnitt "Sync
    /// without a server") - erlaubt Freunden ohne installiertes Plugin, ihren Status trotzdem
    /// als Code zu exportieren/importieren, ganz ohne laufendes FFXIV.</summary>
    private void DrawWebCompanionTab()
    {
        this.DrawLastMessage();

        ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.WebCompanionIntro, this.displayLanguage));
        ImGui.Separator();

        // Als reiner Text angezeigt (nicht nur über die Buttons erreichbar), damit man die URL
        // notfalls auch von Hand abschreiben oder screenshotten kann - siehe Aufgabenstellung.
        ImGui.TextUnformatted(WebCompanionUrl);
        ImGui.Separator();

        if (ImGui.Button(UiStrings.Get(UiStrings.Key.OpenInBrowserButton, this.displayLanguage)))
        {
            try
            {
                Process.Start(new ProcessStartInfo(WebCompanionUrl) { UseShellExecute = true });
                this.SetSuccessMessage(UiStrings.Get(UiStrings.Key.BrowserOpenedMessage, this.displayLanguage));
            }
            catch (Exception ex)
            {
                this.SetErrorMessage(UiStrings.Format(UiStrings.Key.GenericError, this.displayLanguage, ex.Message));
            }
        }

        ImGui.SameLine();

        if (ImGui.Button(UiStrings.Get(UiStrings.Key.CopyLinkButton, this.displayLanguage)))
        {
            ImGui.SetClipboardText(WebCompanionUrl);
            this.SetSuccessMessage(UiStrings.Get(UiStrings.Key.LinkCopiedMessage, this.displayLanguage));
        }
    }

    private void DrawSettingsTab()
    {
        // Zeigt insbesondere Push-/Fetch-/Lösch-Ergebnisse von Live-Sync an (siehe
        // ApplyLiveSyncResult) - dieselbe zentrale Anzeige wie in den anderen Tabs.
        this.DrawLastMessage();

        ImGui.TextUnformatted(UiStrings.Get(UiStrings.Key.DisplayLanguageHeader, this.displayLanguage));
        ImGui.Separator();

        foreach (var language in Enum.GetValues<DisplayLanguage>())
        {
            if (ImGui.RadioButton(GetNativeLanguageName(language), this.displayLanguage == language))
                this.displayLanguage = language;
        }

        ImGui.Separator();
        ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.DisplayLanguageHint, this.displayLanguage));

        // Feature 2: reine Checkbox, kein An-/Abmelden von irgendwas nötig (anders als beim
        // Chat-Hook in Feature 3) - der aktuelle Wert wird einfach beim nächsten Export-Klick in
        // DrawSyncTab gelesen (siehe TryAutoShareToPartyChat).
        ImGui.Separator();
        ImGui.Checkbox(UiStrings.Get(UiStrings.Key.AutoShareToPartyChatToggle, this.displayLanguage), ref this.autoShareToPartyChat);
        ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.AutoShareToPartyChatHint, this.displayLanguage));

        // Live-Sync: einzige NEUE persistierte Einstellung im Projekt (siehe Configuration.cs) -
        // deshalb sofort per Save() weggeschrieben, statt wie die übrigen Checkboxen hier nur
        // In-Memory zu leben.
        ImGui.Separator();

        var liveSyncEnabled = this.configuration.LiveSyncEnabled;
        if (ImGui.Checkbox(UiStrings.Get(UiStrings.Key.LiveSyncEnabledToggle, this.displayLanguage), ref liveSyncEnabled))
        {
            this.configuration.LiveSyncEnabled = liveSyncEnabled;
            this.configuration.Save();
            // Kein gesonderter PushOwnProfile()-Aufruf beim Einschalten nötig: LiveSyncService.Tick
            // (läuft ab jetzt jeden Frame, siehe Draw()) erkennt beim allerersten Durchlauf von
            // selbst, dass noch nichts gepusht wurde, und stößt den ersten Push automatisch an.
        }

        ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.LiveSyncEnabledHint, this.displayLanguage));

        var hasLiveSyncProfile = this.liveSyncService.HasEditTokenForLocalCharacter();
        ImGui.BeginDisabled(!hasLiveSyncProfile);
        if (ImGui.Button(UiStrings.Get(UiStrings.Key.LiveSyncDeleteProfileButton, this.displayLanguage)))
            this.liveSyncService.DeleteOwnProfile();
        ImGui.EndDisabled();
    }

    /// <summary>Name EINER Sprache, immer in ihrer eigenen Schrift ("Deutsch", "English",
    /// "Français", "日本語") - bewusst NICHT über <see cref="UiStrings"/> in die jeweils
    /// AKTUELL gewählte Sprache übersetzt. Das ist das aus praktisch jedem Sprachauswahlmenü
    /// bekannte Muster (Wikipedia, Discord, ...): so findet man seine eigene Sprache in der
    /// Liste auch dann noch, wenn die UI gerade auf eine Sprache eingestellt ist, die man nicht
    /// versteht - und die Radio-Button-Beschriftungen ändern sich dadurch beim Sprachwechsel
    /// bewusst NICHT.</summary>
    private static string GetNativeLanguageName(DisplayLanguage language) => language switch
    {
        DisplayLanguage.German => "Deutsch",
        DisplayLanguage.English => "English",
        DisplayLanguage.French => "Français",
        DisplayLanguage.Japanese => "日本語",
        _ => language.ToString(),
    };

    /// <summary>Zentrale Stelle, über die überall in der UI (Comparison-Tab, Lernplan-Tab,
    /// Tooltips) auf den Spell-Namen in der aktuell gewählten <see cref="displayLanguage"/>
    /// zugegriffen wird, statt direkt eines der NameDe/NameEn/NameFr/NameJa-Felder zu lesen.</summary>
    private string GetSpellName(Spell spell) => spell.GetName(this.displayLanguage);

    /// <summary>Analog zu <see cref="GetSpellName"/>, nur für Monster-Namen (Tooltips,
    /// Lernplan-Tab). Für die meisten Monster DE/FR über FFXIV Collect verifiziert, JA aktuell
    /// noch Platzhalter (= Englisch) bis zum Lumina-Nachzug, siehe Models/Monster.cs.</summary>
    private string GetMonsterName(Monster monster) => monster.GetName(this.displayLanguage);

    /// <summary>Meldet den in Feature 3 (siehe <see cref="autoImportAsPartyLeader"/>/
    /// <see cref="OnChatMessage"/>) ggf. noch aktiven Chat-Hook wieder ab. Bewusst unbedingt
    /// (kein if (this.autoImportAsPartyLeader) davor) - ein -= auf ein Delegate, das nie
    /// abonniert wurde, ist in C# ein sicherer No-Op, und so kann hier nichts vergessen werden.
    /// Muss von Plugin.Dispose() aufgerufen werden (Window selbst ist nicht IDisposable und wird
    /// von WindowSystem.RemoveAllWindows() auch nicht entsorgt) - sonst bliebe der Hook nach dem
    /// Entladen/Neuladen des Plugins bestehen (Speicherleck + doppeltes Feuern bei einem Reload).</summary>
    public void Dispose() => this.chatGui.ChatMessage -= this.OnChatMessage;
}
