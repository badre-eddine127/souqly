import { api, get, problemMessage } from "../api.js";
import { CARRIERS, carrierLabel, esc, formatDate, formatMoney, shortId } from "../format.js";
import { modal, statusBadge, toast } from "../ui.js";

const TABS = [
  ["CONFIRMED", "À préparer"],
  ["SHIPPED", "Expédiées"],
  ["DELIVERED", "Livrées"],
  ["", "Toutes"],
];

/** US-13 : commandes payées à préparer, expédition avec numéro de suivi ; livraison simulée (US-14). */
export async function vendorOrdersPage(app) {
  let tab = "CONFIRMED";

  const draw = async () => {
    const orders = (await get(`/api/v1/vendor/orders?size=50${tab ? `&status=${tab}` : ""}`)).data?.content ?? [];
    app.innerHTML = `
      <h1>Commandes à préparer</h1>
      <div class="tabs">${TABS.map(([s, l]) => `<button class="chip ${s === tab ? "active" : ""}" data-tab="${s}">${l}</button>`).join("")}</div>
      ${orders.length === 0 ? `<div class="empty">Rien ici pour le moment.</div>` : ""}
      <div class="stack">
        ${orders
          .map((o) => `
            <div class="card vendor-order">
              <div class="order-head">
                <div><strong>Commande ${shortId(o.orderId)}</strong><div class="muted small">${formatDate(o.createdAt)}</div></div>
                ${statusBadge(o.status)}
              </div>
              <ul class="plain">${o.lines.map((l) => `<li>${l.quantity} × ${esc(l.productName)} <span class="muted">(${formatMoney(l.lineTotal.amount)})</span></li>`).join("")}</ul>
              <div class="order-head">
                <span>Ma part : <strong>${formatMoney(o.vendorTotal.amount)}</strong></span>
                ${o.shipment
                  ? `<span class="muted">${esc(carrierLabel(o.shipment.carrier))} · <code>${esc(o.shipment.trackingNumber)}</code> · ${
                      o.shipment.status === "DELIVERED" ? "livré" : `<button class="link" data-deliver="${o.orderId}">Confirmer la livraison (transporteur simulé)</button>`
                    }</span>`
                  : o.status === "CONFIRMED" ? `<button class="btn" data-ship="${o.orderId}">Marquer expédiée</button>` : ""}
              </div>
            </div>`)
          .join("")}
      </div>`;

    app.querySelectorAll("[data-tab]").forEach((b) =>
      b.addEventListener("click", () => {
        tab = b.dataset.tab;
        draw();
      }),
    );
    app.querySelectorAll("[data-deliver]").forEach((b) =>
      b.addEventListener("click", async () => {
        const { ok, error } = await api("POST", `/api/v1/vendor/orders/${b.dataset.deliver}/delivery`);
        if (!ok) return toast(problemMessage(error), "ko");
        toast("Livraison confirmée — le client est prévenu");
        draw();
      }),
    );
    app.querySelectorAll("[data-ship]").forEach((b) => b.addEventListener("click", () => shipDialog(b.dataset.ship)));
  };

  const shipDialog = (orderId) => {
    const m = modal(`
      <form>
        <h2>Expédier la commande ${shortId(orderId)}</h2>
        <label>Transporteur
          <select name="carrier">${CARRIERS.map((c) => `<option value="${c.code}">${c.label}</option>`).join("")}</select>
        </label>
        <label>Numéro de suivi<input name="tracking" required pattern="[A-Za-z0-9\\-]{6,30}" placeholder="AM123456789MA" /></label>
        <div class="modal-actions">
          <button type="button" class="btn btn-ghost" data-close>Annuler</button>
          <button class="btn">Confirmer l'expédition</button>
        </div>
      </form>`);
    m.root.querySelector("[data-close]").addEventListener("click", m.close);
    m.root.querySelector("form").addEventListener("submit", async (e) => {
      e.preventDefault();
      const f = new FormData(e.target);
      const { ok, error } = await api("POST", `/api/v1/vendor/orders/${orderId}/shipment`, {
        body: { carrier: f.get("carrier"), trackingNumber: String(f.get("tracking")).trim().toUpperCase() },
      });
      if (!ok) return toast(problemMessage(error), "ko");
      toast("Colis expédié — le client reçoit le numéro de suivi");
      m.close();
      draw();
    });
  };

  await draw();
}
