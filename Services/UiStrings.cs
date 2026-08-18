using BLUnion.Models;

namespace BLUnion.Services;

/// <summary>
/// Zentrale Stelle für sämtliche festen UI-Texte des Plugins, in allen 4 unterstützten
/// Sprachen (siehe <see cref="DisplayLanguage"/>). Bewusst NICHT die Texte direkt in den
/// ImGui-Aufrufen in UI/MainWindow.cs übersetzt (unübersichtlich, schwer wartbar) - stattdessen
/// hier ein zentrales Dictionary, aus dem MainWindow über <see cref="Get"/>/<see cref="Format"/>
/// liest.
///
/// Werte mit "{0}"/"{1}" usw. sind Format-Strings für <see cref="string.Format(string, object?[])"/>
/// (siehe <see cref="Format"/>) - die Platzhalter-REIHENFOLGE kann pro Sprache abweichen (falls
/// die Satzstellung das erfordert), die ANZAHL der Platzhalter muss aber für alle 4 Sprachen
/// gleich bleiben, da der Aufrufer in MainWindow dieselben Argumente für alle Sprachen übergibt.
///
/// Neue Sprache hinzufügen: neuer <see cref="DisplayLanguage"/>-Enum-Wert + hier pro
/// <see cref="Key"/> einen weiteren Eintrag ergänzen. Neuer Text: neuer <see cref="Key"/>-Wert +
/// Eintrag mit allen 4 Übersetzungen hier + Verwendung in MainWindow über <see cref="Get"/>/
/// <see cref="Format"/>. Eine fehlende Übersetzung fällt dank des Konsistenz-Checks im statischen
/// Konstruktor SOFORT beim Plugin-Start als Exception auf, nicht erst als leerer/englischer Text
/// mitten in der Session.
/// </summary>
public static class UiStrings
{
    /// <summary>Ein einzelner UI-Text. Absichtlich als Enum statt roher String-Konstanten -
    /// Tippfehler in einem Key fallen so schon beim Compilieren auf, nicht erst zur Laufzeit.</summary>
    public enum Key
    {
        WindowTitle,
        TabParty,
        TabSpellComparison,
        TabLearningPlan,
        TabSync,
        TabWebCompanion,
        TabSettings,
        NoBlueMagesInParty,
        PartyMemberEntry,
        NoPlayerDataLoaded,
        CommonlyMissingHeader,
        AllSpellsKnownByAll,
        SpellFilterHint,
        ColumnNumber,
        ColumnSpell,
        ColumnMissingFor,
        ColumnSources,
        TooltipMissingFor,
        TooltipSourceLine,
        UnknownLocation,
        SourceCountSummary,
        LearnableAtMonstersHeader,
        NoMonsterCoversTwoMissing,
        LearnableAtMonsterCount,
        SpellFallback,
        MonsterFallback,
        SyncIntro,
        DetermineAndExportButton,
        LocalPlayerFallbackName,
        ClipboardCopiedMessage,
        GenericError,
        ImportCodeLabel,
        ImportButton,
        ImportFailed,
        CurrentlyLoadedPlayersHeader,
        NoPlayerDataLoadedShort,
        PlayerSpellCount,
        YouSuffix,
        RemoveButton,
        DevToolHeader,
        DevLoadAliceButton,
        DevLoadBobButton,
        DevLoadCharlesButton,
        DevFixtureLoaded,
        DisplayLanguageHeader,
        DisplayLanguageHint,
        HideTotemsToggle,
        WebCompanionIntro,
        OpenInBrowserButton,
        CopyLinkButton,
        BrowserOpenedMessage,
        LinkCopiedMessage,
    }

