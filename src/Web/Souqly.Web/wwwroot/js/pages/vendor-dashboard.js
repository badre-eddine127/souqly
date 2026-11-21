import { get } from "../api.js";
import { esc, formatDate, formatMoney, shortId } from "../format.js";
import { empty } from "../ui.js";

/**
 * US-17 : tableau de bord du vendeur. Deux appels en parallèle, composés ici :
 * Ordering (ventes du jour, commandes à préparer) et Inventory (ruptures et stocks bas).
 */
export async function vendorDashboardPage(app) {
  const [dashboard, alerts, products] = await Promise.all([
    get("/api/v1/vendor/dashboard"),
    get("/api/v1/vendor/stock-alerts"),
    get("/api/v1/vendor/products?size=100"),
  ]);
  if (!dashboard.ok) {
    app.innerHTML = empty("Tableau de bord indisponible", `<p>${esc(dashboard.error?.detail ?? "Réessayez dans un instant.")}</p>`);
    return;
  }

  const d = dashboard.data;
  const names = Object.fromEntries((products.data?.content ?? []).map((p) => [p.id, p.name]));
  const stock = alerts.data ?? [];
  const day = new Intl.DateTimeFormat("fr-FR", { weekday: "long", day: "numeric", month: "long" }).format(new Date(`${d.day}T12:00:00`));

  const tile = (label, value, hint, tone = "") =>
    `<div class="kpi card ${tone}"><span class="kpi-label">${esc(label)}</span><strong class="kpi-value">${value}</strong><span class="muted small">${esc(hint)}</span></div>`;

  app.innerHTML = `
    <div class="page-head">
      <div><span class="eyebrow">Espace vendeur</span><h1>Tableau de bord</h1><p class="muted">Aujourd'hui, ${esc(day)}</p></div>
      <a class="btn btn-ghost" href="/vendeur/produits" data-link>Gérer mes produits</a>
    </div>

    <div class="kpis">
      ${tile("Ventes du jour", formatMoney(d.salesToday.amount), "commandes payées, vos lignes uniquement", "kpi-main")}
      ${tile("Commandes payées", d.ordersToday, "depuis minuit")}
      ${tile("Articles vendus", d.unitsSoldToday, "unités")}
      ${tile("En attente de paiement", d.awaitingPaymentToday, "passées aujourd'hui", d.awaitingPaymentToday > 0 ? "kpi-wait" : "")}
    </div>

    <div class="dash-grid">
      <section class="card dash-panel">
        <div class="panel-head"><h2>À préparer <span class="pill">${d.toPrepareCount}</span></h2><a href="/vendeur/commandes" data-link>Tout voir →</a></div>
        ${d.toPrepare.length === 0
          ? `<p class="muted">Aucune commande payée en attente d'expédition.</p>`
          : `<ul class="dash-list">${d.toPrepare
              .map((o) => `<li>
                  <div><strong>Commande ${shortId(o.orderId)}</strong><div class="muted small">${formatDate(o.createdAt)} · ${o.lines.map((l) => `${l.quantity} × ${esc(l.productName)}`).join(", ")}</div></div>
                  <span class="price">${formatMoney(o.vendorTotal.amount)}</span>
                </li>`)
              .join("")}</ul>`}
      </section>

      <section class="card dash-panel">
        <div class="panel-head"><h2>Ruptures et stocks bas <span class="pill ${stock.length ? "pill-ko" : ""}">${stock.length}</span></h2><a href="/vendeur/produits" data-link>Réapprovisionner →</a></div>
        ${stock.length === 0
          ? `<p class="muted">Tous vos produits ont du stock. Bravo !</p>`
          : `<ul class="dash-list">${stock
              .map((s) => `<li>
                  <div><strong>${esc(names[s.productId] ?? `Produit ${s.productId}`)}</strong><div class="muted small">${s.onHand} en main · ${s.reserved} réservé${s.reserved > 1 ? "s" : ""}</div></div>
                  ${s.available === 0 ? `<span class="status status-ko">Rupture</span>` : `<span class="status status-wait">Plus que ${s.available}</span>`}
                </li>`)
              .join("")}</ul>`}
      </section>
    </div>`;
}
