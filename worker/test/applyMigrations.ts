import { applyD1Migrations } from "cloudflare:test";
import { env } from "cloudflare:workers";

// Wendet migrations/ (siehe vitest.config.mts, TEST_MIGRATIONS) auf die lokale PRESENCE-D1 an - läuft vor
// jeder Testdatei; applyD1Migrations überspringt bereits angewendete Migrationen.
const testEnv = env as unknown as { PRESENCE: D1Database; TEST_MIGRATIONS: Parameters<typeof applyD1Migrations>[1] };
await applyD1Migrations(testEnv.PRESENCE, testEnv.TEST_MIGRATIONS);