    private static readonly Dictionary<Key, Dictionary<DisplayLanguage, string>> Strings = new()
    {
        // "BLUnion" ist der Produktname, wird bewusst NICHT übersetzt (wie z.B. Firefox/Discord
        // auch in jeder Sprache "Firefox"/"Discord" heißen) - läuft trotzdem über Get(), damit
        // der Fenstertitel konsistent über denselben Mechanismus wie alles andere gesetzt wird.
        [Key.WindowTitle] = new()
        {
            [DisplayLanguage.German] = "BLUnion",
            [DisplayLanguage.English] = "BLUnion",
            [DisplayLanguage.French] = "BLUnion",
            [DisplayLanguage.Japanese] = "BLUnion",
        },
        [Key.TabParty] = new()
        {
            [DisplayLanguage.German] = "Party",
            [DisplayLanguage.English] = "Party",
            [DisplayLanguage.French] = "Groupe",
            [DisplayLanguage.Japanese] = "パーティ",
        },
        [Key.TabSpellComparison] = new()
        {
            [DisplayLanguage.German] = "Spell Comparison",
            [DisplayLanguage.English] = "Spell Comparison",
            [DisplayLanguage.French] = "Comparaison des sorts",
            [DisplayLanguage.Japanese] = "スペル比較",
        },
        [Key.TabLearningPlan] = new()
        {
            [DisplayLanguage.German] = "Lernplan",
            [DisplayLanguage.English] = "Learning Plan",
            [DisplayLanguage.French] = "Plan d'apprentissage",
            [DisplayLanguage.Japanese] = "習得プラン",
        },
        [Key.TabSync] = new()
        {
            [DisplayLanguage.German] = "Sync",
            [DisplayLanguage.English] = "Sync",
            [DisplayLanguage.French] = "Synchro",
            [DisplayLanguage.Japanese] = "同期",
        },
        [Key.TabWebCompanion] = new()
        {
            [DisplayLanguage.German] = "Web Companion",
            [DisplayLanguage.English] = "Web Companion",
            [DisplayLanguage.French] = "Web Companion",
            [DisplayLanguage.Japanese] = "Web Companion",
        },
        [Key.TabSettings] = new()
        {
            [DisplayLanguage.German] = "Settings",
            [DisplayLanguage.English] = "Settings",
            [DisplayLanguage.French] = "Paramètres",
            [DisplayLanguage.Japanese] = "設定",
        },
        [Key.NoBlueMagesInParty] = new()
        {
            [DisplayLanguage.German] = "Keine Blue Mages in der aktuellen Party gefunden.",
            [DisplayLanguage.English] = "No Blue Mages found in the current party.",
            [DisplayLanguage.French] = "Aucun Mage bleu trouvé dans le groupe actuel.",
            [DisplayLanguage.Japanese] = "現在のパーティに青魔道士が見つかりません。",
        },
        [Key.PartyMemberEntry] = new()
        {
            [DisplayLanguage.German] = "{0}  (Level {1})",
            [DisplayLanguage.English] = "{0}  (Level {1})",
            [DisplayLanguage.French] = "{0}  (Niveau {1})",
            [DisplayLanguage.Japanese] = "{0}  (レベル{1})",
        },
        // {0} = Name des Sync-Tabs (siehe TabSync) - so bleibt der Hinweis auch dann korrekt,
        // wenn sich die Übersetzung des Tab-Namens mal ändert.
        [Key.NoPlayerDataLoaded] = new()
        {
            [DisplayLanguage.German] =
                "Noch keine Spielerdaten geladen. Gehe zum '{0}'-Tab, um deinen eigenen Status zu " +
                "ermitteln und Codes von Mitspielern zu importieren.",
            [DisplayLanguage.English] =
                "No player data loaded yet. Go to the '{0}' tab to determine your own status and " +
                "import codes from party members.",
            [DisplayLanguage.French] =
                "Aucune donnée de joueur chargée pour l'instant. Va dans l'onglet « {0} » pour " +
                "déterminer ton propre statut et importer les codes de tes coéquipiers.",
            [DisplayLanguage.Japanese] =
                "まだプレイヤーデータが読み込まれていません。「{0}」タブで自分の状況を確認し、" +
                "パーティメンバーのコードをインポートしてください。",
        },
        [Key.CommonlyMissingHeader] = new()
        {
            [DisplayLanguage.German] = "Gemeinsam fehlend:",
            [DisplayLanguage.English] = "Commonly missing:",
            [DisplayLanguage.French] = "Sorts manquants en commun :",
            [DisplayLanguage.Japanese] = "共通して未習得:",
        },
        [Key.AllSpellsKnownByAll] = new()
        {
            [DisplayLanguage.German] = "Alle bekannten Spells sind bei allen geladenen Spielern vorhanden.",
            [DisplayLanguage.English] = "All known spells are already learned by every loaded player.",
            [DisplayLanguage.French] = "Tous les sorts connus sont déjà appris par tous les joueurs chargés.",
            [DisplayLanguage.Japanese] = "既知のスペルはすべて、読み込んだプレイヤー全員が習得済みです。",
        },
        [Key.SpellFilterHint] = new()
        {
            [DisplayLanguage.German] = "Filter: Name oder Nr. (z.B. 58, #058, a)...",
            [DisplayLanguage.English] = "Filter: name or # (e.g. 58, #058, a)...",
            [DisplayLanguage.French] = "Filtre : nom ou n° (p. ex. 58, #058, a)...",
            [DisplayLanguage.Japanese] = "フィルター: 名前または番号 (例: 58、#058、a)...",
        },
        [Key.ColumnNumber] = new()
        {
            [DisplayLanguage.German] = "#",
            [DisplayLanguage.English] = "#",
            [DisplayLanguage.French] = "#",
            [DisplayLanguage.Japanese] = "#",
        },
        [Key.ColumnSpell] = new()
        {
            [DisplayLanguage.German] = "Spell",
            [DisplayLanguage.English] = "Spell",
            [DisplayLanguage.French] = "Sort",
            [DisplayLanguage.Japanese] = "スペル",
        },
        [Key.ColumnMissingFor] = new()
        {
            [DisplayLanguage.German] = "Fehlt bei",
            [DisplayLanguage.English] = "Missing for",
            [DisplayLanguage.French] = "Manque à",
            [DisplayLanguage.Japanese] = "未習得者",
        },
        [Key.ColumnSources] = new()
        {
            [DisplayLanguage.German] = "Quelle(n)",
            [DisplayLanguage.English] = "Source(s)",
            [DisplayLanguage.French] = "Source(s)",
            [DisplayLanguage.Japanese] = "入手先",
        },
        [Key.TooltipMissingFor] = new()
        {
            [DisplayLanguage.German] = "Fehlt bei: {0}",
            [DisplayLanguage.English] = "Missing for: {0}",
            [DisplayLanguage.French] = "Manque à : {0}",
            [DisplayLanguage.Japanese] = "未習得者: {0}",
        },
        // {0} = Monstername (Eigenname, nicht übersetzt), {1} = Method aus sources.json
        // (datengetrieben, ebenfalls nicht Teil dieser Aufgabe), {2} = FormatLocation()-Ergebnis.
        [Key.TooltipSourceLine] = new()
        {
            [DisplayLanguage.German] = "Quelle: {0} ({1}) — {2}",
            [DisplayLanguage.English] = "Source: {0} ({1}) — {2}",
            [DisplayLanguage.French] = "Source : {0} ({1}) — {2}",
            [DisplayLanguage.Japanese] = "入手先: {0} ({1}) — {2}",
        },
        [Key.UnknownLocation] = new()
        {
            [DisplayLanguage.German] = "unbekannt",
            [DisplayLanguage.English] = "unknown",
            [DisplayLanguage.French] = "inconnu",
            [DisplayLanguage.Japanese] = "不明",
        },
        [Key.SourceCountSummary] = new()
        {
            [DisplayLanguage.German] = "{0} Quellen",
            [DisplayLanguage.English] = "{0} sources",
            [DisplayLanguage.French] = "{0} sources",
            [DisplayLanguage.Japanese] = "入手先 {0} 件",
        },
        [Key.LearnableAtMonstersHeader] = new()
        {
            [DisplayLanguage.German] = "Monster, an denen sich mehrere der noch fehlenden Spells gleichzeitig lernen lassen:",
            [DisplayLanguage.English] = "Monsters where you can learn several of your still-missing spells at once:",
            [DisplayLanguage.French] = "Monstres permettant d'apprendre plusieurs sorts manquants en même temps :",
            [DisplayLanguage.Japanese] = "まだ未習得のスペルを同時に複数習得できるモンスター:",
        },
        [Key.NoMonsterCoversTwoMissing] = new()
        {
            [DisplayLanguage.German] = "Aktuell kein Monster, das mindestens 2 gemeinsam fehlende Spells abdeckt.",
            [DisplayLanguage.English] = "Currently no monster covers at least 2 commonly missing spells.",
            [DisplayLanguage.French] = "Aucun monstre ne couvre actuellement au moins 2 sorts manquants en commun.",
            [DisplayLanguage.Japanese] = "現在、共通して未習得のスペルを2つ以上カバーするモンスターはありません。",
        },
        [Key.LearnableAtMonsterCount] = new()
        {
            [DisplayLanguage.German] = "Bei diesem Monster lernbar: {0} fehlende Spells",
            [DisplayLanguage.English] = "Learnable at this monster: {0} missing spells",
            [DisplayLanguage.French] = "Apprenable sur ce monstre : {0} sorts manquants",
            [DisplayLanguage.Japanese] = "このモンスターで習得可能: 未習得スペル{0}個",
        },
        [Key.SpellFallback] = new()
        {
            [DisplayLanguage.German] = "Spell #{0}",
            [DisplayLanguage.English] = "Spell #{0}",
            [DisplayLanguage.French] = "Sort n° {0}",
            [DisplayLanguage.Japanese] = "スペル #{0}",
        },
        [Key.MonsterFallback] = new()
        {
            [DisplayLanguage.German] = "Monster #{0}",
            [DisplayLanguage.English] = "Monster #{0}",
            [DisplayLanguage.French] = "Monstre n° {0}",
            [DisplayLanguage.Japanese] = "モンスター #{0}",
        },
        [Key.SyncIntro] = new()
        {
            [DisplayLanguage.German] =
                "Sync-Option A (MVP): Exportiere deinen eigenen Status als Code und teile ihn z.B. " +
                "über Discord. Mitspieler importieren ihn hier.",
            [DisplayLanguage.English] =
                "Sync option A (MVP): export your own status as a code and share it, e.g. via " +
                "Discord. Party members import it here.",
            [DisplayLanguage.French] =
                "Option de synchro A (MVP) : exporte ton propre statut sous forme de code et " +
                "partage-le, p. ex. via Discord. Les coéquipiers l'importent ici.",
            [DisplayLanguage.Japanese] =
                "同期方式A (MVP): 自分の状況をコードとしてエクスポートし、Discordなどで共有します。" +
                "パーティメンバーはここでインポートします。",
        },
        [Key.DetermineAndExportButton] = new()
        {
            [DisplayLanguage.German] = "Eigenen Status ermitteln + exportieren",
            [DisplayLanguage.English] = "Determine + export own status",
            [DisplayLanguage.French] = "Déterminer + exporter mon statut",
            [DisplayLanguage.Japanese] = "自分の状況を確認してエクスポート",
        },
        [Key.LocalPlayerFallbackName] = new()
        {
            [DisplayLanguage.German] = "Du",
            [DisplayLanguage.English] = "You",
            [DisplayLanguage.French] = "Toi",
            [DisplayLanguage.Japanese] = "自分",
        },
        [Key.ClipboardCopiedMessage] = new()
        {
            [DisplayLanguage.German] = "Code in Zwischenablage kopiert.",
            [DisplayLanguage.English] = "Code copied to clipboard.",
            [DisplayLanguage.French] = "Code copié dans le presse-papiers.",
            [DisplayLanguage.Japanese] = "コードをクリップボードにコピーしました。",
        },
        [Key.GenericError] = new()
        {
            [DisplayLanguage.German] = "Fehler: {0}",
            [DisplayLanguage.English] = "Error: {0}",
            [DisplayLanguage.French] = "Erreur : {0}",
            [DisplayLanguage.Japanese] = "エラー: {0}",
        },
        [Key.ImportCodeLabel] = new()
        {
            [DisplayLanguage.German] = "Code eines Mitspielers",
            [DisplayLanguage.English] = "Party member's code",
            [DisplayLanguage.French] = "Code d'un coéquipier",
            [DisplayLanguage.Japanese] = "パーティメンバーのコード",
        },
        [Key.ImportButton] = new()
        {
            [DisplayLanguage.German] = "Importieren",
            [DisplayLanguage.English] = "Import",
            [DisplayLanguage.French] = "Importer",
            [DisplayLanguage.Japanese] = "インポート",
        },
        [Key.ImportFailed] = new()
        {
            [DisplayLanguage.German] = "Import fehlgeschlagen: {0}",
            [DisplayLanguage.English] = "Import failed: {0}",
            [DisplayLanguage.French] = "Échec de l'import : {0}",
            [DisplayLanguage.Japanese] = "インポートに失敗しました: {0}",
        },
        [Key.CurrentlyLoadedPlayersHeader] = new()
        {
            [DisplayLanguage.German] = "Aktuell geladene Spieler:",
            [DisplayLanguage.English] = "Currently loaded players:",
            [DisplayLanguage.French] = "Joueurs actuellement chargés :",
            [DisplayLanguage.Japanese] = "現在読み込まれているプレイヤー:",
        },
        [Key.NoPlayerDataLoadedShort] = new()
        {
            [DisplayLanguage.German] = "Noch keine Spielerdaten geladen.",
            [DisplayLanguage.English] = "No player data loaded yet.",
            [DisplayLanguage.French] = "Aucune donnée de joueur chargée pour l'instant.",
            [DisplayLanguage.Japanese] = "まだプレイヤーデータが読み込まれていません。",
        },
        [Key.PlayerSpellCount] = new()
        {
            [DisplayLanguage.German] = "{0} ({1} Spells)",
            [DisplayLanguage.English] = "{0} ({1} spells)",
            [DisplayLanguage.French] = "{0} ({1} sorts)",
            [DisplayLanguage.Japanese] = "{0} ({1}スペル)",
        },
        [Key.YouSuffix] = new()
        {
            [DisplayLanguage.German] = " — Du",
            [DisplayLanguage.English] = " — You",
            [DisplayLanguage.French] = " — Toi",
            [DisplayLanguage.Japanese] = " — 自分",
        },
        [Key.RemoveButton] = new()
        {
            [DisplayLanguage.German] = "Entfernen",
            [DisplayLanguage.English] = "Remove",
            [DisplayLanguage.French] = "Retirer",
            [DisplayLanguage.Japanese] = "削除",
        },
        [Key.DevToolHeader] = new()
        {
            [DisplayLanguage.German] = "Dev-Tool (keine echte Party-Funktion):",
            [DisplayLanguage.English] = "Dev tool (not a real party feature):",
            [DisplayLanguage.French] = "Outil dev (pas une vraie fonction de groupe) :",
            [DisplayLanguage.Japanese] = "開発用ツール (実際のパーティ機能ではありません):",
        },
        // "Alice"/"Bob"/"Charles" sind Eigennamen der Dev-Test-Fixtures (siehe
        // Services/DevTestFixtures.cs) - bleiben unübersetzt.
        [Key.DevLoadAliceButton] = new()
        {
            [DisplayLanguage.German] = "Dev: Alice laden",
            [DisplayLanguage.English] = "Dev: Load Alice",
            [DisplayLanguage.French] = "Dev : charger Alice",
            [DisplayLanguage.Japanese] = "開発: Aliceを読み込む",
        },
        [Key.DevLoadBobButton] = new()
        {
            [DisplayLanguage.German] = "Dev: Bob laden",
            [DisplayLanguage.English] = "Dev: Load Bob",
            [DisplayLanguage.French] = "Dev : charger Bob",
            [DisplayLanguage.Japanese] = "開発: Bobを読み込む",
        },
        [Key.DevLoadCharlesButton] = new()
        {
            [DisplayLanguage.German] = "Dev: Charles laden",
            [DisplayLanguage.English] = "Dev: Load Charles",
            [DisplayLanguage.French] = "Dev : charger Charles",
            [DisplayLanguage.Japanese] = "開発: Charlesを読み込む",
        },
        [Key.DevFixtureLoaded] = new()
        {
            [DisplayLanguage.German] = "Dev-Tool: '{0}' mit {1} Spells geladen.",
            [DisplayLanguage.English] = "Dev tool: '{0}' loaded with {1} spells.",
            [DisplayLanguage.French] = "Outil dev : « {0} » chargé avec {1} sorts.",
            [DisplayLanguage.Japanese] = "開発用ツール: 「{0}」を{1}スペルで読み込みました。",
        },
        // Bewusst NICHT mehr "...für Spell-Namen" wie vor dieser Aufgabe - die Auswahl steuert
        // jetzt die komplette Oberfläche, nicht mehr nur die Spell-Namen.
        [Key.DisplayLanguageHeader] = new()
        {
            [DisplayLanguage.German] = "Anzeigesprache:",
            [DisplayLanguage.English] = "Display language:",
            [DisplayLanguage.French] = "Langue d'affichage :",
            [DisplayLanguage.Japanese] = "表示言語:",
        },
        [Key.DisplayLanguageHint] = new()
        {
            [DisplayLanguage.German] =
                "Gilt für die komplette Oberfläche (inkl. Spell-Namen) und nur für die laufende " +
                "Sitzung - wird bewusst nicht gespeichert, beim nächsten Öffnen greift wieder der " +
                "Default anhand deiner Client-Sprache.",
            [DisplayLanguage.English] =
                "Applies to the entire interface (including spell names) and only for the current " +
                "session - intentionally not saved; the default based on your client language " +
                "applies again next time you open the window.",
            [DisplayLanguage.French] =
                "S'applique à toute l'interface (y compris les noms de sorts) et uniquement à la " +
                "session en cours - volontairement non enregistré : le réglage par défaut selon la " +
                "langue de ton client sera réutilisé à la prochaine ouverture.",
            [DisplayLanguage.Japanese] =
                "画面全体(スペル名を含む)に適用され、現在のセッションのみ有効です。意図的に保存され" +
                "ないため、次回開いたときはクライアント言語に基づくデフォルトに戻ります。",
        },
        [Key.HideTotemsToggle] = new()
        {
            [DisplayLanguage.German] = "Totems ausblenden",
            [DisplayLanguage.English] = "Hide totems",
            [DisplayLanguage.French] = "Masquer les totems",
            [DisplayLanguage.Japanese] = "トーテムを非表示",
        },
        // Kürzere Zusammenfassung des README-Abschnitts "Sync without a server" - erklärt die
        // Browser-Version des Sync-Codes (Punkt 1 & 3 der Aufgabenstellung: OHNE laufendes FFXIV
        // nutzbar, gedacht für Freunde ohne installiertes Plugin).
        [Key.WebCompanionIntro] = new()
        {
            [DisplayLanguage.German] =
                "Es gibt auch eine Browser-Version, mit der du deinen Spellstatus als Code " +
                "exportieren oder einen Code importieren kannst - ganz ohne dass FFXIV läuft. " +
                "Praktisch, um Freunden ohne installiertes Plugin die Teilnahme zu ermöglichen.",
            [DisplayLanguage.English] =
                "There's also a browser version where you can export your spell status as a code " +
                "or import one - without FFXIV even running. Handy for letting friends without the " +
                "plugin installed join in.",
            [DisplayLanguage.French] =
                "Il existe aussi une version navigateur qui permet d'exporter ton statut de sorts " +
                "sous forme de code ou d'en importer un - sans même que FFXIV soit lancé. Pratique " +
                "pour permettre à des amis sans le plugin installé d'y participer.",
            [DisplayLanguage.Japanese] =
                "FFXIVを起動していなくても、自分のスペル状況をコードとしてエクスポートしたり、コード" +
                "をインポートしたりできるブラウザ版もあります。プラグインをインストールしていない" +
                "友達も参加しやすくなります。",
        },
        [Key.OpenInBrowserButton] = new()
        {
            [DisplayLanguage.German] = "Im Browser öffnen",
            [DisplayLanguage.English] = "Open in browser",
            [DisplayLanguage.French] = "Ouvrir dans le navigateur",
            [DisplayLanguage.Japanese] = "ブラウザで開く",
        },
        [Key.CopyLinkButton] = new()
        {
            [DisplayLanguage.German] = "Link kopieren",
            [DisplayLanguage.English] = "Copy link",
            [DisplayLanguage.French] = "Copier le lien",
            [DisplayLanguage.Japanese] = "リンクをコピー",
        },
        [Key.BrowserOpenedMessage] = new()
        {
            [DisplayLanguage.German] = "Im Browser geöffnet.",
            [DisplayLanguage.English] = "Opened in browser.",
            [DisplayLanguage.French] = "Ouvert dans le navigateur.",
            [DisplayLanguage.Japanese] = "ブラウザで開きました。",
        },
        [Key.LinkCopiedMessage] = new()
        {
            [DisplayLanguage.German] = "Link in Zwischenablage kopiert.",
            [DisplayLanguage.English] = "Link copied to clipboard.",
            [DisplayLanguage.French] = "Lien copié dans le presse-papiers.",
            [DisplayLanguage.Japanese] = "リンクをクリップボードにコピーしました。",
        },
    };

