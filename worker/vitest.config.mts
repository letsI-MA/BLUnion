import path from "node:path";
import { cloudflareTest, readD1Migrations } from "@cloudflare/vitest-plugin";
import { defineConfig } from "vitest/config";

/**
 * @cloudflare/vitest-plugin (Nachfolger von @cloudflare/vitest-pool-workers für Vitest 4.x, siehe
 * dessen fehlgeschlagenen Ersteinsatz-Build-Versuch/Recherche) statt reinem Miniflare-Setup von
 * Hand - läuft gegen einen ECHTEN lokalen workerd-Prozess (keine reine JS-Simulation), inklusive
 * echtem KV (siehe wrangler.toml BLUNION_PROFILES) und dem nativen Rate-Limiting-Binding
 * WRITE_RATE_LIMITER (siehe enforceWriteRateLimit in src/index.ts) - dadurch werden PUT/DELETE-
 * Handler exakt so getestet, wie sie später auf Cloudflares Edge laufen, statt gegen ein Mock, das
 * bei einem Worker-Runtime-Update stillschweigend abweichen könnte.
 *
 * wrangler.configPath zeigt bewusst auf die ECHTE wrangler.toml (keine separate Test-Config) -
 * dieselben Bindings, kein zweiter, potenziell abweichender Konfigurationsstand.
 */
export default defineConfig(async () => {
  // D1-Migrationen (migrations/) werden vor jedem Testlauf auf die lokale PRESENCE-Datenbank
  // angewendet, siehe test/applyMigrations.ts.
  const migrations = await readD1Migrations(path.join(import.meta.dirname, "migrations"));

  return {
    plugins: [
      cloudflareTest({
        wrangler: { configPath: "./wrangler.toml" },
        // Gruppen-Feature ist in wrangler.toml ausgeschaltet - die bestehenden Gruppen-Tests laufen mit
        // "true"; groupsDisabled.test.ts schaltet für die Aus-Fälle gezielt per env-Mutation um.
        miniflare: { bindings: { GROUPS_ENABLED: "true", TEST_MIGRATIONS: migrations } },
      }),
    ],
    test: { setupFiles: ["./test/applyMigrations.ts"] },
  };
});
