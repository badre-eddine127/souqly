import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { catalogApi, inventoryApi, problemMessage, type Category, type Product, type StockItem } from "../api/client";
import { formatMoney } from "../format";
import { ProductVisual } from "../components/ui";
import { useSession } from "../session";

type Row = Product & { stock?: StockItem | null };
type Draft = { id?: number; categoryId: number; sku: string; name: string; description: string; price: string; publish: boolean };

const EMPTY: Draft = { categoryId: 2, sku: "", name: "", description: "", price: "", publish: true };

/** US-03 (mes produits : créer, modifier, archiver) et US-10 (déclarer et ajuster mon stock). */
export function VendorProductsPage() {
  const s = useSession();
  const [rows, setRows] = useState<Row[] | null>(null);
  const [categories, setCategories] = useState<Category[]>([]);
  const [draft, setDraft] = useState<Draft | null>(null);
  const [stockFor, setStockFor] = useState<Row | null>(null);

  const load = useCallback(async () => {
    const { data } = await catalogApi.GET("/api/v1/vendor/products", { params: { query: { size: 100 } } });
    const products = data?.content ?? [];
    const stocks = await Promise.all(
      products.map((p) => inventoryApi.GET("/api/v1/stock-items/{productId}/details", { params: { path: { productId: Number(p.id) } } }).then((r) => r.data ?? null)),
    );
    setRows(products.map((p, i) => ({ ...p, stock: stocks[i] })));
  }, []);

  useEffect(() => {
    load();
    catalogApi.GET("/api/v1/categories").then(({ data }) => setCategories(data ?? []));
  }, [load]);

  const save = async (d: Draft) => {
    const body = {
      categoryId: d.categoryId,
      sku: d.sku,
      name: d.name,
      description: d.description,
      price: { amount: Number(d.price.replace(",", ".")).toFixed(2), currency: "MAD" },
      publish: d.publish,
    };
    const { error } = d.id
      ? await catalogApi.PUT("/api/v1/products/{id}", { params: { path: { id: d.id } }, body })
      : await catalogApi.POST("/api/v1/products", { body });
    if (error) return s.toast(problemMessage(error), "ko");
    s.toast(d.id ? "Produit mis à jour" : "Produit créé — pensez à déclarer son stock");
    setDraft(null);
    load();
  };

  const archive = async (p: Row) => {
    if (!window.confirm(`Archiver « ${p.name} » ? Il disparaîtra du catalogue.`)) return;
    const { error } = await catalogApi.DELETE("/api/v1/products/{id}", { params: { path: { id: Number(p.id) } } });
    if (error) return s.toast(problemMessage(error), "ko");
    s.toast("Produit archivé");
    load();
  };

  return (
    <div>
      <div className="order-head">
        <h1>Mes produits</h1>
        <button className="btn" onClick={() => setDraft({ ...EMPTY, sku: `SKU-${Date.now().toString(36).toUpperCase()}` })}>+ Nouveau produit</button>
      </div>

      {!rows && <div className="loading">Chargement…</div>}
      {rows?.length === 0 && <div className="empty">Vous n'avez pas encore de produit.</div>}

      {rows && rows.length > 0 && (
        <div className="card">
          <table className="table">
            <thead>
              <tr><th></th><th>Produit</th><th>Prix</th><th>Statut</th><th>Stock (dispo / réservé)</th><th></th></tr>
            </thead>
            <tbody>
              {rows.map((p) => (
                <tr key={String(p.id)}>
                  <td><ProductVisual name={p.name} categoryId={p.categoryId} size="sm" /></td>
                  <td><Link to={`/produits/${p.id}`}><strong>{p.name}</strong></Link><div className="muted small">{p.sku}</div></td>
                  <td>{formatMoney(p.price.amount)}</td>
                  <td><span className={`status status-${p.status === "PUBLISHED" ? "ok" : p.status === "DRAFT" ? "wait" : "ko"}`}>{{ PUBLISHED: "Publié", DRAFT: "Brouillon", ARCHIVED: "Archivé" }[p.status] ?? p.status}</span></td>
                  <td>{p.stock ? <>{String(p.stock.available)} / {String(p.stock.reserved)}{Number(p.stock.available) <= 2 && <span className="warn"> ⚠ bas</span>}</> : <span className="muted">non déclaré</span>}</td>
                  <td className="actions">
                    {p.status !== "ARCHIVED" && (
                      <>
                        <button className="link" onClick={() => setStockFor(p)}>Stock</button>
                        <button className="link" onClick={() => setDraft({ id: Number(p.id), categoryId: Number(p.categoryId), sku: p.sku, name: p.name, description: p.description, price: p.price.amount, publish: p.status === "PUBLISHED" })}>Modifier</button>
                        <button className="link danger" onClick={() => archive(p)}>Archiver</button>
                      </>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {draft && <ProductForm draft={draft} categories={categories} onCancel={() => setDraft(null)} onSave={save} />}
      {stockFor && <StockDialog product={stockFor} onClose={() => { setStockFor(null); load(); }} />}
    </div>
  );
}

function ProductForm({ draft, categories, onCancel, onSave }: { draft: Draft; categories: Category[]; onCancel: () => void; onSave: (d: Draft) => void }) {
  const [d, setD] = useState(draft);
  return (
    <div className="modal-backdrop" onClick={onCancel}>
      <form className="modal card" onClick={(e) => e.stopPropagation()} onSubmit={(e) => { e.preventDefault(); onSave(d); }}>
        <h2>{d.id ? "Modifier le produit" : "Nouveau produit"}</h2>
        <label>Nom<input required value={d.name} onChange={(e) => setD({ ...d, name: e.target.value })} /></label>
        <div className="two">
          <label>Catégorie
            <select value={d.categoryId} onChange={(e) => setD({ ...d, categoryId: Number(e.target.value) })}>
              {categories.map((c) => <option key={String(c.id)} value={String(c.id)}>{c.parentId ? "— " : ""}{c.name}</option>)}
            </select>
          </label>
          <label>Prix (MAD)<input required inputMode="decimal" value={d.price} onChange={(e) => setD({ ...d, price: e.target.value })} placeholder="1200,00" /></label>
        </div>
        <label>Référence (SKU)<input required value={d.sku} onChange={(e) => setD({ ...d, sku: e.target.value })} /></label>
        <label>Description<textarea rows={4} value={d.description} onChange={(e) => setD({ ...d, description: e.target.value })} /></label>
        <label className="check"><input type="checkbox" checked={d.publish} onChange={(e) => setD({ ...d, publish: e.target.checked })} /> Publié dans le catalogue</label>
        <div className="modal-actions">
          <button type="button" className="btn btn-ghost" onClick={onCancel}>Annuler</button>
          <button className="btn">Enregistrer</button>
        </div>
      </form>
    </div>
  );
}

function StockDialog({ product, onClose }: { product: Row; onClose: () => void }) {
  const s = useSession();
  const [onHand, setOnHand] = useState(String(product.stock?.onHand ?? 0));
  const [delta, setDelta] = useState("");
  const [reason, setReason] = useState("");
  const productId = Number(product.id);

  const declare = async () => {
    const { error } = await inventoryApi.PUT("/api/v1/stock-items/{productId}", { params: { path: { productId } }, body: { onHand: Number(onHand) } });
    if (error) return s.toast(problemMessage(error), "ko");
    s.toast("Stock déclaré");
    onClose();
  };

  const adjust = async () => {
    const { error } = await inventoryApi.POST("/api/v1/stock-items/{productId}/adjustments", { params: { path: { productId } }, body: { delta: Number(delta), reason } });
    if (error) return s.toast(problemMessage(error), "ko");
    s.toast("Stock ajusté");
    onClose();
  };

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal card" onClick={(e) => e.stopPropagation()}>
        <h2>Stock — {product.name}</h2>
        {product.stock && (
          <p className="muted">En main : {String(product.stock.onHand)} · réservé : {String(product.stock.reserved)} · disponible : {String(product.stock.available)}</p>
        )}
        <h3>Déclarer le stock physique</h3>
        <div className="inline">
          <input type="number" min={0} value={onHand} onChange={(e) => setOnHand(e.target.value)} />
          <button className="btn" onClick={declare}>Enregistrer</button>
        </div>
        {product.stock && (
          <>
            <h3>Ajuster (entrée, casse, inventaire…)</h3>
            <div className="inline">
              <input type="number" placeholder="-3 ou +10" value={delta} onChange={(e) => setDelta(e.target.value)} />
              <input placeholder="Motif" value={reason} onChange={(e) => setReason(e.target.value)} />
              <button className="btn" disabled={!delta || !reason} onClick={adjust}>Ajuster</button>
            </div>
          </>
        )}
        <div className="modal-actions"><button className="btn btn-ghost" onClick={onClose}>Fermer</button></div>
      </div>
    </div>
  );
}