    /// <summary>Prüft beim ersten Zugriff auf die Klasse (statischer Konstruktor läuft genau
    /// einmal), dass wirklich für JEDEN <see cref="Key"/> und JEDE <see cref="DisplayLanguage"/>
    /// ein Text hinterlegt ist - eine vergessene Übersetzung fällt so sofort beim Plugin-Start
    /// als Exception auf statt erst zur Laufzeit als Lücke im UI.</summary>
    static UiStrings()
    {
        foreach (var key in Enum.GetValues<Key>())
        {
            if (!Strings.TryGetValue(key, out var translations))
                throw new InvalidOperationException($"UiStrings: Kein Eintrag für Key \"{key}\".");

            foreach (var language in Enum.GetValues<DisplayLanguage>())
            {
                if (!translations.ContainsKey(language))
                    throw new InvalidOperationException($"UiStrings: Kein Eintrag für Key \"{key}\" in Sprache \"{language}\".");
            }
        }
    }

    /// <summary>Liefert einen UI-Text unverändert (für Keys ohne Platzhalter).</summary>
    public static string Get(Key key, DisplayLanguage language) => Strings[key][language];

    /// <summary>Liefert einen UI-Text mit über <see cref="string.Format(string, object?[])"/>
    /// eingesetzten Platzhaltern (für dynamische Texte wie Spieleranzahl/Spellnamen).</summary>
    public static string Format(Key key, DisplayLanguage language, params object[] args) =>
        string.Format(Get(key, language), args);
}
