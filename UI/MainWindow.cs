using BLUnion.Models;
using BLUnion.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Game;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;

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
    private readonly ITextureProvider textureProvider;

    private string importCodeBuffer = string.Empty;
    private string comparisonFilterText = string.Empty;
    private string learningPlanFilterText = string.Empty;
    private string? lastError;

    /// <summary>Sprache, in der aktuell die GESAMTE Oberfläche angezeigt wird (Spell-Namen über
    /// <see cref="GetSpellName"/>, alle sonstigen Texte über <see cref="UiStrings"/>). Nur
    /// In-Memory für die laufende Sitzung, wird bewusst nicht persistiert (siehe
    /// Aufgabenstellung zur Mehrsprachigkeit) - beim nächsten Fensteröffnen greift wieder der
    /// Default aus dem Konstruktor.</summary>
    private DisplayLanguage displayLanguage;

    public MainWindow(
        PartyService partyService,
        SpellDataService spellDataService,
        ComparisonService comparisonService,
        LocalSpellUnlockService localSpellUnlockService,
        ManualCodeSyncProvider syncProvider,
        ITextureProvider textureProvider,
        IClientState clientState)
        : base("BLUnion###BLUnion")
    {
        this.partyService = partyService;
        this.spellDataService = spellDataService;
        this.comparisonService = comparisonService;
        this.localSpellUnlockService = localSpellUnlockService;
        this.syncProvider = syncProvider;
        this.textureProvider = textureProvider;

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
    }

    public override void Draw()
    {
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
        if (this.lastError is not null)
        {
            ImGui.TextColored(new System.Numerics.Vector4(1, 0.4f, 0.4f, 1), this.lastError);
            ImGui.Separator();
        }

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
        ImGui.Separator();

        var filteredRows = rows
            .Where(r => SpellFilter.Matches(r.Name, r.SpellbookOrder, this.comparisonFilterText))
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
                var sources = this.spellDataService.GetSourcesForSpell(entry.SpellId).ToList();

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
                            UiStrings.Key.TooltipSourceLine, this.displayLanguage, monster.Name, method, this.FormatLocation(location)));
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

        var groups = this.comparisonService.GroupMissingSpellsByMonster(missing, this.spellDataService)
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
            var monsterName = monster?.Name ?? UiStrings.Format(UiStrings.Key.MonsterFallback, this.displayLanguage, group.MonsterId);

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
    /// sonst Dungeon/Trial-Name (falls vorhanden), sonst nur die Zone.</summary>
    private string FormatLocation(Location? location)
    {
        if (location is null)
            return UiStrings.Get(UiStrings.Key.UnknownLocation, this.displayLanguage);

        if (location.Coordinates is not null)
            return $"{location.ZoneName} ({location.Coordinates})";

        if (location.DutyName is not null)
            return location.DutyName;

        return location.ZoneName;
    }

    /// <summary>Kurze Quellen-Zusammenfassung für die Tabellenspalte; Details gibt's im Zeilen-Tooltip.</summary>
    private string FormatSourceSummary(IReadOnlyList<(Monster Monster, Location? Location, string Method)> sources)
    {
        if (sources.Count == 0)
            return UiStrings.Get(UiStrings.Key.UnknownLocation, this.displayLanguage);

        if (sources.Count == 1)
        {
            var (monster, location, _) = sources[0];
            return $"{monster.Name} ({this.FormatLocation(location)})";
        }

        return UiStrings.Format(UiStrings.Key.SourceCountSummary, this.displayLanguage, sources.Count);
    }

    private void DrawSyncTab()
    {
        ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.SyncIntro, this.displayLanguage));

        ImGui.Separator();

        if (ImGui.Button(UiStrings.Get(UiStrings.Key.DetermineAndExportButton, this.displayLanguage)))
        {
            try
            {
                this.lastError = null;
                var localPlayerName = this.partyService.GetPartyMembers()
                    .FirstOrDefault(m => m.IsBlueMage)?.Name ?? UiStrings.Get(UiStrings.Key.LocalPlayerFallbackName, this.displayLanguage);

                var status = this.localSpellUnlockService.GetLocalPlayerStatus(localPlayerName);
                this.syncProvider.PublishLocalStatus(status);
                var code = this.syncProvider.ExportToCode(status);
                ImGui.SetClipboardText(code);
                this.lastError = UiStrings.Get(UiStrings.Key.ClipboardCopiedMessage, this.displayLanguage);
            }
            catch (Exception ex)
            {
                this.lastError = UiStrings.Format(UiStrings.Key.GenericError, this.displayLanguage, ex.Message);
            }
        }

        ImGui.Separator();
        ImGui.InputText(UiStrings.Get(UiStrings.Key.ImportCodeLabel, this.displayLanguage), ref this.importCodeBuffer, 4096);

        if (ImGui.Button(UiStrings.Get(UiStrings.Key.ImportButton, this.displayLanguage)))
        {
            try
            {
                this.syncProvider.ImportCode(this.importCodeBuffer);
                this.lastError = null;
                this.importCodeBuffer = string.Empty;
            }
            catch (Exception ex)
            {
                this.lastError = UiStrings.Format(UiStrings.Key.ImportFailed, this.displayLanguage, ex.Message);
            }
        }

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
            this.lastError = UiStrings.Format(
                UiStrings.Key.DevFixtureLoaded, this.displayLanguage, fixture.CharacterName, fixture.LearnedSpellIds.Count);
        }
    }

    private void DrawSettingsTab()
    {
        ImGui.TextUnformatted(UiStrings.Get(UiStrings.Key.DisplayLanguageHeader, this.displayLanguage));
        ImGui.Separator();

        foreach (var language in Enum.GetValues<DisplayLanguage>())
        {
            if (ImGui.RadioButton(GetNativeLanguageName(language), this.displayLanguage == language))
                this.displayLanguage = language;
        }

        ImGui.Separator();
        ImGui.TextWrapped(UiStrings.Get(UiStrings.Key.DisplayLanguageHint, this.displayLanguage));
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
}
