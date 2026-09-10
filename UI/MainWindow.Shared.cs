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
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var columns = Math.Max(1, (int)((availableWidth + spacing) / (cardWidth + spacing)));

        for (var i = 0; i < items.Count; i++)
        {
            ImGui.BeginChild($"{gridId}Card{i}", new System.Numerics.Vector2(cardWidth, cardHeight), true);
            drawCardContent(items[i]);
            ImGui.EndChild();

            if ((i + 1) % columns != 0 && i < items.Count - 1)
                ImGui.SameLine();
        }
    }

    private static void AlignCursorToCardBottom()
    {
        var remainingHeight = ImGui.GetContentRegionAvail().Y;
        var buttonHeight = ImGui.GetFrameHeight();
        if (remainingHeight > buttonHeight)
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + remainingHeight - buttonHeight);
    }

    // Extra separation on top of the global ItemSpacing, for blocks that need more of a break than that alone gives.
    // Not wired up anywhere yet - callers are added at specific spots in a later step.
    private static void DrawSectionGap() => ImGui.Dummy(new System.Numerics.Vector2(0f, 8f));

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

    private string GetSpellName(Spell spell) => spell.GetName(this.displayLanguage);

    private string GetMonsterName(Monster monster) => monster.GetName(this.displayLanguage);
}
