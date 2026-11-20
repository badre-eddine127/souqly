import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// En développement, /api passe par la passerelle (même origine pour le navigateur : pas de CORS).
// Lancé par Aspire, l'adresse de la passerelle arrive par la découverte de services ; sinon localhost:8080.
const gateway = process.env.services__gateway__http__0 ?? process.env.GATEWAY_URL ?? "http://localhost:8080";

export default defineConfig({
  plugins: [react()],
  build: { rollupOptions: { input: ["index.html", "silent-renew.html"] } },
  server: {
    port: Number(process.env.PORT ?? 5173),
    strictPort: true,
    proxy: { "/api": { target: gateway, changeOrigin: true } },
  },
});
