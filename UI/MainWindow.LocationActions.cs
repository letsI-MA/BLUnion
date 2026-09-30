using BLUnion.Models;
using BLUnion.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;

namespace BLUnion.UI;

// Klickbare Orts-/Duty-Namen und Karten-Icons - gemeinsam für Learning Plan und Spellbook-Detail.
// Was ein Klick tut, entscheidet LocationActions/AetheryteSelector (ohne Dalamud, getestet); ausgeführt
// wird über GameUiLinkService:
// - Duty: Duty Finder öffnen (Einzel-Duty direkt, mehrere Varianten über ein Popup-Menü), keine Anmeldung.
// - Open World: GENAU EIN Teleport zum Aetheryten pro Klick (abschaltbar), danach nur die Kartenflagge.
// Die Map-Icons setzen ImGui.SameLine() selbst und stehen hinter dem zuletzt gezeichneten Element.
public sealed partial class MainWindow
{
    private static readonly System.Numerics.Vector4 LinkTextColor = new(0.45f, 0.75f, 1f, 1f);

    // Orte mit unlesbaren Koordinaten, für die schon gewarnt wurde - sonst käme die Warnung jeden Frame.
    private readonly HashSet<uint> unparsableCoordinatesWarned = new();

    // Zeichnet den Namen eines Orts an der aktuellen Cursorposition - klickbar, falls LocationActions eine
    // Aktion liefert, sonst als normaler Text. zoneLevel: Zone als Ganzes (Learning-Plan-Kopfzeile) ->
    // Haupt-Aetheryt, keine Flagge; sonst mit Koordinaten der nächstgelegene Aetheryt + Flagge beim Monster.
    private void DrawLocationName(Location? location, string text, string idSuffix, bool zoneLevel = false)
    {
        var mapLink = zoneLevel ? null : LocationActions.Get(location).MapLink;
        var action = this.GetLocationNameAction(location, mapLink);

        switch (action.Kind)
        {
            case LocationNameKind.DutyFinder:
                if (this.DrawLinkText(text, $"DutyName{idSuffix}"))
                    this.gameUiLinkService.OpenDutyFinder(action.DutyIds[0]);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(UiStrings.Get(UiStrings.Key.DutyFinderOpenTooltip, this.displayLanguage));
                break;

            case LocationNameKind.DutyFinderMenu:
                var popupId = $"DutyVariants{idSuffix}";
                if (this.DrawLinkText(text, $"DutyName{idSuffix}"))
                    ImGui.OpenPopup(popupId);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(UiStrings.Get(UiStrings.Key.DutyFinderOpenTooltip, this.displayLanguage));
                this.DrawDutyVariantPopup(popupId, action.DutyIds);
                break;

            case LocationNameKind.Teleport:
                var aetheryte = action.Aetheryte!;
                if (this.DrawLinkText(text, $"Teleport{idSuffix}"))
                    this.TeleportTo(aetheryte, mapLink);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(this.FormatTeleportTooltip(aetheryte, location));
                break;

            case LocationNameKind.DutyFinderLocked or LocationNameKind.TeleportLocked:
                ImGui.TextColored(NotLearnedColor, text);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(UiStrings.Get(
                        action.Kind == LocationNameKind.TeleportLocked ? UiStrings.Key.AetheryteNotUnlockedTooltip : UiStrings.Key.DutyFinderNotUnlockedTooltip,
                        this.displayLanguage));
                }

                break;

            default:
                ImGui.TextUnformatted(text);
                break;
        }
    }

    private LocationNameAction GetLocationNameAction(Location? location, MapLinkTarget? mapLink)
    {
        var teleportEnabled = this.configuration.TeleportOnLocationClick;

        AetheryteCandidate? aetheryte = null;
        if (teleportEnabled && location is { DutyName: null, TerritoryTypeId: { } territoryTypeId } && location.GetContentFinderConditionIds().Count == 0)
            aetheryte = AetheryteSelector.Select(this.gameUiLinkService.GetAetherytes(), territoryTypeId, mapLink);

        var dutyIds = location?.GetContentFinderConditionIds() ?? [];
        return LocationActions.GetNameAction(
            location,
            teleportEnabled,
            aetheryte,
            aetheryte is null ? null : this.gameUiLinkService.IsAetheryteUnlocked(aetheryte.Id),
            dutyIds.Count == 1 ? this.gameUiLinkService.IsDutyUnlocked(dutyIds[0]) : null);
    }

    // Varianten einer Mehrfach-Duty ("Hard" / "Extreme"); nicht freigeschaltete ausgegraut mit Tooltip.
    private void DrawDutyVariantPopup(string popupId, IReadOnlyList<uint> dutyIds)
    {
        if (!ImGui.BeginPopup(popupId))
            return;

        var items = LocationActions.GetVariantMenu(
            dutyIds,
            dutyIds.Select(cfcId => this.gameTextService.GetDutyName(cfcId, this.displayLanguage)).ToList(),
            this.gameUiLinkService.IsDutyUnlocked,
            UiStrings.Get(UiStrings.Key.DutyFinderVariantNormal, this.displayLanguage),
            number => UiStrings.Format(UiStrings.Key.DutyFinderVariantFallback, this.displayLanguage, number));

        foreach (var item in items)
        {
            if (ImGui.MenuItem($"{item.Label}##{item.ContentFinderConditionId}", string.Empty, false, item.Enabled))
                this.gameUiLinkService.OpenDutyFinder(item.ContentFinderConditionId);

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.SetTooltip(item.Enabled
                    ? UiStrings.Format(UiStrings.Key.DutyFinderOpenVariantTooltip, this.displayLanguage, item.FullName)
                    : $"{UiStrings.Get(UiStrings.Key.DutyFinderNotUnlockedTooltip, this.displayLanguage)}\n{item.FullName}");
            }
        }

        ImGui.EndPopup();
    }

    // Genau ein Teleport-Aufruf; nur wenn das Spiel ihn annimmt, zusätzlich die Flagge beim Monster.
    // Abgelehnt (Duty, Kampf, Crafting ...) -> nur eine kurze Statusmeldung, keine Wiederholung.
    private void TeleportTo(AetheryteCandidate aetheryte, MapLinkTarget? flagTarget)
    {
        if (!this.gameUiLinkService.Teleport(aetheryte.Id))
        {
            this.SetErrorMessage(UiStrings.Get(UiStrings.Key.TeleportFailedMessage, this.displayLanguage));
            return;
        }

        if (flagTarget is { } target)
            this.gameUiLinkService.SetMapFlag(target);
    }

    private string FormatTeleportTooltip(AetheryteCandidate aetheryte, Location? location) =>
        UiStrings.Format(
            UiStrings.Key.TeleportToTooltip,
            this.displayLanguage,
            this.gameUiLinkService.GetAetheryteName(aetheryte.Id, this.displayLanguage)
                ?? (location is null ? $"#{aetheryte.Id}" : this.gameTextService.GetZoneName(location, this.displayLanguage)));

    // Text in Link-Farbe (unterstrichen beim Hover). InvisibleButton statt Text: hat eine eigene ID und
    // nimmt den Klick damit einem darunterliegenden CollapsingHeader (AllowItemOverlap) weg.
    private bool DrawLinkText(string text, string id)
    {
        if (text.Length == 0)
            return false;

        var position = ImGui.GetCursorScreenPos();
        var size = ImGui.CalcTextSize(text);
        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();

        var color = ImGui.GetColorU32(LinkTextColor);
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddText(position, color, text);
        if (hovered)
        {
            drawList.AddLine(new System.Numerics.Vector2(position.X, position.Y + size.Y), position + size, color);
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        return clicked;
    }

    // "Auf Karte markieren" + "Link in Chat" für Open-World-Orte mit Koordinaten; mit showTeleportIcon
    // dazwischen ein Teleport-Icon zum nächstgelegenen Aetheryten (Learning Plan, je Monster).
    // mapLinkLabel steht im Chat vor dem Link (z.B. der Monstername).
    private void DrawMapLinkButtons(Location? location, string mapLinkLabel, string idSuffix, bool showTeleportIcon = false)
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

        if (showTeleportIcon)
            this.DrawTeleportIcon(location, target, idSuffix);

        ImGui.SameLine();
        if (ImGuiComponents.IconButton($"MapChat{idSuffix}", FontAwesomeIcon.CommentDots))
            this.gameUiLinkService.PrintMapLink(target, mapLinkLabel);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(UiStrings.Get(UiStrings.Key.MapLinkToChatTooltip, this.displayLanguage));
    }

    // Einstellung aus / kein Aetheryt: kein Icon. Nicht freigeschaltet: ausgegraut mit Tooltip.
    private void DrawTeleportIcon(Location? location, MapLinkTarget target, string idSuffix)
    {
        var action = this.GetLocationNameAction(location, target);
        if (action.Kind is not (LocationNameKind.Teleport or LocationNameKind.TeleportLocked))
            return;

        var locked = action.Kind == LocationNameKind.TeleportLocked;
        ImGui.SameLine();
        if (locked)
            ImGui.BeginDisabled();

        if (ImGuiComponents.IconButton($"Teleport{idSuffix}", FontAwesomeIcon.LocationArrow))
            this.TeleportTo(action.Aetheryte!, target);

        if (locked)
            ImGui.EndDisabled();

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(locked
                ? UiStrings.Get(UiStrings.Key.AetheryteNotUnlockedTooltip, this.displayLanguage)
                : this.FormatTeleportTooltip(action.Aetheryte!, location));
        }
    }
}
