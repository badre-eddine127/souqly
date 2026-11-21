import { get, query } from "../api.js";
import { esc, formatDate } from "../format.js";
import { pager } from "../ui.js";

// Libellé lisible des actions connues (le journal garde le chemin exact).
const ACTIONS = [
  ["POST", /^\/api\/v1\/vendors\/\d+\/approval$/, "Décision sur une boutique"],
  ["POST", /^\/api\/v1\/vendors$/, "Demande d'ouverture de boutique"],
  ["POST", /^\/api\/v1\/products$/, "Création d'un produit"],
  ["PUT", /^\/api\/v1\/products\/\d+$/, "Modification d'un produit"],
  ["DELETE", /^\/api\/v1\/products\/\d+$/, "Archivage d'un produit"],
  ["PUT", /^\/api\/v1\/stock-items\/\d+$/, "Stock déclaré"],
  ["POST", /^\/api\/v1\/stock-items\/\d+\/adjustments$/, "Stock ajusté"],
  ["POST", /^\/api\/v1\/orders$/, "Commande passée"],
  ["POST", /\/cancellation$/, "Commande annulée"],
  ["POST", /\/shipment$/, "Colis expédié"],
  ["POST", /\/delivery$/, "Livraison confirmée"],
];
const describe = (e) => ACTIONS.find(([m, re]) => m === e.method && re.test(e.path))?.[2] ?? "Action";
const tone = (status) => (status < 300 ? "ok" : status === 401 || status === 403 ? "ko" : status < 500 ? "wait" : "ko");
const ROLES = { CUSTOMER: "client", VENDOR: "vendeur", ADMIN: "admin" };

/** US-18 : journal des actions sensibles — qui, quoi, quand — tenu par la passerelle. */
export async function adminAuditPage(app, _match, params) {
  const filters = { user: params.get("user") ?? "", method: params.get("method") ?? "", page: Number(params.get("page") ?? 0) };

  const draw = async () => {
    const result = await get(`/api/v1/admin/audit${query({ ...filters, size: 25 })}`);
    const page = result.data ?? { content: [], page: 0, totalPages: 0, totalElements: 0 };
    app.innerHTML = `
      <div class="page-head">
        <div><span class="eyebrow">Administration</span><h1>Journal d'audit</h1>
          <p class="muted">Toutes les écritures authentifiées qui traversent la passerelle, y compris les tentatives refusées. Ni e-mail ni jeton n'y figurent.</p></div>
        <a class="btn btn-ghost" href="/admin/vendeurs" data-link>Demandes de boutique</a>
      </div>
      <form class="card audit-filters">
        <label>Utilisateur<input name="user" value="${esc(filters.user)}" placeholder="nom de connexion ou identifiant" /></label>
        <label>Verbe<select name="method">${["", "POST", "PUT", "DELETE"].map((m) => `<option value="${m}" ${m === filters.method ? "selected" : ""}>${m || "Tous"}</option>`).join("")}</select></label>
        <button class="btn">Filtrer</button>
        <span class="muted small grow" style="text-align:right">${page.totalElements} entrée${page.totalElements > 1 ? "s" : ""}</span>
      </form>
      ${page.content.length === 0
        ? `<div class="empty">Aucune action enregistrée pour ces filtres.</div>`
        : `<div class="card table-wrap"><table class="audit">
            <thead><tr><th>Quand</th><th>Qui</th><th>Quoi</th><th>Résultat</th><th>Durée</th></tr></thead>
            <tbody>${page.content
              .map((e) => `<tr>
                <td class="nowrap">${formatDate(e.at)}</td>
                <td><strong>${esc(e.username ?? e.userId.slice(0, 8))}</strong><div class="muted small">${esc(e.roles.map((r) => ROLES[r] ?? r).join(", ") || "—")}</div></td>
                <td>${esc(describe(e))}<div class="small"><span class="method method-${e.method.toLowerCase()}">${e.method}</span> <code>${esc(e.path)}</code></div></td>
                <td><span class="status status-${tone(e.status)}">${e.status}</span></td>
                <td class="muted small nowrap">${e.durationMs} ms${e.traceId ? `<div title="Identifiant de trace (Grafana)"><code>${esc(e.traceId.slice(0, 8))}</code></div>` : ""}</td>
              </tr>`)
              .join("")}</tbody></table></div>`}
      ${pager(page.page, page.totalPages)}`;

    app.querySelector(".audit-filters").addEventListener("submit", (e) => {
      e.preventDefault();
      const f = new FormData(e.target);
      filters.user = String(f.get("user")).trim();
      filters.method = String(f.get("method"));
      filters.page = 0;
      draw();
    });
    app.querySelectorAll("[data-page]").forEach((b) => b.addEventListener("click", () => { filters.page = Number(b.dataset.page); draw(); }));
  };

  await draw();
}
