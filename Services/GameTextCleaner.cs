using System.Text;
using Lumina.Text.Payloads;
using Lumina.Text.ReadOnly;

namespace BLUnion.Services;

// Wandelt SeStrings aus den Spieldaten in lesbaren Klartext für ImGui um: Text bleibt, NewLine wird
// "\n", NonBreakingSpace/Hyphen werden zu ihren Zeichen, alle übrigen Makros (Farbe, Kursiv,
// SoftHyphen, ...) entfallen. In AozActionTransient.Description kommen laut tools/data-enrich/
// report.md nur NewLine, NonBreakingSpace, SoftHyphen und Italic vor.
public static class GameTextCleaner
{
    public static string ToPlainText(ReadOnlySeString value)
    {
        var text = new StringBuilder();

        foreach (var payload in value)
        {
            switch (payload.Type)
            {
                case ReadOnlySePayloadType.Text:
                    text.Append(Encoding.UTF8.GetString(payload.Body.Span));
                    break;

                case ReadOnlySePayloadType.Macro:
                    switch (payload.MacroCode)
                    {
                        case MacroCode.NewLine:
                            text.Append('\n');
                            break;
                        case MacroCode.NonBreakingSpace:
                            text.Append('\u00A0');
                            break;
                        case MacroCode.Hyphen:
                            text.Append('-');
                            break;
                    }

                    break;
            }
        }

        // "\r\n" aus Textpayloads vereinheitlichen, damit ImGui keine doppelten Umbrüche zeigt.
        return text.ToString().Replace("\r\n", "\n").Trim();
    }
}
