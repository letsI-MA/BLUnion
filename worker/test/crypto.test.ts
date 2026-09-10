import { describe, expect, it } from "vitest";
import { base64UrlDecode, base64UrlEncode, generateEditToken, sha256Hex } from "../src/crypto";

describe("sha256Hex", () => {
  it("produces the known SHA-256 hex digest for a fixed input", async () => {
    // Bekannter Referenzwert (verifiziert gegen `echo -n "hello" | sha256sum") - stellt sicher,
    // dass wirklich SHA-256 (nicht z.B. SHA-1) verwendet wird, nicht nur "irgendein" Hash.
    const hash = await sha256Hex("hello");
    expect(hash).toBe("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824");
  });

  it("is deterministic for the same input", async () => {
    const a = await sha256Hex("editToken-Beispiel");
    const b = await sha256Hex("editToken-Beispiel");
    expect(a).toBe(b);
  });

  it("produces different hashes for different input", async () => {
    const a = await sha256Hex("tokenA");
    const b = await sha256Hex("tokenB");
    expect(a).not.toBe(b);
  });

  it("handles empty string input", async () => {
    const hash = await sha256Hex("");
    // SHA-256 der leeren Zeichenkette - ebenfalls ein bekannter Referenzwert.
    expect(hash).toBe("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
  });

  it("returns a 64-character lowercase hex string", async () => {
    const hash = await sha256Hex("irgendein Token");
    expect(hash).toMatch(/^[0-9a-f]{64}$/);
  });
});

describe("generateEditToken", () => {
  it("returns a non-empty string", () => {
    expect(generateEditToken().length).toBeGreaterThan(0);
  });

  it("returns different tokens on repeated calls (crypto.getRandomValues, not a fixed value)", () => {
    const tokens = new Set(Array.from({ length: 20 }, () => generateEditToken()));
    expect(tokens.size).toBe(20);
  });

  it("returns URL-safe Base64 without padding (no '+', '/', or '=')", () => {
    const token = generateEditToken();
    expect(token).not.toMatch(/[+/=]/);
  });

  it("round-trips through base64UrlDecode to exactly 32 bytes (256 Bit Entropie, siehe Doku)", () => {
    const token = generateEditToken();
    const decoded = base64UrlDecode(token);
    expect(decoded.length).toBe(32);
  });
});

describe("base64UrlEncode / base64UrlDecode", () => {
  it("round-trips arbitrary byte content", () => {
    const original = new Uint8Array([0, 1, 2, 3, 255, 254, 128, 127, 16, 32]);
    const encoded = base64UrlEncode(original);
    const decoded = base64UrlDecode(encoded);
    expect(Array.from(decoded)).toEqual(Array.from(original));
  });

  it("round-trips an empty byte array", () => {
    const encoded = base64UrlEncode(new Uint8Array(0));
    const decoded = base64UrlDecode(encoded);
    expect(decoded.length).toBe(0);
  });

  it("encodes without standard-Base64 characters ('+', '/') or padding ('=')", () => {
    // Bytes gewählt, damit Standard-Base64 garantiert '+' und '/' enthalten würde.
    const bytesThatWouldProducePlusAndSlash = new Uint8Array([251, 255, 191]);
    const encoded = base64UrlEncode(bytesThatWouldProducePlusAndSlash);
    expect(encoded).not.toMatch(/[+/=]/);
  });

  it("round-trips the 16-byte spell bitmask size used by the rest of the worker", () => {
    const bitmask = new Uint8Array(16).fill(0xaa);
    const decoded = base64UrlDecode(base64UrlEncode(bitmask));
    expect(Array.from(decoded)).toEqual(Array.from(bitmask));
  });

  it("throws for a value with an invalid base64url length (length % 4 === 1)", () => {
    // Laut Implementierung wird bei ungültiger Länge geworfen (BASE64URL_PAD_CHARS[1] ist
    // undefined) - genau dieser Fall wird in index.ts von handlePut über isValidBitmaskBase64
    // abgefangen und als 400 beantwortet, siehe worker-Integrationstests.
    expect(() => base64UrlDecode("abcde")).toThrow("Ungültige Base64url-Länge.");
  });

  it("throws for a value containing characters outside the base64url alphabet", () => {
    expect(() => base64UrlDecode("not valid base64url!!")).toThrow();
  });
});
