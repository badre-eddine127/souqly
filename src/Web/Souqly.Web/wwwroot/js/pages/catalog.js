import { api, get, problemMessage, query } from "../api.js";
import { esc, formatMoney } from "../format.js";
import { pager, productVisual, toast } from "../ui.js";
import { categoryImage, HERO_SLIDES, STORY_IMAGE } from "../images.js";
import { session } from "../session.js";
import { navigate } from "../nav.js";

const SORTS = [
  ["", "Nouveautés"],
  ["price,asc", "Prix croissant"],
  ["price,desc", "Prix décroissant"],
  ["name,asc", "Nom (A → Z)"],
];

let categoriesCache = null;
// Après un clic sur une catégorie, une recherche ou un tri, on revient directement sous le diaporama.
let scrollToSouk = false;

/** US-01 : parcourir par univers, avec recherche, tri et pagination (l'état est dans l'adresse : partageable). */
export async function catalogPage(app, _, params) {
  const category = params.get("categorie") ?? "";
  const q = params.get("q") ?? "";
  const sort = params.get("tri") ?? "";
  const page = Number(params.get("page") ?? 0);
  const minPrice = params.get("prixMin") ?? "";
  const maxPrice = params.get("prixMax") ?? "";

  categoriesCache ??= (await get("/api/v1/categories")).data ?? [];
  const categories = categoriesCache;

  // US-04 : avec un texte, la recherche passe par le service Search (tolérant aux fautes, classé par pertinence).
  // S'il est indisponible, repli sur la recherche exacte du catalogue : le site reste utilisable (dégradation gracieuse).
  let result;
  let facets = null;
  let degraded = false;
  if (q) {
    const search = await get(`/api/v1/search${query({ q, category, minPrice, maxPrice, page, size: 12 })}`);
    if (search.ok) {
      facets = search.data.facets;
      result = {
        ok: true,
        data: {
          ...search.data,
          content: search.data.content.map((h) => ({ id: h.productId, vendorId: h.vendorId, categoryId: h.categoryId, name: h.name, description: h.description, price: h.price })),
        },
      };
    } else {
      degraded = search.status === 0 || search.status >= 500;
    }
  }
  result ??= await get(`/api/v1/products${query({ category, q, sort, page, size: 12, minPrice, maxPrice })}`);
  const products = result.data?.content ?? [];
  const total = Number(result.data?.totalElements ?? 0);
  const stock = Object.fromEntries(
    await Promise.all(products.map(async (p) => [p.id, (await get(`/api/v1/stock-items/${p.id}`)).data])),
  );

  // Chaque univers suivi de ses sous-catégories (Artisanat, Tapis, Poterie, Maroquinerie, Cosmétique, Argan…).
  const ordered = categories
    .filter((c) => c.parentId == null)
    .flatMap((root) => [root, ...categories.filter((c) => c.parentId === root.id)]);
  const selected = categories.find((c) => String(c.id) === String(category));
  const title = selected ? selected.name : q ? `Résultats pour « ${q} »` : "Coups de cœur du souk";

  app.innerHTML = `
    <section class="carousel" aria-roledescription="carrousel" aria-label="À la une">
      ${HERO_SLIDES.map((sl, i) => `
        <div class="slide ${i === 0 ? "active" : ""}" role="group" aria-roledescription="diapositive" aria-label="${i + 1} sur ${HERO_SLIDES.length}" aria-hidden="${i !== 0}">
          <div class="slide-photo" style="background-image:url('${sl.image}')"></div>
          <div class="slide-content">
            <div class="eyebrow">${esc(sl.eyebrow)}</div>
            <h1>${sl.title}</h1>
            <p>${esc(sl.text)}</p>
            ${i === 0
              ? `<form class="searchbox" data-search>
                  <input name="q" type="search" value="${esc(q)}" placeholder="Que recherchez-vous aujourd'hui ? (ex. tapis, poterie…)" aria-label="Rechercher un produit" />
                  <button class="btn">Explorer le souk&nbsp; →</button>
                </form>
                <div class="trustline"><span>Artisans sélectionnés</span><span>Paiement sécurisé</span><span>Livraison suivie</span></div>`
              : `<button class="btn btn-clay btn-lg" data-category="${sl.category}">Découvrir ${esc(sl.eyebrow.toLowerCase())}&nbsp; →</button>`}
          </div>
        </div>`).join("")}
      <div class="carousel-dots">${HERO_SLIDES.map((_, i) => `<button class="${i === 0 ? "active" : ""}" data-slide="${i}" aria-label="Aller à la diapositive ${i + 1}"></button>`).join("")}</div>
      <button class="scroll-cue" aria-label="Voir les catégories et les produits">Découvrir le souk<span>↓</span></button>
    </section>

    <div id="souk"></div>
    <section>
      <div class="section-head">
        <div><h2>Explorer par univers</h2><p>Découvrez les métiers et matières emblématiques du Maroc.</p></div>
      </div>
      <div class="category-row">
        <button class="category all ${category === "" ? "selected" : ""}" data-category=""><span>✦</span><strong>Tout découvrir</strong></button>
        ${ordered
          .map((c) => `<button class="category ${String(c.id) === String(category) ? "selected" : ""}" data-category="${c.id}" style="background-image:url('${categoryImage(c.id, 400)}')"><strong>${esc(c.name)}</strong></button>`)
          .join("")}
      </div>
    </section>

    <section>
      <div class="catalog-head">
        <div>
          <h2>${esc(title)}</h2>
          <p>${total} produit${total > 1 ? "s" : ""}${q ? ` · <button class="link" id="clear">effacer la recherche</button>` : " dans cette sélection"}</p>
        </div>
        <div class="catalog-actions">
          <form class="price-filter" data-price>
            <label>Prix (MAD)<span><input name="prixMin" type="number" min="0" step="1" inputmode="numeric" value="${esc(minPrice)}" placeholder="min" aria-label="Prix minimum" />–<input name="prixMax" type="number" min="0" step="1" inputmode="numeric" value="${esc(maxPrice)}" placeholder="max" aria-label="Prix maximum" /></span></label>
            <button class="btn btn-ghost btn-sm">OK</button>
          </form>
          ${q && facets
            ? `<span class="relevance">Classés par pertinence</span>`
            : `<label>Trier par
            <select id="sort">${SORTS.map(([v, l]) => `<option value="${v}" ${v === sort ? "selected" : ""}>${l}</option>`).join("")}</select>
          </label>`}
        </div>
      </div>
      ${facets && facets.categories.length > 1
        ? `<div class="facets">${facets.categories
            .map((f) => {
              const c = categories.find((x) => x.id === f.categoryId);
              return `<button class="chip ${String(f.categoryId) === String(category) ? "active" : ""}" data-facet="${f.categoryId}">${esc(c?.name ?? `Catégorie ${f.categoryId}`)} <b>${f.count}</b></button>`;
            })
            .join("")}${category ? `<button class="chip" data-facet="">Toutes</button>` : ""}</div>`
        : ""}
      ${degraded ? `<div class="alert alert-info">La recherche intelligente est momentanément indisponible : résultats exacts du catalogue.</div>` : ""}
      ${!result.ok ? `<div class="alert alert-ko">${esc(result.status === 502 || result.status === 504 ? problemMessage(result.error) : "Le catalogue est indisponible pour le moment.")}</div>` : ""}
      ${result.ok && products.length === 0 ? `<div class="empty">Aucun produit trouvé. Essayez une autre recherche.</div>` : ""}
      <div class="products">
        ${products
          .map((p) => {
            const v = session.vendor(p.vendorId);
            const available = Number(stock[p.id]?.available ?? 0);
            const tag = available === 0 ? `<span class="tag out">Épuisé</span>` : available <= 3 ? `<span class="tag">Plus que ${available}</span>` : `<span class="tag">Fait main</span>`;
            const canBuy = available > 0 && !(session.is("VENDOR") && !session.is("CUSTOMER"));
            return `
              <a href="/produits/${p.id}" data-link class="product">
                <div class="flip3d">
                  <div class="flip3d-inner">
                    <div class="flip3d-face">${productVisual(p.name, p.categoryId)}${tag}</div>
                    <div class="flip3d-face flip3d-back" aria-hidden="true">
                      ${productVisual(p.name, p.categoryId)}
                      <div class="flip3d-info">
                        <span class="artisan">Fait main</span>
                        <p>${esc((p.description || "Pièce artisanale du Maroc.").slice(0, 110))}</p>
                        <span class="flip3d-cta">Voir le produit →</span>
                      </div>
                    </div>
                  </div>
                </div>
                <div class="product-info">
                  <div class="artisan">${esc(v.name)}</div>
                  <h3>${esc(p.name)}</h3>
                  <div class="location">⌖ ${v.city ? `${esc(v.city)}, ` : ""}Maroc</div>
                  <div class="product-bottom">
                    <span class="price">${formatMoney(p.price.amount, p.price.currency)}</span>
                    <button class="add" data-add="${p.id}" data-name="${esc(p.name)}" ${canBuy ? "" : "disabled"}>Ajouter +</button>
                  </div>
                </div>
              </a>`;
          })
          .join("")}
      </div>
      ${pager(page, Number(result.data?.totalPages ?? 0))}
    </section>

    <section class="story">
      <div class="story-text">
        <h2>Chaque création a une histoire.</h2>
        <p>Rencontrez les artisans, découvrez leurs ateliers et soutenez le fait-main marocain. Chaque commande est payée en sécurité et suivie jusqu'à votre porte.</p>
        ${session.user.authenticated ? "" : `<button class="btn" data-action="login">Rejoindre le souk&nbsp; →</button>`}
      </div>
      <div class="story-photo" style="background-image:url('${STORY_IMAGE}')"></div>
    </section>`;

  const update = (changes) => {
    const next = new URLSearchParams(params);
    for (const [k, v] of Object.entries(changes)) (v === "" || v == null ? next.delete(k) : next.set(k, v));
    if (!("page" in changes)) next.delete("page");
    const s = next.toString();
    scrollToSouk = true;
    navigate(`/${s ? `?${s}` : ""}`);
  };

  app.querySelector("[data-search]").addEventListener("submit", (e) => {
    e.preventDefault();
    update({ q: new FormData(e.currentTarget).get("q") });
  });
  app.querySelector("#sort")?.addEventListener("change", (e) => update({ tri: e.target.value }));
  app.querySelector("[data-price]").addEventListener("submit", (e) => {
    e.preventDefault();
    const f = new FormData(e.currentTarget);
    update({ prixMin: String(f.get("prixMin") ?? "").trim(), prixMax: String(f.get("prixMax") ?? "").trim() });
  });
  app.querySelectorAll("[data-facet]").forEach((b) => b.addEventListener("click", () => update({ categorie: b.dataset.facet })));
  app.querySelector("#clear")?.addEventListener("click", () => update({ q: "" }));
  app.querySelectorAll("[data-category]").forEach((b) => b.addEventListener("click", () => update({ categorie: b.dataset.category })));
  app.querySelectorAll("[data-page]").forEach((b) => b.addEventListener("click", () => update({ page: b.dataset.page })));

  // « Ajouter + » directement depuis la carte (sans ouvrir la fiche).
  app.querySelectorAll("[data-add]").forEach((b) =>
    b.addEventListener("click", async (e) => {
      e.preventDefault();
      e.stopPropagation();
      if (!session.user.authenticated) return session.login();
      b.disabled = true;
      const id = b.dataset.add;
      const cart = await get("/api/v1/carts/me");
      const already = Number(cart.data?.items?.find((i) => String(i.productId) === String(id))?.quantity ?? 0);
      const { ok, error } = await api("PUT", `/api/v1/carts/me/items/${id}`, { body: { quantity: Math.min(10, already + 1) } });
      b.disabled = false;
      if (!ok) return toast(problemMessage(error), "ko");
      await session.refreshCart();
      toast(`${b.dataset.name} ajouté au panier`);
    }),
  );

  if (scrollToSouk) {
    scrollToSouk = false;
    app.querySelector("#souk").scrollIntoView();
  }
  app.querySelector(".scroll-cue").addEventListener("click", () => app.querySelector("#souk").scrollIntoView({ behavior: "smooth" }));
  return startCarousel(app.querySelector(".carousel"));
}

