namespace BLUnion.Models;

public enum DisplayLanguage
{
    German,
    English,
    French,
    Japanese,
}

// Gemeinsamer Helper für den an mehreren Stellen wiederkehrenden "vier Sprachvarianten -> die zu
// DisplayLanguage passende auswählen"-Switch (siehe Spell.GetName/Monster.GetName/
// Location.GetZoneName/Loadout.GetName - jeweils identischer Aufbau, nur mit anderen
// Property-Werten). English ist überall der Fallback für einen (aktuell nicht vorkommenden)
// unbekannten DisplayLanguage-Wert.
public static class DisplayLanguageText
{
    public static string Select(DisplayLanguage language, string german, string english, string french, string japanese) => language switch
    {
        DisplayLanguage.German => german,
        DisplayLanguage.English => english,
        DisplayLanguage.French => french,
        DisplayLanguage.Japanese => japanese,
        _ => english,
    };
}
