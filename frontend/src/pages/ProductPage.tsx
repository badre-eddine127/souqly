import { useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { cartApi, catalogApi, inventoryApi, problemMessage, type Availability, type Product } from "../api/client";
import { formatMoney } from "../format";
import { ProductVisual } from "../components/ui";
import { useSession } from "../session";

/** US-02 : fiche produit (prix, vendeur, disponibilité) et ajout au panier (US-05). */
export function ProductPage() {
  const { id } = useParams();
  const s = useSession();
  const navigate = useNavigate();
  const [product, setProduct] = useState<Product | null>();
  const [availability, setAvailability] = useState<Availability | null>(null);
  const [quantity, setQuantity] = useState(1);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    const productId = Number(id);
    catalogApi.GET("/api/v1/products/{id}", { params: { path: { id: productId } } }).then(({ data }) => setProduct(data ?? null));
    inventoryApi.GET("/api/v1/stock-items/{productId}", { params: { path: { productId } } }).then(({ data }) => setAvailability(data ?? null));
  }, [id, s.signedIn]);

  if (product === undefined) return <div className="loading">Chargement…</div>;
  if (product === null)
    return (
      <div className="empty">
        <h2>Produit introuvable</h2>
        <Link to="/">Retour au catalogue</Link>
      </div>
    );

  const available = Number(availability?.available ?? 0);
  const max = Math.min(10, available);

  const addToCart = async () => {
    if (!s.signedIn) return s.login();
    setBusy(true);
    const current = await cartApi.GET("/api/v1/carts/me");
    const already = Number(current.data?.items.find((i) => String(i.productId) === String(product.id))?.quantity ?? 0);
    const { error } = await cartApi.PUT("/api/v1/carts/me/items/{productId}", {
      params: { path: { productId: Number(product.id) } },
      body: { quantity: Math.min(10, already + quantity) },
    });
    setBusy(false);
    if (error) return s.toast(problemMessage(error), "ko");
    await s.refreshCart();
    s.toast(`${quantity} × ${product.name} ajouté au panier`);
  };

  return (
    <div className="product-page">
      <Link to="/" className="back">← Catalogue</Link>
      <div className="product-layout">
        <ProductVisual name={product.name} categoryId={product.categoryId} size="lg" />
        <div>
          {product.status !== "PUBLISHED" && <span className="status status-wait">{product.status === "DRAFT" ? "Brouillon" : "Archivé"}</span>}
          <h1>{product.name}</h1>
          <p className="price big">{formatMoney(product.price.amount, product.price.currency)}</p>
          <p className="muted">Vendu par <strong>{product.vendorId}</strong> · réf. {product.sku}</p>
          <p className="description">{product.description || "Pas de description."}</p>

          <div className={`stock ${available > 0 ? "in" : "out"}`}>
            {availability == null ? "Disponibilité inconnue" : available > 0 ? `En stock : ${available} disponible${available > 1 ? "s" : ""}` : "Rupture de stock"}
          </div>

          {s.is("VENDOR") && !s.is("CUSTOMER") ? (
            <p className="muted">Connecté en vendeur : les achats se font avec un compte client.</p>
          ) : (
            <div className="buy">
              <label>
                Quantité
                <select value={quantity} onChange={(e) => setQuantity(Number(e.target.value))} disabled={max === 0}>
                  {Array.from({ length: Math.max(1, max) }, (_, i) => i + 1).map((n) => <option key={n}>{n}</option>)}
                </select>
              </label>
              <button className="btn btn-lg" disabled={busy || available === 0 || product.status !== "PUBLISHED"} onClick={addToCart}>
                {s.signedIn ? "Ajouter au panier" : "Se connecter pour acheter"}
              </button>
              {s.cartUnits > 0 && <button className="btn btn-ghost" onClick={() => navigate("/panier")}>Voir le panier</button>}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
