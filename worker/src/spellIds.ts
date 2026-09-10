/**
 * Bekannte Blue-Mage-Spell-IDs - dasselbe Muster wie WORLD_DATA_CENTERS in worlds.ts: fest
 * hinterlegt statt zur Laufzeit aus einer Datei/einem Dienst geladen, weil sich die Blue-Mage-
 * Spellliste nur mit neuen Erweiterungen ändert (siehe dortige Begründung). NUR für die
 * targetSpellIds-Validierung in handleGroupPut gebraucht (siehe index.ts) - der Worker kennt
 * sonst keine Spell-Metadaten, spellBitmaskBase64 bleibt für ihn ein opakes Byte-Array.
 *
 * Quelle ist Data/spells.json im Repo-Root (dieselbe Datei, aus der auch das Plugin seine
 * Spell-Liste lädt, siehe SpellDataService.cs) - MUSS bei neuen Spells manuell neu generiert
 * werden. Aus dem Repo-Root:
 *   node -e "console.log(JSON.stringify(require('./Data/spells.json').map(s => s.Id).sort((a, b) => a - b)))"
 * und das Ergebnis-Array unten einsetzen.
 */
export const KNOWN_SPELL_IDS: ReadonlySet<number> = new Set([
  11383, 11384, 11385, 11386, 11387, 11388, 11389, 11390, 11391, 11392, 11393, 11394, 11395,
  11396, 11397, 11398, 11399, 11400, 11401, 11402, 11403, 11404, 11405, 11406, 11407, 11408,
  11409, 11410, 11411, 11412, 11413, 11414, 11415, 11416, 11417, 11418, 11419, 11420, 11421,
  11422, 11423, 11424, 11425, 11426, 11427, 11428, 11429, 11430, 11431, 18295, 18296, 18297,
  18298, 18299, 18300, 18301, 18302, 18303, 18304, 18305, 18306, 18307, 18308, 18309, 18310,
  18311, 18312, 18313, 18314, 18315, 18316, 18317, 18318, 18319, 18320, 18321, 18322, 18323,
  18324, 18325, 23264, 23265, 23266, 23267, 23269, 23270, 23271, 23272, 23273, 23275, 23276,
  23277, 23278, 23279, 23280, 23281, 23282, 23283, 23284, 23285, 23286, 23287, 23288, 23290,
  34563, 34564, 34565, 34566, 34567, 34568, 34569, 34570, 34571, 34572, 34573, 34574, 34575,
  34576, 34577, 34578, 34579, 34580, 34581, 34582,
]);
