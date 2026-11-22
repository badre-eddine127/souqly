import { api, get, problemMessage } from "../api.js";
import { esc, formatMoney } from "../format.js";
import { empty, toast } from "../ui.js";
import { session } from "../session.js";
import { productImage } from "../images.js";
import { mountSpin } from "../spin3d.js";

/** US-02 : fiche produit (prix, vendeur, disponibilité) et ajout au panier (US-05). */
export async function productPage(app, [id]) {
  const [{ data: product }, { data: availability }] = await Promise.all([
    get(`/api/v1/products/${id}`),
    get(`/api/v1/stock-items/${id}`),
  ]);
  if (!product) {
    app.innerHTML = empty("Produit introuvable", `<a href="/" data-link>Retour au catalogue</a>`);
    return;
  }

  const available = Number(availability?.available ?? 0);
  const vendor = session.vendor(product.vendorId);
  const max = Math.min(10, available);
  const vendorOnly = session.is("VENDOR") && !session.is("CUSTOMER");
  const statusTag = product.status === "ACTIVE" ? "" : `<span class="status status-wait">${product.status === "DRAFT" ? "Brouillon" : "Archivé"}</span>`;

  app.innerHTML = `
    <a href="/" data-link class="back">← Retour au souk</a>
    <div class="product-layout">
      <div class="showcase">
        <div class="turntable" title="Glissez pour faire tourner le produit">
          <div class="spin">
            <div class="face face-front">
              <img src="${productImage(product.name, product.categoryId, 1200)}" alt="${esc(product.name)}" draggable="false" onerror="this.remove()" />
              <div class="face-light" aria-hidden="true"></div>
              <span class="face-tag">Face</span>
            </div>
            <div class="face face-back" aria-hidden="true">
              <img src="${productImage(product.name, product.categoryId, 1200)}" alt="" draggable="false" onerror="this.remove()" />
              <div class="face-light"></div>
              <div class="back-card">
                <span class="artisan">Fait main · ${esc(vendor.city || "Maroc")}</span>
                <strong>${esc(product.name)}</strong>
                <span class="muted small">${esc(vendor.name)} · réf. ${esc(product.sku)}</span>
                <span class="price">${formatMoney(product.price.amount, product.price.currency)}</span>
              </div>
              <span class="face-tag">Dos · détail</span>
            </div>
          </div>
          <div class="turntable-shadow" aria-hidden="true"></div>
        </div>
        <div class="showcase-actions">
          <span class="muted small">⟲ Glissez pour faire tourner</span>
          <button class="btn btn-ghost" data-flip type="button">Voir l'autre côté</button>
        </div>
      </div>
      <div class="product-info-3d">
        ${statusTag}
        <h1>${esc(product.name)}</h1>
        <p class="price big">${formatMoney(product.price.amount, product.price.currency)}</p>
        <p class="description">${esc(product.description || "Pas de description.")}</p>
        <div class="seller">
          <div class="avatar">${esc(vendor.name.charAt(0))}</div>
          <div><div class="artisan">Vendu par</div><strong>${esc(vendor.name)}</strong><div class="muted small">⌖ ${vendor.city ? `${esc(vendor.city)}, ` : ""}Maroc · réf. ${esc(product.sku)}</div></div>
        </div>
        <div class="stock ${available > 0 ? "in" : "out"}">
          ${availability == null ? "Disponibilité inconnue" : available > 0 ? `En stock : ${available} disponible${available > 1 ? "s" : ""}` : "Rupture de stock"}
        </div>
        ${vendorOnly
          ? `<p class="muted">Connecté en vendeur : les achats se font avec un compte client.</p>`
          : `<div class="buy">
              <label>Quantité
                <select id="quantity" ${max === 0 ? "disabled" : ""}>
                  ${Array.from({ length: Math.max(1, max) }, (_, i) => `<option>${i + 1}</option>`).join("")}
                </select>
              </label>
              <button class="btn btn-clay btn-lg" id="add" ${available === 0 || product.status !== "ACTIVE" ? "disabled" : ""}>
                ${session.user.authenticated ? "Ajouter au panier" : "Se connecter pour acheter"}
              </button>
            </div>`}
        <div class="perks">
          <div><strong>Fait main</strong>par un artisan sélectionné</div>
          <div><strong>Paiement sécurisé</strong>remboursé si vous annulez</div>
          <div><strong>Livraison suivie</strong>Amana, CTM, Aramex, DHL</div>
        </div>
      </div>
    </div>`;

  app.querySelector("#add")?.addEventListener("click", async (e) => {
    if (!session.user.authenticated) return session.login();
    e.target.disabled = true;
    const quantity = Number(app.querySelector("#quantity").value);
    const cart = await get("/api/v1/carts/me");
    const already = Number(cart.data?.items?.find((i) => String(i.productId) === String(id))?.quantity ?? 0);
    const { ok, error } = await api("PUT", `/api/v1/carts/me/items/${id}`, { body: { quantity: Math.min(10, already + quantity) } });
    e.target.disabled = false;
    if (!ok) return toast(problemMessage(error), "ko");
    await session.refreshCart();
    toast(`${quantity} × ${product.name} ajouté au panier`);
  });

  // Le produit tourne en 3D ; le routeur appelle ce nettoyage en quittant la page.
  return mountSpin(app.querySelector(".turntable"));
}
