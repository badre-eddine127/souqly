import { api, get, problemMessage } from "../api.js";
import { esc, formatMoney } from "../format.js";
import { modal, productVisual, toast } from "../ui.js";

const STATUS_LABELS = { PUBLISHED: ["Publié", "ok"], DRAFT: ["Brouillon", "wait"], ARCHIVED: ["Archivé", "ko"] };

/** US-03 (mes produits : créer, modifier, archiver) et US-10 (déclarer et ajuster mon stock). */
export async function vendorProductsPage(app) {
  const categories = (await get("/api/v1/categories")).data ?? [];

  const draw = async () => {
    const products = (await get("/api/v1/vendor/products?size=100")).data?.content ?? [];
    const stocks = await Promise.all(products.map(async (p) => (await get(`/api/v1/stock-items/${p.id}/details`)).data));
    const rows = products.map((p, i) => ({ ...p, stock: stocks[i] }));

    app.innerHTML = `
      <div class="order-head">
        <h1>Mes produits</h1>
        <button class="btn" id="new">+ Nouveau produit</button>
      </div>
      ${rows.length === 0 ? `<div class="empty">Vous n'avez pas encore de produit.</div>` : `
      <div class="card">
        <table class="table">
          <thead><tr><th></th><th>Produit</th><th>Prix</th><th>Statut</th><th>Stock (dispo / réservé)</th><th></th></tr></thead>
          <tbody>
            ${rows
              .map((p) => {
                const [label, tone] = STATUS_LABELS[p.status] ?? [p.status, "info"];
                const stock = p.stock
                  ? `${p.stock.available} / ${p.stock.reserved}${Number(p.stock.available) <= 2 ? ` <span class="warn">⚠ bas</span>` : ""}`
                  : `<span class="muted">non déclaré</span>`;
                return `<tr>
                  <td>${productVisual(p.name, p.categoryId, "sm")}</td>
                  <td><a href="/produits/${p.id}" data-link><strong>${esc(p.name)}</strong></a><div class="muted small">${esc(p.sku)}</div></td>
                  <td>${formatMoney(p.price.amount)}</td>
                  <td><span class="status status-${tone}">${label}</span></td>
                  <td>${stock}</td>
                  <td class="actions">${p.status === "ARCHIVED" ? "" : `
                    <button class="link" data-stock="${p.id}">Stock</button>
                    <button class="link" data-edit="${p.id}">Modifier</button>
                    <button class="link danger" data-archive="${p.id}">Archiver</button>`}
                  </td>
                </tr>`;
              })
              .join("")}
          </tbody>
        </table>
      </div>`}`;

    const byId = (id) => rows.find((r) => String(r.id) === String(id));
    app.querySelector("#new").addEventListener("click", () =>
      productForm({ categoryId: 2, sku: `SKU-${Date.now().toString(36).toUpperCase()}`, name: "", description: "", price: "", publish: true }),
    );
    app.querySelectorAll("[data-edit]").forEach((b) =>
      b.addEventListener("click", () => {
        const p = byId(b.dataset.edit);
        productForm({ id: p.id, categoryId: p.categoryId, sku: p.sku, name: p.name, description: p.description, price: p.price.amount, publish: p.status === "PUBLISHED" });
      }),
    );
    app.querySelectorAll("[data-archive]").forEach((b) =>
      b.addEventListener("click", async () => {
        const p = byId(b.dataset.archive);
        if (!confirm(`Archiver « ${p.name} » ? Il disparaîtra du catalogue.`)) return;
        const { ok, error } = await api("DELETE", `/api/v1/products/${p.id}`);
        if (!ok) return toast(problemMessage(error), "ko");
        toast("Produit archivé");
        draw();
      }),
    );
    app.querySelectorAll("[data-stock]").forEach((b) => b.addEventListener("click", () => stockDialog(byId(b.dataset.stock))));
  };

  const productForm = (d) => {
    const m = modal(`
      <form id="product-form">
        <h2>${d.id ? "Modifier le produit" : "Nouveau produit"}</h2>
        <label>Nom<input name="name" required value="${esc(d.name)}" /></label>
        <div class="two">
          <label>Catégorie
            <select name="categoryId">${categories
              .map((c) => `<option value="${c.id}" ${String(c.id) === String(d.categoryId) ? "selected" : ""}>${c.parentId ? "— " : ""}${esc(c.name)}</option>`)
              .join("")}</select>
          </label>
          <label>Prix (MAD)<input name="price" required inputmode="decimal" value="${esc(d.price)}" placeholder="1200,00" /></label>
        </div>
        <label>Référence (SKU)<input name="sku" required value="${esc(d.sku)}" /></label>
        <label>Description<textarea name="description" rows="4">${esc(d.description)}</textarea></label>
        <label class="check"><input type="checkbox" name="publish" ${d.publish ? "checked" : ""} /> Publié dans le catalogue</label>
        <div class="modal-actions">
          <button type="button" class="btn btn-ghost" data-close>Annuler</button>
          <button class="btn">Enregistrer</button>
        </div>
      </form>`);
    m.root.querySelector("[data-close]").addEventListener("click", m.close);
    m.root.querySelector("form").addEventListener("submit", async (e) => {
      e.preventDefault();
      const f = new FormData(e.target);
      const body = {
        categoryId: Number(f.get("categoryId")),
        sku: f.get("sku"),
        name: f.get("name"),
        description: f.get("description"),
        price: { amount: Number(String(f.get("price")).replace(/\s/g, "").replace(",", ".")).toFixed(2), currency: "MAD" },
        publish: f.get("publish") === "on",
      };
      const { ok, error } = d.id ? await api("PUT", `/api/v1/products/${d.id}`, { body }) : await api("POST", "/api/v1/products", { body });
      if (!ok) return toast(problemMessage(error), "ko");
      toast(d.id ? "Produit mis à jour" : "Produit créé — pensez à déclarer son stock");
      m.close();
      draw();
    });
  };

  const stockDialog = (p) => {
    const m = modal(`
      <h2>Stock — ${esc(p.name)}</h2>
      ${p.stock ? `<p class="muted">En main : ${p.stock.onHand} · réservé : ${p.stock.reserved} · disponible : ${p.stock.available}</p>` : ""}
      <h3>Déclarer le stock physique</h3>
      <div class="inline"><input type="number" min="0" id="onhand" value="${p.stock?.onHand ?? 0}" /><button class="btn" id="declare">Enregistrer</button></div>
      ${p.stock ? `
        <h3>Ajuster (entrée, casse, inventaire…)</h3>
        <div class="inline">
          <input type="number" id="delta" placeholder="-3 ou +10" />
          <input id="reason" placeholder="Motif" />
          <button class="btn" id="adjust">Ajuster</button>
        </div>` : ""}
      <div class="modal-actions"><button class="btn btn-ghost" data-close>Fermer</button></div>`);
    m.root.querySelector("[data-close]").addEventListener("click", m.close);
    m.root.querySelector("#declare").addEventListener("click", async () => {
      const { ok, error } = await api("PUT", `/api/v1/stock-items/${p.id}`, { body: { onHand: Number(m.root.querySelector("#onhand").value) } });
      if (!ok) return toast(problemMessage(error), "ko");
      toast("Stock déclaré");
      m.close();
      draw();
    });
    m.root.querySelector("#adjust")?.addEventListener("click", async () => {
      const delta = Number(m.root.querySelector("#delta").value);
      const reason = m.root.querySelector("#reason").value.trim();
      if (!delta || !reason) return toast("Indiquez une quantité et un motif.", "ko");
      const { ok, error } = await api("POST", `/api/v1/stock-items/${p.id}/adjustments`, { body: { delta, reason } });
      if (!ok) return toast(problemMessage(error), "ko");
      toast("Stock ajusté");
      m.close();
      draw();
    });
  };

  await draw();
}
