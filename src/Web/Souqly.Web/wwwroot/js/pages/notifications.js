import { api, get } from "../api.js";
import { esc, formatDate } from "../format.js";
import { session } from "../session.js";

/** US-15 : les messages envoyés à chaque étape. Ouvrir la page (ou la cloche) les marque tous comme lus. */
export async function notificationsPage(app) {
  const { data } = await get("/api/v1/notifications?size=50");
  const items = data?.content ?? [];
  const fresh = items.filter((n) => !n.read).length;

  app.innerHTML = `
    <div class="order-head">
      <h1>Notifications</h1>
      ${fresh ? `<span class="status status-info">${fresh} nouvelle${fresh > 1 ? "s" : ""}</span>` : ""}
    </div>
    ${items.length === 0 ? `<div class="empty">Aucune notification.</div>` : ""}
    <div class="card list">
      ${items
        .map((n) => `
          <div class="notification ${n.read ? "" : "unread"}">
            <div class="grow">
              <strong>${esc(n.title)}</strong>${n.read ? "" : ` <span class="new-pill">Nouveau</span>`}
              <p>${esc(n.body)}</p>
              <span class="muted small">${formatDate(n.createdAt)}</span>
            </div>
            ${n.kind === "NEW_ORDER"
              ? `<a href="/vendeur/commandes" data-link>Préparer</a>`
              : session.is("CUSTOMER") ? `<a href="/commandes/${n.orderId}" data-link>Voir la commande</a>` : ""}
          </div>`)
        .join("")}
    </div>`;

  // Les nouvelles restent surlignées pendant cette visite, mais sont déjà marquées lues côté serveur.
  if (fresh > 0 || session.unread > 0) await session.markAllRead();
}
