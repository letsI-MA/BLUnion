/**
 * Kryptografie-Hilfsfunktionen für die Edit-Token-Verwaltung (siehe README.md/index.ts).
 *
 * Bewusst nur die Web Crypto API (global "crypto", in der Workers-Runtime immer vorhanden) -
 * keine zusätzliche Abhängigkeit nötig, siehe Vorgabe "keine unnötigen Abhängigkeiten".
 */

const BASE64URL_PAD_CHARS: Record<number, string> = { 0: "", 2: "==", 3: "=" };

/** SHA-256 von <paramref>input</paramref>, als Hex-String - für den Vergleich "übergebener
 * Klartext-Token" gegen "gespeicherter Hash" (siehe index.ts: PUT/DELETE). Der Klartext-Token
 * selbst wird NIE gespeichert, siehe Aufgabenstellung. */
export async function sha256Hex(input: string): Promise<string> {
  const bytes = new TextEncoder().encode(input);
  const digest = await crypto.subtle.digest("SHA-256", bytes);
  return Array.from(new Uint8Array(digest))
    .map((b) => b.toString(16).padStart(2, "0"))
    .join("");
}

/** Erzeugt einen neuen, zufälligen Edit-Token: 32 kryptografisch zufällige Bytes (256 Bit
 * Entropie), Base64 URL-safe ohne Padding kodiert - gleiches Kodierungsformat wie die
 * Spell-Bitmaske (siehe README.md), damit der Token problemlos in einer URL/einem Header
 * landen kann, ohne extra escaped werden zu müssen. */
export function generateEditToken(): string {
  const bytes = new Uint8Array(32);
  crypto.getRandomValues(bytes);
  return base64UrlEncode(bytes);
}

export function base64UrlEncode(bytes: Uint8Array): string {
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

/** Wirft, falls <paramref>value</paramref> kein gültiges Base64url ist - Aufrufer (index.ts)
 * fängt das ab und antwortet mit 400. */
export function base64UrlDecode(value: string): Uint8Array {
  const base64 = value.replace(/-/g, "+").replace(/_/g, "/");
  const pad = BASE64URL_PAD_CHARS[base64.length % 4];
  if (pad === undefined)
    throw new Error("Ungültige Base64url-Länge.");

  const binary = atob(base64 + pad);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  return bytes;
}
