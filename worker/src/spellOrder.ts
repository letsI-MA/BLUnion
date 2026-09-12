/**
 * Spell-ID -> "order" (die sprachunabhängige Spellbook-Positionsnummer, wie sie im Spell-Auswahl-
 * Dialog des Plugins angezeigt wird) - dasselbe Muster wie KNOWN_SPELL_IDS in spellIds.ts: fest
 * hinterlegt statt zur Laufzeit geladen, weil sich die Blue-Mage-Spellliste nur mit neuen
 * Erweiterungen ändert.
 *
 * BEWUSSTE KOPIE der "order"-Werte aus dem SPELLS-Array in docs/index.html (dort PRO Spell mit
 * nameDe/nameEn/nameFr/nameJa hinterlegt) - der Worker kennt sonst keine Spell-Metadaten (siehe
 * spellIds.ts-Doc). NUR für die Discord-Embed-Darstellung von StoredGroupProfile.targetSpellIds
 * gebraucht (siehe buildGroupEmbedField in index.ts): Discord-Embeds können nicht wie die Website
 * pro Betrachter die Sprache umschalten, deshalb zeigt das Embed absichtlich NUR die sprach-
 * unabhängige order-Nummer (z.B. "#1"), keinen lokalisierten Namen.
 *
 * MUSS bei neuen Spells manuell neu gepflegt werden - UND ZWAR SOWOHL hier ALS AUCH in
 * docs/index.html (die beiden Quellen laufen sonst auseinander). Aus dem Repo-Root, nachdem
 * docs/index.html aktualisiert wurde:
 *   node -e "const fs=require('fs'); const html=fs.readFileSync('docs/index.html','utf8'); \
 *     const spells=JSON.parse(html.match(/const SPELLS = (\[[\s\S]*?\]); \/\/ bereits/)[1]); \
 *     console.log(spells.map(s=>[s.id,s.order]).sort((a,b)=>a[0]-b[0]))"
 * und das Ergebnis-Array unten einsetzen.
 */
export const SPELL_ORDER_BY_ID: ReadonlyMap<number, number> = new Map([
  [11383, 25], [11384, 26], [11385, 1], [11386, 9], [11387, 6], [11388, 28], [11389, 4],
  [11390, 3], [11391, 11], [11392, 18], [11393, 12], [11394, 41], [11395, 17], [11396, 19],
  [11397, 36], [11398, 5], [11399, 27], [11400, 15], [11401, 7], [11402, 2], [11403, 23],
  [11404, 10], [11405, 35], [11406, 13], [11407, 8], [11408, 21], [11409, 22], [11410, 32],
  [11411, 20], [11412, 31], [11413, 40], [11414, 14], [11415, 39], [11416, 42], [11417, 30],
  [11418, 16], [11419, 33], [11420, 34], [11421, 43], [11422, 37], [11423, 24], [11424, 29],
  [11425, 38], [11426, 44], [11427, 45], [11428, 46], [11429, 47], [11430, 48], [11431, 49],
  [18295, 50], [18296, 51], [18297, 52], [18298, 53], [18299, 54], [18300, 55], [18301, 56],
  [18302, 57], [18303, 58], [18304, 59], [18305, 60], [18306, 61], [18307, 62], [18308, 63],
  [18309, 64], [18310, 65], [18311, 66], [18312, 67], [18313, 68], [18314, 69], [18315, 70],
  [18316, 71], [18317, 72], [18318, 73], [18319, 74], [18320, 75], [18321, 76], [18322, 77],
  [18323, 78], [18324, 79], [18325, 80], [23264, 81], [23265, 82], [23266, 83], [23267, 84],
  [23269, 85], [23270, 86], [23271, 87], [23272, 88], [23273, 89], [23275, 90], [23276, 91],
  [23277, 92], [23278, 93], [23279, 94], [23280, 95], [23281, 96], [23282, 97], [23283, 98],
  [23284, 99], [23285, 100], [23286, 101], [23287, 102], [23288, 103], [23290, 104],
  [34563, 105], [34564, 106], [34565, 107], [34566, 108], [34567, 109], [34568, 110],
  [34569, 111], [34570, 112], [34571, 113], [34572, 114], [34573, 115], [34574, 116],
  [34575, 117], [34576, 118], [34577, 119], [34578, 120], [34579, 121], [34580, 122],
  [34581, 123], [34582, 124],
]);
