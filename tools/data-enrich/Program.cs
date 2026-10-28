// Einmaliges Anreicherungs-Werkzeug für Data/*.json (siehe README.md im selben Ordner).
//
// Liest die Spieldateien per Lumina direkt aus einem sqpack-Ordner und ergänzt Game-IDs:
//   spells.json    -> ActionId, AozActionId
//   monsters.json  -> BNpcNameId bzw. bei Sammelnamen BNpcNameIds
//   locations.json -> TerritoryTypeId, MapId, PlaceNameId, ContentFinderConditionId bzw. bei
//                     Sammeleinträgen mit mehreren Duty-Varianten ContentFinderConditionIds
// Bestehende Felder bleiben erhalten, neue Felder werden am Ende des jeweiligen Objekts angehängt.
// Nichts wird geraten: ohne eindeutigen Treffer bleibt das Feld leer und der Eintrag landet in
// report.md. Idempotent: Ergebnis hängt nur von Spieldaten + JSON-Inhalt ab (keine Zeitstempel).

using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lumina;
using Lumina.Data;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Lumina.Text.Payloads;
using Lumina.Text.ReadOnly;

namespace DataEnrich;

internal static class Program
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        NewLine = "\n",
        // Umlaute/Japanisch/Apostrophe bleiben wie in den handgepflegten Dateien unescaped.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // Quellen, die kein echtes Monster sind (Totems, Startzauber, Carnivale-Fortschritt) - für
    // diese "Monster" wird bewusst keine BNpcName-ID gesucht, ein Namenstreffer wäre Zufall.
    private static readonly HashSet<string> PseudoSourceMethods =
        ["Totem", "TotemSpellCount", "TotemLevel", "StartingSpell", "MaskedCarnivaleProgress"];

    // TerritoryIntendedUse 1 = offene Welt (Feldgebiet). Nur zum Auflösen mehrdeutiger Zonen-Treffer.
    private const uint OverworldIntendedUse = 1;

    private static int Main(string[] args)
    {
        if (args.Length < 1 || args[0] is "-h" or "--help")
        {
            Console.Error.WriteLine("Verwendung: dotnet run -- <sqpack-Pfad> [--data <Data-Ordner>] [--report <report.md>]");
            return 2;
        }

        var sqpackPath = args[0];
        var dataDir = GetOption(args, "--data") ?? FindDataDirectory();
        var reportPath = GetOption(args, "--report") ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "report.md");
        reportPath = Path.GetFullPath(reportPath);

        if (!Directory.Exists(sqpackPath))
        {
            Console.Error.WriteLine($"sqpack-Ordner nicht gefunden: {sqpackPath}");
            return 2;
        }

        Console.WriteLine($"sqpack: {sqpackPath}");
        Console.WriteLine($"Data:   {dataDir}");

        var gameData = new GameData(sqpackPath, new LuminaOptions { DefaultExcelLanguage = Language.English });
        var report = new Report(ReadGameVersion(sqpackPath));

        if (!RunSanityChecks(gameData, out var sanityError))
        {
            Console.Error.WriteLine($"Plausibilitätsprüfung fehlgeschlagen - Sheet-Schema passt nicht zu den Spieldaten: {sanityError}");
            return 1;
        }

        var spells = LoadArray(Path.Combine(dataDir, "spells.json"));
        var monsters = LoadArray(Path.Combine(dataDir, "monsters.json"));
        var locations = LoadArray(Path.Combine(dataDir, "locations.json"));
        var sources = LoadArray(Path.Combine(dataDir, "sources.json")).Array;

        EnrichSpells(gameData, spells.Array, report);
        EnrichMonsters(gameData, monsters.Array, sources, report);
        EnrichLocations(gameData, locations.Array, report);
        CollectDescriptionMacros(gameData, report);

        SaveArray(spells);
        SaveArray(monsters);
        SaveArray(locations);
        File.WriteAllText(reportPath, report.ToMarkdown(), new UTF8Encoding(false));

        Console.WriteLine(report.SummaryLine());
        Console.WriteLine($"Report: {reportPath}");
        return 0;
    }

    // ---------------------------------------------------------------- Spells

    private static void EnrichSpells(GameData gameData, JsonArray spells, Report report)
    {
        var actions = gameData.GetExcelSheet<Lumina.Excel.Sheets.Action>(Language.English)!;
        var aozActions = gameData.GetExcelSheet<AozAction>(Language.English)!;
        var aozTransients = gameData.GetExcelSheet<AozActionTransient>(Language.English)!;

        // Action-ID -> AozAction-Zeilen (normalerweise genau eine)
        var aozByActionId = aozActions
            .Where(row => row.RowId != 0 && row.Action.RowId != 0)
            .GroupBy(row => row.Action.RowId)
            .ToDictionary(group => group.Key, group => group.Select(row => row.RowId).OrderBy(id => id).ToList());

        var section = report.Section("spells.json", spells.Count);
        foreach (var spell in spells.OfType<JsonObject>())
        {
            var id = spell["Id"]!.GetValue<uint>();
            var nameEn = spell["NameEn"]?.GetValue<string>() ?? "";
            var order = spell["SpellbookOrder"]?.GetValue<int>() ?? 0;
            var label = $"{id} {nameEn} (#{order})";

            if (!aozByActionId.TryGetValue(id, out var aozIds))
            {
                KeepOrClear(spell, section, label, "keine AozAction mit Action = Id", Candidates(aozActions, aozTransients, actions, order),
                    "ActionId", "AozActionId");
                continue;
            }

            if (aozIds.Count > 1)
            {
                KeepOrClear(spell, section, label, "mehrere AozAction-Zeilen für diese Action",
                    string.Join(", ", aozIds), "ActionId", "AozActionId");
                continue;
            }

            var aozId = aozIds[0];
            var number = aozTransients.GetRowOrDefault(aozId)?.Number ?? 0;
            var actionName = Text(actions.GetRow(id).Name);

            if (number != order)
            {
                KeepOrClear(spell, section, label,
                    $"Spellbook-Nummer passt nicht (AozActionTransient.Number = {number})",
                    $"AozAction {aozId} \"{actionName}\"", "ActionId", "AozActionId");
                continue;
            }

            spell["ActionId"] = id;
            spell["AozActionId"] = aozId;
            section.Matched++;

            if (!NamesEqual(actionName, nameEn))
                section.Notes.Add($"{label}: zugeordnet über Id + Nummer, Spielname weicht ab: \"{actionName}\"");
        }
    }

    private static string Candidates(ExcelSheet<AozAction> aozActions, ExcelSheet<AozActionTransient> transients,
        ExcelSheet<Lumina.Excel.Sheets.Action> actions, int order)
    {
        var byNumber = transients.Where(row => row.RowId != 0 && row.Number == order).Select(row => row.RowId).ToList();
        return string.Join(", ", byNumber.Select(aozId =>
        {
            var actionId = aozActions.GetRowOrDefault(aozId)?.Action.RowId ?? 0;
            return $"AozAction {aozId} -> Action {actionId} \"{Text(actions.GetRowOrDefault(actionId)?.Name)}\"";
        }));
    }

    // ---------------------------------------------------------------- Monster

    private static void EnrichMonsters(GameData gameData, JsonArray monsters, JsonArray sources, Report report)
    {
        var bnpcNames = gameData.GetExcelSheet<BNpcName>(Language.English)!;

        // Normalisierter englischer Name -> niedrigste ID (mehrere gleichnamige BNpcs sind für die
        // Namensauflösung gleichwertig, siehe Aufgabenstellung).
        var idByName = new Dictionary<string, uint>();
        var nameById = new Dictionary<uint, string>();
        foreach (var row in bnpcNames)
        {
            var name = Normalize(Text(row.Singular));
            if (name.Length > 0)
                nameById[row.RowId] = name;
            if (name.Length > 0 && (!idByName.TryGetValue(name, out var existing) || row.RowId < existing))
                idByName[name] = row.RowId;
        }

        var methodsByMonster = sources.OfType<JsonObject>()
            .GroupBy(source => source["MonsterId"]!.GetValue<uint>())
            .ToDictionary(group => group.Key, group => group.Select(source => source["Method"]!.GetValue<string>()).ToHashSet());

        var section = report.Section("monsters.json", monsters.Count);
        foreach (var monster in monsters.OfType<JsonObject>())
        {
            var id = monster["Id"]!.GetValue<uint>();
            var nameEn = monster["NameEn"]?.GetValue<string>() ?? "";
            var label = $"{id} {nameEn}";

            if (methodsByMonster.TryGetValue(id, out var methods) && methods.All(PseudoSourceMethods.Contains))
            {
                section.Skipped.Add($"{label}: kein echtes Monster ({string.Join(", ", methods.Order())}) - bewusst ohne BNpcNameId");
                continue;
            }

            if (idByName.TryGetValue(Normalize(nameEn), out var bnpcNameId))
            {
                monster["BNpcNameId"] = bnpcNameId;
                section.Matched++;
                continue;
            }

            // Sammelnamen ("Goblin Fisher & Gambler", "Coeurl Sruti or Smriti"): jeder Teil wird
            // einzeln exakt abgeglichen. BNpcNameIds wird nur geschrieben, wenn ALLE Teile passen -
            // sonst würde die Anzeige stillschweigend Namen weglassen. Nicht passende Teile (z.B.
            // "Gambler" statt "Goblin Gambler") werden nicht ergänzt/geraten, sondern mit Kandidaten
            // im Report gelistet.
            var parts = SplitCombinedName(nameEn);
            if (parts.Count > 1)
            {
                var partIds = parts.Select(part => idByName.TryGetValue(Normalize(part), out var partId) ? partId : (uint?)null).ToList();
                if (partIds.All(partId => partId is not null))
                {
                    monster["BNpcNameIds"] = new JsonArray(partIds.Select(partId => (JsonNode)JsonValue.Create(partId!.Value)).ToArray());
                    section.Matched++;
                    continue;
                }

                var missing = parts.Where((_, index) => partIds[index] is null).ToList();

                // Von Hand ergänzte IDs (Report-Kandidaten) gelten als zugeordnet, wenn sie zum Namen
                // passen: je Teil eine ID, die automatisch gefundenen Teile stimmen überein, und jede
                // ID ist ein existierender BNpcName.
                if (ReadIds(monster, "BNpcNameIds") is { } manualIds && manualIds.Count == parts.Count
                    && manualIds.All(manualId => nameById.ContainsKey(manualId))
                    && partIds.Select((partId, index) => partId is null || partId == manualIds[index]).All(ok => ok))
                {
                    monster.Remove("BNpcNameId");
                    section.Matched++;
                    section.Notes.Add($"{label}: von Hand ergänzt: " + string.Join(", ", missing.Select(part =>
                    {
                        var manualId = manualIds[parts.IndexOf(part)];
                        return $"\"{part}\" = {manualId} \"{nameById[manualId]}\"";
                    })));
                    continue;
                }

                var matchedText = string.Join(", ", parts
                    .Select((part, index) => (part, partIds[index]))
                    .Where(entry => entry.Item2 is not null)
                    .Select(entry => $"\"{entry.part}\" = {entry.Item2}"));
                var candidates = string.Join("; ", missing.Select(part =>
                    $"\"{part}\": {string.Join(", ", SimilarBNpcNames(idByName, part, nameEn))}"));

                KeepOrClear(monster, section, label,
                    $"Sammelname, nicht zuordenbar: {string.Join(", ", missing.Select(part => $"\"{part}\""))}"
                        + (matchedText.Length > 0 ? $" (zugeordnet: {matchedText})" : ""),
                    candidates, "BNpcNameIds", "BNpcNameId");
                continue;
            }

            KeepOrClear(monster, section, label, "kein BNpcName mit diesem englischen Namen", "", "BNpcNameId", "BNpcNameIds");
        }
    }

    private static List<string> SplitCombinedName(string name) =>
        name.Split([" & ", " or "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    // Nur für den Report (nie für die Zuordnung): BNpc-Namen, die den Teil als ganzes Wort enthalten,
    // bevorzugt solche mit weiteren Wörtern aus dem Gesamtnamen (z.B. "goblin" bei "Gambler").
    private static IEnumerable<string> SimilarBNpcNames(Dictionary<string, uint> idByName, string part, string fullName)
    {
        var partWords = Normalize(part).Split(' ');
        var fullWords = Normalize(fullName).Split(' ').Where(word => word.Length > 2 && word is not "or").ToHashSet();

        return idByName
            .Select(pair => (Name: pair.Key, Id: pair.Value, Words: pair.Key.Split(' ')))
            .Where(entry => partWords.All(entry.Words.Contains))
            .OrderByDescending(entry => entry.Words.Count(fullWords.Contains))
            .ThenBy(entry => entry.Name.Length)
            .ThenBy(entry => entry.Id)
            .Take(5)
            .Select(entry => $"{entry.Id} \"{entry.Name}\"");
    }

    // ---------------------------------------------------------------- Locations

    private static void EnrichLocations(GameData gameData, JsonArray locations, Report report)
    {
        var cfcs = gameData.GetExcelSheet<ContentFinderCondition>(Language.English)!;
        var territories = gameData.GetExcelSheet<TerritoryType>(Language.English)!;
        var placeNames = gameData.GetExcelSheet<PlaceName>(Language.English)!;

        var cfcByName = cfcs
            .Where(row => row.RowId != 0 && row.TerritoryType.RowId != 0)
            .Select(row => (row.RowId, Name: Normalize(Text(row.Name))))
            .Where(row => row.Name.Length > 0)
            .GroupBy(row => row.Name)
            .ToDictionary(group => group.Key, group => group.Select(row => row.RowId).OrderBy(id => id).ToList());

        var placeNameIdsByName = placeNames
            .Where(row => row.RowId != 0)
            .Select(row => (row.RowId, Name: Normalize(Text(row.Name))))
            .Where(row => row.Name.Length > 0)
            .GroupBy(row => row.Name)
            .ToDictionary(group => group.Key, group => group.Select(row => row.RowId).ToHashSet());

        var section = report.Section("locations.json", locations.Count);
        string[] allFields = ["TerritoryTypeId", "MapId", "PlaceNameId", "ContentFinderConditionId"];

        foreach (var location in locations.OfType<JsonObject>())
        {
            var id = location["Id"]!.GetValue<uint>();
            var zoneEn = location["ZoneNameEn"]?.GetValue<string>() ?? "";
            var dutyName = location["DutyName"]?.GetValue<string>();
            var label = $"{id} {dutyName ?? zoneEn}";

            // Masked-Carnivale-Stages sind keine eigenen ContentFinderCondition-Einträge - eine Zuordnung
            // zum gemeinsamen Arena-Gebiet würde die Stage-Nummer verlieren. Mit MaskedCarnivaleStage
            // kommt der Anzeigename aus dem UiStrings-Template (alle Sprachen), eine Game-ID braucht es nicht.
            if (location["MaskedCarnivaleStage"] is JsonValue stageValue && stageValue.TryGetValue<int>(out var stage) && stage > 0)
            {
                section.Skipped.Add($"{label}: Masked-Carnivale-Stage {stage} - Name über UiStrings.MaskedCarnivaleStageFormat, bewusst ohne Game-ID");
                continue;
            }

            if (dutyName is not null)
            {
                if (dutyName.StartsWith("Masked Carnivale", StringComparison.OrdinalIgnoreCase))
                {
                    var carnivale = string.Join(", ", cfcByName
                        .Where(pair => pair.Key.Contains("masked carnivale"))
                        .SelectMany(pair => pair.Value.Select(cfcId => $"CFC {cfcId} \"{Text(cfcs.GetRow(cfcId).Name)}\"")));
                    KeepOrClear(location, section, label, "Masked-Carnivale-Stage ohne MaskedCarnivaleStage (Stage-Nummer fehlt)", carnivale, allFields);
                    continue;
                }

                string? variantReason = null;
                if (!cfcByName.TryGetValue(Normalize(dutyName), out var cfcIds))
                {
                    // Sammeleinträge ("Thornmarch (Hard oder Extreme)", "... (Turn 1-2)"): je Variante
                    // exakt abgleichen - nur wenn JEDE Variante genau einen Treffer hat.
                    if (TryMatchDutyVariants(dutyName, cfcByName, out var variantIds, out variantReason))
                    {
                        var variantText = string.Join(", ", variantIds.Select(cfcId => $"CFC {cfcId} \"{Text(cfcs.GetRow(cfcId).Name)}\""));
                        if (variantIds.Count > 1)
                        {
                            // Bleibt EIN Eintrag mit dem Sammelnamen, nur die Varianten-IDs kommen dazu - keine
                            // TerritoryType-/Map-/PlaceName-ID, die unterscheiden sich je Variante.
                            location["ContentFinderConditionIds"] = new JsonArray(variantIds.Select(cfcId => (JsonNode)JsonValue.Create(cfcId)).ToArray());
                            section.Matched++;
                            section.Notes.Add($"{label}: Varianten zugeordnet: {variantText}");
                            continue;
                        }

                        // Nur eine Variante (z.B. "Akh Afah Amphitheatre (Extreme)") -> wie eine Einzel-Duty.
                        cfcIds = variantIds;
                        section.Notes.Add($"{label}: über die Variante zugeordnet: {variantText}");
                    }
                }

                if (cfcIds is null)
                {
                    var similar = string.Join(", ", SimilarNames(cfcByName, dutyName)
                        .SelectMany(pair => pair.Value.Select(cfcId => $"CFC {cfcId} \"{Text(cfcs.GetRow(cfcId).Name)}\"")));
                    KeepOrClear(location, section, label,
                        "keine ContentFinderCondition mit diesem Namen" + (variantReason is null ? "" : $" ({variantReason})"), similar,
                        [.. allFields, "ContentFinderConditionIds"]);
                    continue;
                }

                if (cfcIds.Count > 1)
                {
                    KeepOrClear(location, section, label, "mehrere ContentFinderConditions mit diesem Namen",
                        string.Join(", ", cfcIds.Select(cfcId => $"CFC {cfcId}")), allFields);
                    continue;
                }

                var cfc = cfcs.GetRow(cfcIds[0]);
                var territory = territories.GetRow(cfc.TerritoryType.RowId);
                location["TerritoryTypeId"] = territory.RowId;
                SetOrRemove(location, "MapId", territory.Map.RowId);
                SetOrRemove(location, "PlaceNameId", territory.PlaceName.RowId);
                location["ContentFinderConditionId"] = cfc.RowId;
                section.Matched++;
                continue;
            }

            // Offene Welt: PlaceName über den englischen Zonennamen, TerritoryType über dessen PlaceName.
            if (!placeNameIdsByName.TryGetValue(Normalize(zoneEn), out var placeNameIds))
            {
                KeepOrClear(location, section, label, "kein PlaceName mit diesem Zonennamen", "", allFields);
                continue;
            }

            var candidates = territories
                .Where(row => row.RowId != 0 && placeNameIds.Contains(row.PlaceName.RowId)
                    && row.ContentFinderCondition.RowId == 0 && row.Map.RowId != 0 && !row.IsPvpZone)
                .ToList();
            var overworld = candidates.Where(row => row.TerritoryIntendedUse.RowId == OverworldIntendedUse).ToList();
            var chosen = overworld.Count == 1 ? overworld : candidates;

            if (chosen.Count == 1)
            {
                var territory = chosen[0];
                location["TerritoryTypeId"] = territory.RowId;
                location["MapId"] = territory.Map.RowId;
                location["PlaceNameId"] = territory.PlaceName.RowId;
                section.Matched++;
                continue;
            }

            var candidateText = string.Join(", ", candidates.Select(row =>
                $"TT {row.RowId} ({Text(row.Name)}, IntendedUse {row.TerritoryIntendedUse.RowId}, Map {row.Map.RowId})"));

            // Gebiet mehrdeutig, Ortsname aber eindeutig -> nur PlaceNameId (reicht für die Namensauflösung).
            var distinctPlaceNames = candidates.Select(row => row.PlaceName.RowId).Distinct().ToList();
            if (distinctPlaceNames.Count == 1 || (candidates.Count == 0 && placeNameIds.Count == 1))
            {
                location["PlaceNameId"] = distinctPlaceNames.Count == 1 ? distinctPlaceNames[0] : placeNameIds.Single();
                KeepOrClear(location, section, label,
                    candidates.Count == 0 ? "PlaceName eindeutig, aber kein passendes TerritoryType" : "PlaceName eindeutig, TerritoryType mehrdeutig",
                    candidateText, "TerritoryTypeId", "MapId", "ContentFinderConditionId");
                section.Partial++;
                continue;
            }

            KeepOrClear(location, section, label, "Zone mehrdeutig", candidateText, allFields);
        }
    }

    // Sammelname = "<Basis> (<Varianten>)", Varianten durch ", " / " oder " / " or " getrennt, z.B.
    // "Thornmarch (Hard oder Extreme)", "The Bowl of Embers (Normal, Hard oder Extreme)" oder mit
    // Bereich "The Binding Coil of Bahamut (Turn 1-2)" -> Turn 1, Turn 2. Je Variante werden nur die
    // Spielschreibweisen "<Basis> (<Variante>)" und "<Basis> - <Variante>" ("Normal" = nur die Basis)
    // exakt verglichen, jeweils mit/ohne führendes "The". Jede Variante braucht genau einen Treffer,
    // sonst false (reason = Grund für den Report, null = gar kein Sammelname).
    private static readonly Regex VariantName = new(@"^(?<base>.+?)\s*\((?<variants>[^()]+)\)\s*$", RegexOptions.Compiled);

    private static readonly Regex VariantRange = new(@"^(?<word>.*?)\s*(?<from>\d+)\s*-\s*(?<to>\d+)$", RegexOptions.Compiled);

    private static bool TryMatchDutyVariants(
        string dutyName, Dictionary<string, List<uint>> cfcByName, out List<uint> ids, out string? reason)
    {
        ids = [];
        reason = null;

        var match = VariantName.Match(dutyName.Trim());
        if (!match.Success)
            return false;

        var baseName = Normalize(match.Groups["base"].Value);
        var baseNames = baseName.StartsWith("the ", StringComparison.Ordinal) ? new[] { baseName, baseName[4..] } : [baseName, "the " + baseName];

        var variants = new List<string>();
        foreach (var token in match.Groups["variants"].Value.Split([",", " oder ", " or "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var range = VariantRange.Match(token);
            if (range.Success && int.Parse(range.Groups["from"].Value, CultureInfo.InvariantCulture) is var from
                && int.Parse(range.Groups["to"].Value, CultureInfo.InvariantCulture) is var to && from < to && to - from < 10)
            {
                var word = range.Groups["word"].Value.Trim();
                variants.AddRange(Enumerable.Range(from, to - from + 1).Select(number => word.Length > 0 ? $"{word} {number}" : $"{number}"));
            }
            else
            {
                variants.Add(token);
            }
        }

        foreach (var variant in variants)
        {
            var normalized = Normalize(variant);
            var candidates = normalized == "normal"
                ? baseNames
                : baseNames.SelectMany(name => new[] { $"{name} ({normalized})", $"{name} - {normalized}" });

            var found = candidates
                .SelectMany(name => cfcByName.TryGetValue(name, out var cfcIds) ? cfcIds : [])
                .Distinct()
                .ToList();

            if (found.Count != 1)
            {
                reason = found.Count == 0 ? $"Variante \"{variant}\" ohne Treffer" : $"Variante \"{variant}\" mehrdeutig";
                ids = [];
                return false;
            }

            ids.Add(found[0]);
        }

        ids = ids.Distinct().ToList();
        if (ids.Count != variants.Count)
        {
            reason = "Varianten ergeben dieselbe Duty";
            ids = [];
            return false;
        }

        return true;
    }

    // Nur für die Kandidatenliste im Report (nie für die Zuordnung): die Namen mit den meisten
    // gemeinsamen Wörtern, Füllwörter und Schwierigkeitsgrade ausgenommen.
    private static IEnumerable<KeyValuePair<string, List<uint>>> SimilarNames(Dictionary<string, List<uint>> byName, string name)
    {
        string[] ignored = ["the", "of", "oder", "and", "hard", "extreme", "normal", "savage", "turn"];
        HashSet<string> Words(string s) => s.Split([' ', '(', ')', '-', ':', ','], StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length > 1 && !ignored.Contains(word)).ToHashSet();

        var wanted = Words(Normalize(name));
        return byName
            .Select(pair => (Pair: pair, Score: Words(pair.Key).Count(wanted.Contains)))
            .Where(entry => entry.Score > 0)
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.Pair.Value.Min())
            .Take(5)
            .Select(entry => entry.Pair);
    }

    // ---------------------------------------------------------------- SeString-Makros (Info)

    private static void CollectDescriptionMacros(GameData gameData, Report report)
    {
        foreach (var language in new[] { Language.German, Language.English, Language.French, Language.Japanese })
        {
            var transients = gameData.GetExcelSheet<AozActionTransient>(language)!;
            foreach (var row in transients)
            {
                foreach (var payload in row.Description)
                {
                    if (payload.Type == ReadOnlySePayloadType.Macro)
                        report.DescriptionMacros[payload.MacroCode] = report.DescriptionMacros.GetValueOrDefault(payload.MacroCode) + 1;
                }
            }
        }
    }

    // ---------------------------------------------------------------- Plausibilität

    private static bool RunSanityChecks(GameData gameData, out string error)
    {
        var actions = gameData.GetExcelSheet<Lumina.Excel.Sheets.Action>(Language.English)!;
        var aozActions = gameData.GetExcelSheet<AozAction>(Language.English)!;
        var aozTransients = gameData.GetExcelSheet<AozActionTransient>(Language.English)!;

        // Snort (Action 11383) ist Blaumagier-Spell #25 - prüft Action.Name, AozAction.Action und
        // AozActionTransient.Number in einem Zug.
        var snortName = Text(actions.GetRowOrDefault(11383)?.Name);
        if (!NamesEqual(snortName, "Snort"))
        {
            error = $"Action 11383 heißt \"{snortName}\" statt \"Snort\".";
            return false;
        }

        var snortAoz = aozActions.FirstOrDefault(row => row.Action.RowId == 11383);
        if (snortAoz.RowId == 0 || aozTransients.GetRowOrDefault(snortAoz.RowId)?.Number != 25)
        {
            error = "AozAction/AozActionTransient für Snort nicht plausibel (erwartet Number 25).";
            return false;
        }

        error = "";
        return true;
    }

    // ---------------------------------------------------------------- Hilfen

    // Ohne eindeutigen Treffer: vorhandene (z.B. manuell gesetzte) Werte bleiben stehen und werden
    // im Report markiert, sonst bleibt das Feld weg. So bleibt das Tool idempotent und überschreibt
    // keine Handkorrekturen.
    private static void KeepOrClear(JsonObject obj, Report.FileSection section, string label, string reason, string candidates, params string[] fields)
    {
        var kept = fields.Where(field => obj.ContainsKey(field)).ToList();
        section.Open.Add(new Report.OpenEntry(label, reason, candidates, kept.Count > 0 ? $"beibehalten: {string.Join(", ", kept)}" : ""));
    }

    private static List<uint>? ReadIds(JsonObject obj, string field) =>
        obj[field] is JsonArray array ? array.Select(node => node!.GetValue<uint>()).ToList() : null;

    private static void SetOrRemove(JsonObject obj, string field, uint value)
    {
        if (value != 0)
            obj[field] = value;
    }

    private static string Text(ReadOnlySeString? value) => value?.ExtractText().Trim() ?? "";

    // Vergleich ohne Groß-/Kleinschreibung, mit vereinheitlichten Apostrophen und Leerzeichen -
    // kein unscharfer Abgleich.
    private static string Normalize(string value) =>
        string.Join(' ', value.Replace('\u2019', '\'').Replace('\u00a0', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    private static bool NamesEqual(string a, string b) => Normalize(a) == Normalize(b);

    private static string? GetOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string FindDataDirectory()
    {
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Data");
            if (File.Exists(Path.Combine(candidate, "spells.json")))
                return candidate;
        }

        throw new DirectoryNotFoundException("Data-Ordner mit spells.json nicht gefunden - bitte --data angeben.");
    }

    private static string ReadGameVersion(string sqpackPath)
    {
        var verFile = Path.Combine(sqpackPath, "..", "ffxivgame.ver");
        return File.Exists(verFile) ? File.ReadAllText(verFile).Trim() : "unbekannt";
    }

    private sealed record JsonFile(string Path, JsonArray Array, bool TrailingNewline);

    private static JsonFile LoadArray(string path)
    {
        var text = File.ReadAllText(path);
        return new JsonFile(path, JsonNode.Parse(text)!.AsArray(), text.EndsWith('\n'));
    }

    private static void SaveArray(JsonFile file)
    {
        var json = UnescapeNonAscii(file.Array.ToJsonString(WriteOptions)) + (file.TrailingNewline ? "\n" : "");
        File.WriteAllText(file.Path, json, new UTF8Encoding(false));
    }

    // Auch UnsafeRelaxedJsonEscaping escaped einzelne Zeichen (z.B. U+00A0), die in den
    // handgepflegten Dateien roh stehen - hier wieder zurückgewandelt, damit der Diff nur die neuen
    // Felder zeigt. Nur \uXXXX ab U+0080 ohne Surrogates, und nur wenn der Backslash selbst nicht
    // escaped ist (gerade Anzahl davor).
    private static readonly Regex UnicodeEscape = new(@"(?<!\\)((?:\\\\)*)\\u([0-9A-Fa-f]{4})", RegexOptions.Compiled);

    private static string UnescapeNonAscii(string json) => UnicodeEscape.Replace(json, match =>
    {
        var code = int.Parse(match.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return code < 0x80 || code is >= 0xD800 and <= 0xDFFF
            ? match.Value
            : match.Groups[1].Value + (char)code;
    });
}

internal sealed class Report(string gameVersion)
{
    private readonly List<FileSection> sections = [];

    public Dictionary<MacroCode, int> DescriptionMacros { get; } = [];

    public sealed record OpenEntry(string Entry, string Reason, string Candidates, string Kept);

    public sealed class FileSection(string file, int total)
    {
        public string File { get; } = file;
        public int Total { get; } = total;
        public int Matched { get; set; }
        public int Partial { get; set; }
        public List<OpenEntry> Open { get; } = [];
        public List<string> Skipped { get; } = [];
        public List<string> Notes { get; } = [];
    }

    public FileSection Section(string file, int total)
    {
        var section = new FileSection(file, total);
        this.sections.Add(section);
        return section;
    }

    public string SummaryLine() => string.Join(" | ", this.sections.Select(s =>
        $"{s.File}: {s.Matched}/{s.Total} zugeordnet, {s.Partial} teilweise, {s.Open.Count - s.Partial} offen, {s.Skipped.Count} übersprungen"));

    public string ToMarkdown()
    {
        var md = new StringBuilder();
        md.AppendLine("# Data-Enrich Report");
        md.AppendLine();
        md.AppendLine($"Erzeugt von `tools/data-enrich` gegen Spielversion `{gameVersion}`. Nicht von Hand bearbeiten -");
        md.AppendLine("wird bei jedem Lauf neu geschrieben.");
        md.AppendLine();
        md.AppendLine("| Datei | Einträge | zugeordnet | teilweise | offen | bewusst übersprungen |");
        md.AppendLine("|---|---|---|---|---|---|");
        foreach (var s in this.sections)
            md.AppendLine($"| {s.File} | {s.Total} | {s.Matched} | {s.Partial} | {s.Open.Count - s.Partial} | {s.Skipped.Count} |");

        foreach (var s in this.sections)
        {
            md.AppendLine();
            md.AppendLine($"## {s.File}");

            if (s.Open.Count > 0)
            {
                md.AppendLine();
                md.AppendLine("| Eintrag | Grund | Kandidaten | Hinweis |");
                md.AppendLine("|---|---|---|---|");
                foreach (var entry in s.Open)
                    md.AppendLine($"| {Escape(entry.Entry)} | {Escape(entry.Reason)} | {Escape(entry.Candidates)} | {Escape(entry.Kept)} |");
            }
            else
            {
                md.AppendLine();
                md.AppendLine("Alle Einträge zugeordnet.");
            }

            if (s.Skipped.Count > 0)
            {
                md.AppendLine();
                md.AppendLine("Bewusst übersprungen:");
                md.AppendLine();
                foreach (var line in s.Skipped)
                    md.AppendLine($"- {line}");
            }

            if (s.Notes.Count > 0)
            {
                md.AppendLine();
                md.AppendLine("Hinweise:");
                md.AppendLine();
                foreach (var line in s.Notes)
                    md.AppendLine($"- {line}");
            }
        }

        md.AppendLine();
        md.AppendLine("## SeString-Makros in AozActionTransient.Description (DE/EN/FR/JA)");
        md.AppendLine();
        md.AppendLine("Zur Kontrolle der Klartext-Bereinigung im Plugin (GameTextCleaner).");
        md.AppendLine();
        foreach (var (macro, count) in this.DescriptionMacros.OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal))
            md.AppendLine($"- {macro}: {count}");

        return md.ToString().Replace("\r\n", "\n");
    }

    private static string Escape(string value) => value.Replace("|", "\\|").Replace("\n", " ");
}
