using BLUnion.Models;
using BLUnion.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;

namespace BLUnion.UI;

// Duty-Finder- und Karten-Knöpfe zu einem Ort - gemeinsam für Learning Plan und Spellbook-Detail.
// Welche Knöpfe es gibt, entscheidet LocationActions (ohne Dalamud, getestet); geöffnet wird über
// GameUiLinkService. Alle Knöpfe öffnen nur Fenster/Karten, nichts wird angemeldet oder gesendet.
// Jede Draw-Methode setzt ImGui.SameLine() selbst - die Knöpfe stehen hinter dem zuletzt gezeichneten Element.
public sealed partial class MainWindow
{
    // Orte mit unlesbaren Koordinaten, für die schon gewarnt wurde - sonst käme die Warnung jeden Frame.
    private readonly HashSet<uint> unparsableCoordinatesWarned = new();

    private sealed record DutyFinderButton(uint ContentFinderConditionId, string Label, string FullName, bool? Unlocked);

    private void DrawLocationActionButtons(Location? location, string mapLinkLabel, string idSuffix)
    {
        this.DrawDutyFinderButtons(location, idSuffix);
        this.DrawMapLinkButtons(location, mapLinkLabel, idSuffix);
    }

    // rightEdgeX gesetzt: Knöpfe rechtsbündig in derselben Zeile (Kopfzeile eines CollapsingHeader mit
    // ImGuiTreeNodeFlags.AllowItemOverlap), sonst direkt dahinter.
    private void DrawDutyFinderButtons(Location? location, string idSuffix, float? rightEdgeX = null)
    {
        var buttons = this.GetDutyFinderButtons(location);
        if (buttons.Count == 0)
            return;

        var style = ImGui.GetStyle();
        if (rightEdgeX is { } rightEdge)
        {
            var width = buttons.Sum(button => ImGui.CalcTextSize(button.Label).X + (style.FramePadding.X * 2))
                + (style.ItemSpacing.X * (buttons.Count - 1));
            ImGui.SameLine(Math.Max(ImGui.GetCursorPosX(), rightEdge - width));
        }
        else
        {
            ImGui.SameLine();
        }

        for (var index = 0; index < buttons.Count; index++)
        {
            var button = buttons[index];
            if (index > 0)
                ImGui.SameLine();

            var locked = button.Unlocked == false;
            if (locked)
                ImGui.BeginDisabled();

            if (ImGui.SmallButton($"{button.Label}##DutyFinder{idSuffix}_{button.ContentFinderConditionId}"))
                this.gameUiLinkService.OpenDutyFinder(button.ContentFinderConditionId);

            if (locked)
                ImGui.EndDisabled();

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.SetTooltip(locked
                    ? $"{UiStrings.Get(UiStrings.Key.DutyFinderNotUnlockedTooltip, this.displayLanguage)}\n{button.FullName}"
                    : UiStrings.Format(UiStrings.Key.DutyFinderOpenVariantTooltip, this.displayLanguage, button.FullName));
            }
        }
    }

    // Einzel-Duty: ein Knopf "Im Duty Finder öffnen". Mehrere Varianten: je Variante ein Knopf mit dem
    // übersetzten, auf den Unterschied gekürzten Namen ("Hard" / "Extreme").
    private List<DutyFinderButton> GetDutyFinderButtons(Location? location)
    {
        var dutyIds = LocationActions.Get(location).DutyIds;
        if (dutyIds.Count == 0)
            return [];

        var fullNames = dutyIds
            .Select((cfcId, index) => this.gameTextService.GetDutyName(cfcId, this.displayLanguage)
                ?? (dutyIds.Count == 1
                    ? this.gameTextService.GetZoneName(location!, this.displayLanguage)
                    : UiStrings.Format(UiStrings.Key.DutyFinderVariantFallback, this.displayLanguage, index + 1)))
            .ToList();

        var labels = dutyIds.Count == 1
            ? [UiStrings.Get(UiStrings.Key.DutyFinderOpenButton, this.displayLanguage)]
            : LocationActions.ShortenVariantNames(fullNames)
                .Select(label => label ?? UiStrings.Get(UiStrings.Key.DutyFinderVariantNormal, this.displayLanguage))
                .ToList();

        return dutyIds
            .Select((cfcId, index) => new DutyFinderButton(cfcId, labels[index], fullNames[index], this.gameUiLinkService.IsDutyUnlocked(cfcId)))
            .ToList();
    }

    // "Auf Karte markieren" + "Link in Chat" für Open-World-Orte mit Koordinaten. mapLinkLabel steht im
    // Chat vor dem Link (z.B. der Monstername).
    private void DrawMapLinkButtons(Location? location, string mapLinkLabel, string idSuffix)
    {
        var actions = LocationActions.Get(location);

        if (actions.UnparsableCoordinates && this.unparsableCoordinatesWarned.Add(location!.Id))
            this.log.Warning($"Location {location.Id}: Koordinaten \"{location.Coordinates}\" nicht lesbar - kein Karten-Knopf.");

        if (actions.MapLink is not { } target)
            return;

        ImGui.SameLine();
        if (ImGuiComponents.IconButton($"MapMark{idSuffix}", FontAwesomeIcon.MapMarkerAlt))
            this.gameUiLinkService.OpenMap(target);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(UiStrings.Get(UiStrings.Key.MapMarkTooltip, this.displayLanguage));

        ImGui.SameLine();
        if (ImGuiComponents.IconButton($"MapChat{idSuffix}", FontAwesomeIcon.CommentDots))
            this.gameUiLinkService.PrintMapLink(target, mapLinkLabel);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(UiStrings.Get(UiStrings.Key.MapLinkToChatTooltip, this.displayLanguage));
    }
}
