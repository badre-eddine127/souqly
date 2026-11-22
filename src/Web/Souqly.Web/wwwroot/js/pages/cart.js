import { api, get, problemMessage } from "../api.js";
import { esc, formatMoney } from "../format.js";
import { empty, productVisual, toast } from "../ui.js";
import { session } from "../session.js";
import { navigate } from "../nav.js";

/** US-05 (panier) et US-06 / US-09 (validation idempotente : un double clic ne crée qu'une commande). */
export async function cartPage(app) {
  // Une clé d'idempotence par tentative de commande : réutilisée si l'utilisateur clique deux fois ou réessaie.
  let idempotencyKey = crypto.randomUUID();

  const draw = async () => {
    const { data } = await get("/api/v1/carts/me");
    const items = data?.items ?? [];
    if (items.length === 0) {
      app.innerHTML = empty("Votre panier est vide", `<a href="/" data-link class="btn">Découvrir le catalogue</a>`);
      return;
    }

    const lines = await Promise.all(
      items.map(async (i) => ({ productId: Number(i.productId), quantity: Number(i.quantity), product: (await get(`/api/v1/products/${i.productId}`)).data })),
    );
    const total = lines.reduce((sum, l) => sum + Number(l.product?.price.amount ?? 0) * l.quantity, 0);
    const unavailable = lines.some((l) => !l.product || l.product.status !== "ACTIVE");

    app.innerHTML = `
      <h1>Mon panier</h1>
      <div class="cart-layout">
        <div class="card list">
          ${lines
            .map((l) => `
              <div class="cart-line">
                ${productVisual(l.product?.name ?? "?", l.product?.categoryId ?? 0, "sm")}
                <div class="grow">
                  <a href="/produits/${l.productId}" data-link><strong>${esc(l.product?.name ?? `Produit ${l.productId}`)}</strong></a>
                  <div class="muted small">${l.product ? formatMoney(l.product.price.amount) : "Produit indisponible"}</div>
                </div>
                <select data-quantity="${l.productId}" aria-label="Quantité">
                  ${Array.from({ length: 10 }, (_, k) => `<option ${k + 1 === l.quantity ? "selected" : ""}>${k + 1}</option>`).join("")}
                </select>
                <div class="line-total">${l.product ? formatMoney(Number(l.product.price.amount) * l.quantity) : ""}</div>
                <button class="link danger" data-remove="${l.productId}">Retirer</button>
              </div>`)
            .join("")}
        </div>
        <aside class="card summary">
          <h3>Récapitulatif</h3>
          <div class="row"><span>Articles</span><span>${lines.reduce((n, l) => n + l.quantity, 0)}</span></div>
          <div class="row total"><span>Total</span><span>${formatMoney(total)}</span></div>
          <p class="muted small">Le prix est figé au moment de la commande. Paiement de test : refusé au-delà de 10 000,00 MAD.</p>
          <button class="btn btn-lg full" id="checkout" ${unavailable ? "disabled" : ""}>Valider et payer</button>
          ${unavailable ? `<p class="alert alert-ko small">Un article n'est plus disponible : retirez-le pour continuer.</p>` : ""}
        </aside>
      </div>`;

    app.querySelectorAll("[data-quantity]").forEach((s) =>
      s.addEventListener("change", async () => {
        const { ok, error } = await api("PUT", `/api/v1/carts/me/items/${s.dataset.quantity}`, { body: { quantity: Number(s.value) } });
        if (!ok) toast(problemMessage(error), "ko");
        idempotencyKey = crypto.randomUUID();   // le panier a changé : ce sera une nouvelle commande
        await Promise.all([draw(), session.refreshCart()]);
      }),
    );
    app.querySelectorAll("[data-remove]").forEach((b) =>
      b.addEventListener("click", async () => {
        await api("DELETE", `/api/v1/carts/me/items/${b.dataset.remove}`);
        idempotencyKey = crypto.randomUUID();
        await Promise.all([draw(), session.refreshCart()]);
      }),
    );
    app.querySelector("#checkout").addEventListener("click", async (e) => {
      e.target.disabled = true;
      e.target.textContent = "Commande en cours…";
      const { ok, data: order, error } = await api("POST", "/api/v1/orders", {
        headers: { "Idempotency-Key": idempotencyKey },
        body: { lines: lines.map((l) => ({ productId: l.productId, quantity: l.quantity })) },
      });
      if (!ok) {
        e.target.disabled = false;
        e.target.textContent = "Valider et payer";
        return toast(problemMessage(error, "La commande n'a pas pu être passée."), "ko");
      }
      await api("DELETE", "/api/v1/carts/me");
      await session.refreshCart();
      toast("Commande envoyée : réservation du stock et paiement en cours…");
      navigate(`/commandes/${order.id}`);
    });
  };

  await draw();
}
