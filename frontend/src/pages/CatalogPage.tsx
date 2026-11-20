import { useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { catalogApi, type Category, type Product } from "../api/client";
import { formatMoney } from "../format";
import { Pager, ProductVisual } from "../components/ui";

const SORTS = [
  { value: "", label: "Nouveautés" },
  { value: "price,asc", label: "Prix croissant" },
  { value: "price,desc", label: "Prix décroissant" },
  { value: "name,asc", label: "Nom (A → Z)" },
];

/** US-01 : parcourir par catégorie, avec recherche, tri et pagination (l'état est dans l'URL : partageable). */
export function CatalogPage() {
  const [params, setParams] = useSearchParams();
  const [categories, setCategories] = useState<Category[]>([]);
  const [products, setProducts] = useState<Product[] | null>(null);
  const [totals, setTotals] = useState({ total: 0, pages: 0 });
  const [error, setError] = useState<string>();
  const category = params.get("categorie") ?? "";
  const q = params.get("q") ?? "";
  const sort = params.get("tri") ?? "";
  const page = Number(params.get("page") ?? 0);

  useEffect(() => {
    catalogApi.GET("/api/v1/categories").then(({ data }) => setCategories(data ?? []));
  }, []);

  useEffect(() => {
    setProducts(null);
    catalogApi
      .GET("/api/v1/products", {
        params: { query: { Category: category ? Number(category) : undefined, Q: q || undefined, Sort: sort || undefined, Page: page, Size: 12 } },
      })
      .then(({ data, error }) => {
        if (error || !data) return setError("Le catalogue est indisponible pour le moment.");
        setError(undefined);
        setProducts(data.content);
        setTotals({ total: Number(data.totalElements), pages: Number(data.totalPages) });
      })
      .catch(() => setError("Le catalogue est indisponible pour le moment."));
  }, [category, q, sort, page]);

  const update = (changes: Record<string, string>) => {
    const next = new URLSearchParams(params);
    for (const [k, v] of Object.entries(changes)) v ? next.set(k, v) : next.delete(k);
    if (!("page" in changes)) next.delete("page");
    setParams(next);
  };

  const roots = categories.filter((c) => c.parentId == null);

  return (
    <div className="catalog">
      <section className="hero">
        <h1>L'artisanat marocain, directement des ateliers</h1>
        <p>Tapis, poterie, cuir, argan : des vendeurs de tout le Maroc, paiement sécurisé et suivi de livraison.</p>
        <form
          className="search"
          onSubmit={(e) => {
            e.preventDefault();
            update({ q: String(new FormData(e.currentTarget).get("q") ?? "") });
          }}
        >
          <input name="q" defaultValue={q} placeholder="Rechercher un tapis, une huile d'argan…" aria-label="Rechercher" />
          <button className="btn">Rechercher</button>
        </form>
      </section>

      <div className="catalog-layout">
        <aside className="filters">
          <h3>Catégories</h3>
          <button className={`chip ${category === "" ? "active" : ""}`} onClick={() => update({ categorie: "" })}>Tout</button>
          {roots.map((root) => (
            <div key={String(root.id)} className="cat-group">
              <button className={`chip ${category === String(root.id) ? "active" : ""}`} onClick={() => update({ categorie: String(root.id) })}>
                {root.name}
              </button>
              {categories
                .filter((c) => String(c.parentId) === String(root.id))
                .map((c) => (
                  <button key={String(c.id)} className={`chip chip-sub ${category === String(c.id) ? "active" : ""}`} onClick={() => update({ categorie: String(c.id) })}>
                    {c.name}
                  </button>
                ))}
            </div>
          ))}
        </aside>

        <section>
          <div className="toolbar">
            <span className="muted">{products ? `${totals.total} produit${totals.total > 1 ? "s" : ""}` : "…"}{q && <> pour « {q} » <button className="link" onClick={() => update({ q: "" })}>effacer</button></>}</span>
            <label>
              Trier par{" "}
              <select value={sort} onChange={(e) => update({ tri: e.target.value })}>
                {SORTS.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
              </select>
            </label>
          </div>

          {error && <div className="alert alert-ko">{error}</div>}
          {!products && !error && <div className="loading">Chargement du catalogue…</div>}
          {products?.length === 0 && <div className="empty">Aucun produit ne correspond à votre recherche.</div>}

          <div className="grid">
            {products?.map((p) => (
              <Link key={String(p.id)} to={`/produits/${p.id}`} className="card product-card">
                <ProductVisual name={p.name} categoryId={p.categoryId} />
                <div className="card-body">
                  <h3>{p.name}</h3>
                  <p className="muted small">{categories.find((c) => String(c.id) === String(p.categoryId))?.name}</p>
                  <p className="price">{formatMoney(p.price.amount, p.price.currency)}</p>
                </div>
              </Link>
            ))}
          </div>
          <Pager page={page} totalPages={totals.pages} onChange={(p) => update({ page: String(p) })} />
        </section>
      </div>
    </div>
  );
}
