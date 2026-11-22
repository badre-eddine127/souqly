import { api, get, problemMessage } from "../api.js";
import { carrierLabel, esc, formatDate, formatMoney, isFinal, REASONS, shortId } from "../format.js";
import { empty, pager, productVisual, statusBadge, toast } from "../ui.js";
import { session } from "../session.js";

/** US-07 : mes commandes. */
export async function ordersPage(app, _, params) {
  const page = Number(params.get("page") ?? 0);
  const { data } = await get(`/api/v1/orders?page=${page}&size=10`);
  const orders = data?.content ?? [];
  if (orders.length === 0) {
    app.innerHTML = empty("Aucune commande pour l'instant", `<a href="/" data-link class="btn">Découvrir le catalogue</a>`);
    return;
  }

  app.innerHTML = `
    <h1>Mes commandes</h1>
    <div class="card list">
      ${orders
        .map((o) => `
          <a href="/commandes/${o.id}" data-link class="order-row">
            <div>
              <strong>Commande ${shortId(o.id)}</strong>
              <div class="muted small">${formatDate(o.createdAt)} · ${o.lines.reduce((n, l) => n + Number(l.quantity), 0)} article(s)</div>
            </div>
            ${statusBadge(o.status)}
            <div class="price">${formatMoney(o.total.amount, o.total.currency)}</div>
          </a>`)
        .join("")}
    </div>
    ${pager(page, Number(data.totalPages))}`;

  app.querySelectorAll("[data-page]").forEach((b) =>
    b.addEventListener("click", () => {
      history.pushState({}, "", `/commandes?page=${b.dataset.page}`);
      window.dispatchEvent(new PopStateEvent("popstate"));
    }),
  );
}

const STEPS = ["PENDING", "STOCK_RESERVED", "CONFIRMED", "SHIPPED", "DELIVERED"];
const STEP_LABELS = ["Reçue", "Stock réservé", "Payée", "Expédiée", "Livrée"];
const PAYMENT_LABELS = { CAPTURED: "Encaissé", DECLINED: "Refusé", REFUNDED: "Remboursé" };

/** US-07 (suivi rafraîchi par interrogation), US-08 (annulation), US-14 (suivi de livraison). */
export async function orderPage(app, [id]) {
  let stopped = false;
  let timer = 0;

  const draw = async () => {
    const [{ data: order }, { data: payment }] = await Promise.all([get(`/api/v1/orders/${id}`), get(`/api/v1/payments/${id}`)]);
    if (stopped) return;
    if (!order) {
      app.innerHTML = empty("Commande introuvable", `<a href="/commandes" data-link>Mes commandes</a>`);
      return;
    }

    const failed = order.status === "REJECTED" || order.status === "CANCELLED";
    const current = STEPS.indexOf(order.status);
    const canCancel = ["PENDING", "STOCK_RESERVED", "CONFIRMED"].includes(order.status) && order.shipments.length === 0;

    app.innerHTML = `
      <a href="/commandes" data-link class="back">← Mes commandes</a>
      <div class="order-head"><h1>Commande ${shortId(order.id)}</h1>${statusBadge(order.status)}</div>
      <p class="muted">Passée le ${formatDate(order.createdAt)}</p>
      ${failed
        ? `<div class="alert alert-ko">Commande ${order.status === "CANCELLED" ? "annulée" : "refusée"}${order.statusReason ? ` : ${esc(REASONS[order.statusReason] ?? order.statusReason)}` : ""}.</div>`
        : `<ol class="timeline">${STEPS.map((s, i) => `<li class="${i < current ? "done" : i === current ? "current" : ""}">${STEP_LABELS[i]}</li>`).join("")}</ol>`}
      <div class="cart-layout">
        <div class="card list">
          ${order.lines
            .map((l) => `
              <div class="cart-line">
                ${productVisual(l.productName, 0, "sm")}
                <div class="grow">
                  <a href="/produits/${l.productId}" data-link><strong>${esc(l.productName)}</strong></a>
                  <div class="muted small">${l.quantity} × ${formatMoney(l.unitPrice.amount)} · vendu par ${esc(session.vendor(l.vendorId).name)}</div>
                </div>
                <div class="line-total">${formatMoney(l.lineTotal.amount)}</div>
              </div>`)
            .join("")}
          <div class="cart-line total-line">
            <div class="grow"><strong>Total</strong></div>
            <div class="line-total"><strong>${formatMoney(order.total.amount, order.total.currency)}</strong></div>
          </div>
        </div>
        <aside class="card summary">
          <h3>Paiement</h3>
          ${payment
            ? `<p>${PAYMENT_LABELS[payment.status] ?? esc(payment.status)} — ${formatMoney(payment.amount, payment.currency)}</p>`
            : `<p class="muted">${failed ? "Aucun paiement." : "En attente…"}</p>`}
          <h3>Livraison</h3>
          ${order.shipments.length === 0
            ? `<p class="muted">${order.status === "CONFIRMED" ? "En préparation chez les vendeurs." : "—"}</p>`
            : order.shipments
                .map((sh) => `
                  <div class="parcel">
                    ${statusBadge(sh.status)}
                    <div><strong>${esc(carrierLabel(sh.carrier))}</strong> · suivi <code>${esc(sh.trackingNumber)}</code></div>
                    <div class="muted small">Expédié le ${formatDate(sh.shippedAt)}${sh.deliveredAt ? ` · livré le ${formatDate(sh.deliveredAt)}` : ""}</div>
                  </div>`)
                .join("")}
          ${canCancel ? `<button class="btn btn-danger full" id="cancel">Annuler la commande</button>` : ""}
        </aside>
      </div>`;

    app.querySelector("#cancel")?.addEventListener("click", async (e) => {
      if (!confirm("Annuler cette commande ? Le paiement sera remboursé.")) return;
      e.target.disabled = true;
      const { ok, error } = await api("POST", `/api/v1/orders/${id}/cancellation`);
      if (!ok) {
        e.target.disabled = false;
        return toast(problemMessage(error), "ko");
      }
      toast("Commande annulée : remboursement en cours.");
      await draw();
    });

    // La saga est asynchrone : on interroge toutes les secondes tant que la commande évolue, puis plus lentement.
    clearTimeout(timer);
    timer = setTimeout(draw, isFinal(order.status) ? 10000 : 1000);
  };

  await draw();
  return () => {
    stopped = true;
    clearTimeout(timer);
  };
}
