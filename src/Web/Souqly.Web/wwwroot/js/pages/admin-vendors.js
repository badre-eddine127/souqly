import { api, get, problemMessage } from "../api.js";
import { esc, formatDate } from "../format.js";
import { modal, toast } from "../ui.js";

const TABS = [
  ["PENDING", "À traiter"],
  ["APPROVED", "Validées"],
  ["REJECTED", "Refusées"],
];

/** US-16 : l'équipe Souqly valide ou refuse les demandes d'ouverture de boutique. */
export async function adminVendorsPage(app) {
  let tab = "PENDING";

  const draw = async () => {
    const page = (await get(`/api/v1/vendors?status=${tab}&size=50`)).data;
    const items = page?.content ?? [];
    app.innerHTML = `
      <div class="page-head">
        <div><span class="eyebrow">Administration</span><h1>Demandes de boutique</h1>
          <p class="muted">Une boutique validée reçoit le rôle vendeur dans Keycloak ; chaque décision est inscrite au journal d'audit.</p></div>
        <a class="btn btn-ghost" href="/admin/journal" data-link>Journal d'audit</a>
      </div>
      <div class="tabs">${TABS.map(([s, l]) => `<button class="chip ${s === tab ? "active" : ""}" data-tab="${s}">${l}</button>`).join("")}</div>
      ${items.length === 0 ? `<div class="empty">Aucune demande ${tab === "PENDING" ? "à traiter" : "dans cette liste"}.</div>` : ""}
      <div class="stack">
        ${items
          .map((a) => `
            <article class="card application">
              <div class="order-head">
                <div><h3>${esc(a.shopName)}</h3><div class="muted small">${esc(a.city)} · demandée le ${formatDate(a.submittedAt)} · compte <code>${esc(a.userId.slice(0, 8))}</code></div></div>
                ${a.status === "PENDING"
                  ? `<div class="actions"><button class="btn btn-ghost" data-reject="${a.id}">Refuser</button><button class="btn btn-clay" data-approve="${a.id}">Valider la boutique</button></div>`
                  : `<span class="status status-${a.status === "APPROVED" ? "ok" : "ko"}">${a.status === "APPROVED" ? "Validée" : "Refusée"}${a.decidedAt ? ` le ${formatDate(a.decidedAt)}` : ""}</span>`}
              </div>
              ${a.description ? `<p>${esc(a.description)}</p>` : `<p class="muted">Pas de description.</p>`}
              ${a.rejectionReason ? `<p class="small"><strong>Motif du refus :</strong> ${esc(a.rejectionReason)}</p>` : ""}
            </article>`)
          .join("")}
      </div>`;

    app.querySelectorAll("[data-tab]").forEach((b) => b.addEventListener("click", () => { tab = b.dataset.tab; draw(); }));
    app.querySelectorAll("[data-approve]").forEach((b) =>
      b.addEventListener("click", async () => {
        b.disabled = true;
        const { ok, error } = await api("POST", `/api/v1/vendors/${b.dataset.approve}/approval`, { body: { approved: true } });
        if (!ok) {
          b.disabled = false;
          return toast(problemMessage(error), "ko");
        }
        toast("Boutique validée — le rôle vendeur est attribué");
        draw();
      }),
    );
    app.querySelectorAll("[data-reject]").forEach((b) => b.addEventListener("click", () => rejectDialog(b.dataset.reject)));
  };

  const rejectDialog = (id) => {
    const m = modal(`
      <form>
        <h2>Refuser la demande</h2>
        <label>Motif (envoyé au demandeur)<textarea name="reason" rows="4" maxlength="500" required placeholder="Merci de décrire vos produits et votre savoir-faire."></textarea></label>
        <div class="modal-actions">
          <button type="button" class="btn btn-ghost" data-close>Annuler</button>
          <button class="btn btn-danger">Refuser</button>
        </div>
      </form>`);
    m.root.querySelector("[data-close]").addEventListener("click", m.close);
    m.root.querySelector("form").addEventListener("submit", async (e) => {
      e.preventDefault();
      const reason = String(new FormData(e.target).get("reason")).trim();
      const { ok, error } = await api("POST", `/api/v1/vendors/${id}/approval`, { body: { approved: false, reason } });
      if (!ok) return toast(problemMessage(error), "ko");
      toast("Demande refusée");
      m.close();
      draw();
    });
  };

  await draw();
}