/** Diaporama automatique : une diapositive toutes les 5 s, en boucle ; les points et le glissement au doigt restent possibles. */
function startCarousel(root) {
  const slides = [...root.querySelectorAll(".slide")];
  const dots = [...root.querySelectorAll("[data-slide]")];
  let current = 0;
  let timer = 0;

  const show = (index) => {
    current = (index + slides.length) % slides.length;
    slides.forEach((s, i) => {
      s.classList.toggle("active", i === current);
      s.setAttribute("aria-hidden", String(i !== current));
    });
    dots.forEach((d, i) => d.classList.toggle("active", i === current));
  };
  const play = () => {
    clearInterval(timer);
    timer = setInterval(() => show(current + 1), 5000);
  };
  dots.forEach((d) => d.addEventListener("click", () => { show(Number(d.dataset.slide)); play(); }));
  // Pause seulement pendant la saisie dans la recherche, pour ne pas faire disparaître le champ.
  root.addEventListener("focusin", (e) => { if (e.target.matches("input")) clearInterval(timer); });
  root.addEventListener("focusout", (e) => { if (e.target.matches("input")) play(); });

  let touchX = null;
  root.addEventListener("touchstart", (e) => { touchX = e.touches[0].clientX; }, { passive: true });
  root.addEventListener("touchend", (e) => {
    if (touchX === null) return;
    const dx = e.changedTouches[0].clientX - touchX;
    if (Math.abs(dx) > 50) { show(current + (dx < 0 ? 1 : -1)); play(); }
    touchX = null;
  });

  play();
  // Nettoyage quand on quitte l'accueil (le routeur appelle cette fonction).
  return () => clearInterval(timer);
}
