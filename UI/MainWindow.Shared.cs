using BLUnion.Models;
using BLUnion.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;

namespace BLUnion.UI;

// Kleine, tab-übergreifend genutzte Draw-/Format-Helper (siehe Aufgabenstellung "reine
// Refactoring-Aufteilung nach Tab-Verantwortlichkeiten") - jede Methode hier wird von mindestens
// zwei der MainWindow.<Tab>.cs-Dateien aufgerufen, deshalb bewusst NICHT einer einzelnen
// Tab-Datei zugeordnet. Felder/Enums bleiben unverändert zentral in MainWindow.cs (siehe dortiger
// Klassendoc), nur diese zustandslosen bzw. rein auf vorhandenen Feldern arbeitenden Methoden
// wurden hierher verschoben.
public sealed partial class MainWindow
{
    private static readonly System.Numerics.Vector2 DefaultSpellIconSize = new(24, 24);

    private void DrawSpellIcon(uint iconId, System.Numerics.Vector2? iconSize = null)
    {
        var size = iconSize ?? DefaultSpellIconSize;

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
            }
        }

        ImGui.Dummy(size);
    }

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

    private void DrawCardGrid<T>(
        string gridId,
        IReadOnlyList<T> items,
        Action<T> drawCardContent,
        float cardWidth = 220f,
        float cardHeight = 160f)
    {
        this.DrawCardGridCore(gridId, items, drawCardContent, cardWidth, _ => cardHeight);
    }

    // Gleiches Grid-Layout wie der cardHeight-Overload oben, aber mit INDIVIDUELL pro Element
    // berechneter Höhe (heightSelector) statt einer für alle Karten identischen festen Höhe - siehe
    // ComputeGroupBrowseCardHeight/ComputePlayerBrowseCardHeight in MainWindow.GroupFinder.cs für
    // die beiden Aufrufer (Gruppen- bzw. Spieler-Browse-Karten). Unterschiedlich hohe Karten können
    // dadurch in derselben Grid-Zeile nebeneinander stehen; die kürzere Karte hat dann unten
    // Leerraum - ein akzeptierter optischer Kompromiss für jetzt, KEINE Masonry-artige
    // Neuanordnung.
    private void DrawCardGrid<T>(
        string gridId,
        IReadOnlyList<T> items,
        Action<T> drawCardContent,
        Func<T, float> heightSelector,
        float cardWidth = 220f)
    {
        this.DrawCardGridCore(gridId, items, drawCardContent, cardWidth, heightSelector);
    }

    private void DrawCardGridCore<T>(
        string gridId,
        IReadOnlyList<T> items,
        Action<T> drawCardContent,
        float cardWidth,
        Func<T, float> heightSelector)
    {
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var columns = Math.Max(1, (int)((availableWidth + spacing) / (cardWidth + spacing)));

        for (var i = 0; i < items.Count; i++)
        {
            ImGui.BeginChild($"{gridId}Card{i}", new System.Numerics.Vector2(cardWidth, heightSelector(items[i])), true);
            drawCardContent(items[i]);
            ImGui.EndChild();

            if ((i + 1) % columns != 0 && i < items.Count - 1)
                ImGui.SameLine();
        }
    }

    // buttonRowCount: Anzahl der untereinander stehenden Button-Zeilen, die unten in der Karte
    // reserviert werden sollen (Standard 1, siehe DrawOtherPlayersSection - dort gibt es immer nur
    // EINEN Button). DrawGroupBrowseEntry übergibt hier 2, wenn zusätzlich zum immer vorhandenen
    // Vergleich-Button auch der bedingte "Ziel-Spells anzeigen"-Button gezeichnet wird (siehe Teil A
    // der Aufgabenstellung: beide Buttons stehen seitdem untereinander statt nebeneinander).
    private static void AlignCursorToCardBottom(int buttonRowCount = 1)
    {
        var remainingHeight = ImGui.GetContentRegionAvail().Y;
        var buttonRowsHeight = ImGui.GetFrameHeight() * buttonRowCount
            + ImGui.GetStyle().ItemSpacing.Y * Math.Max(0, buttonRowCount - 1);
        if (remainingHeight > buttonRowsHeight)
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + remainingHeight - buttonRowsHeight);
    }

    // Extra separation on top of the global ItemSpacing, for blocks that need more of a break than that alone gives.
    private static void DrawSectionGap() => ImGui.Dummy(new System.Numerics.Vector2(0f, 8f));

    // Leichtgewichtige "Card"-Darstellung (Header + Inhalt + optionale Action-Zeile darunter) für
    // Dashboard-artige Zusammenfassungen (siehe DrawHomeTab) - bewusst ohne eigenes BeginChild
    // (die vorhandenen ImGui-Bindings hier bieten nur den einfachen BeginChild(id, size, border)-
    // Overload ohne Auto-Resize-Höhe, siehe DrawCardGrid/Spellbook-/Loadouts-Master-Detail-Layout,
    // eine feste Höhe würde bei unterschiedlich langem drawContent aber schlecht aussehen), sondern
    // als Header+Separator+Inhalt+Gap, analog zum bereits bestehenden Section-Header-Muster.
    private void DrawCard(string title, Action drawContent, Action? drawAction = null)
    {
        this.DrawSectionHeader(title);
        ImGui.Separator();
        drawContent();

        if (drawAction is not null)
        {
            ImGui.Spacing();
            drawAction();
        }

        DrawSectionGap();
    }

    // Für "hier gibt es noch nichts anzuzeigen"-Zustände (siehe Aufgabenstellung) - bewusst mit
    // Abstand vor UND nach dem Text (DrawSectionGap), damit der Zustand als eigener, klar
    // abgegrenzter Block wirkt statt als beiläufige Textzeile zwischen anderem Inhalt.
    private void DrawEmptyState(string message, (string Label, Action OnClick)? action = null)
    {
        DrawSectionGap();
        this.DrawHintText(message);

        if (action is { } actionValue)
        {
            ImGui.Spacing();
            if (ImGui.Button(actionValue.Label))
                actionValue.OnClick();
        }

        DrawSectionGap();
    }

    private void DrawHintText(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, HintTextColor);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    private void DrawSectionHeader(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, SectionHeaderColor);
        ImGui.SetWindowFontScale(1.1f);
        ImGui.TextUnformatted(text);
        ImGui.SetWindowFontScale(1.0f);
        ImGui.PopStyleColor();
    }

    private void DrawLastMessage()
    {
        if (this.lastError is null)
            return;

        var color = this.lastMessageIsError
            ? ErrorMessageColor
            : SuccessMessageColor;

        ImGui.TextColored(color, this.lastError);
        ImGui.Separator();
    }

    private void SetSuccessMessage(string message)
    {
        this.lastError = message;
        this.lastMessageIsError = false;
    }

    private void SetErrorMessage(string message)
    {
        this.lastError = message;
        this.lastMessageIsError = true;
    }

    private void ClearMessage() => this.lastError = null;

    // Gemeinsamer Sprungpunkt "zu diesem Spell im Spellbook" von Party Overview/Comparison/
    // Learning Plan aus (siehe Aufgabenstellung) - setzt Auswahl + Filter zurück, damit der Spell im
    // Master-Detail-Layout garantiert sichtbar/ausgewählt ist, und nutzt den bestehenden
    // pendingActiveCategoryTabId/pendingActiveSubTabId-Mechanismus (siehe MainWindow.cs) für den
    // Tab-Sprung selbst. Bekannte Einschränkung (bewusst nicht gelöst): bei schmalen Fenstern
    // (Tabellen- statt Master-Detail-Layout, siehe DrawSpellbookTab) wirkt sich selectedSpellbookSpell
    // erst sichtbar aus, sobald das Fenster wieder breit genug ist.
    private void JumpToSpellInSpellbook(uint spellId)
    {
        if (this.spellDataService.Spells.TryGetValue(spellId, out var spell))
            this.selectedSpellbookSpell = spell;

        this.spellbookFilterMode = SpellbookFilterMode.All;
        this.spellbookFilterText = string.Empty;
        this.pendingActiveCategoryTabId = "TabCategorySpellbook";
        this.pendingActiveSubTabId = "TabSpellbook";
    }

    private string GetSpellName(Spell spell) => spell.GetName(this.displayLanguage);

    private string GetMonsterName(Monster monster) => monster.GetName(this.displayLanguage);
}
