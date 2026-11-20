// Génère les types TypeScript du client d'API à partir des contrats OpenAPI versionnés (contracts/openapi).
// Aucun DTO n'est écrit à la main : `npm run api` après chaque changement de contrat.
import { execFileSync } from "node:child_process";
import { readdirSync } from "node:fs";
import { join } from "node:path";

const contracts = join(import.meta.dirname, "..", "..", "contracts", "openapi");
for (const file of readdirSync(contracts).filter((f) => f.endsWith(".json"))) {
  const name = file.replace(".json", "");
  execFileSync("npx", ["openapi-typescript", join(contracts, file), "-o", join("src", "api", "generated", `${name}.ts`)], {
    stdio: "inherit",
    shell: process.platform === "win32",
  });
}
