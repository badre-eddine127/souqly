import { useCallback, useEffect, useRef, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { cartApi, catalogApi, orderingApi, problemMessage, type Product } from "../api/client";
import { formatMoney } from "../format";
import { ProductVisual } from "../components/ui";
import { useSession } from "../session";

type Line = { productId: number; quantity: number; product?: Product };

/** US-05 (panier) et US-06 / US-09 (validation idempotente : un double clic ne crée qu'une commande). */
export function CartPage() {
  const s = useSession();
  const navigate = useNavigate();
  const [lines, setLines] = useState<Line[] | null>(null);
  const [placing, setPlacing] = useState(false);
  // Une clé d'idempotence par tentative de commande : réutilisée si l'utilisateur clique deux fois ou réessaie.
  const idempotencyKey = useRef(crypto.randomUUID());

  const load = useCallback(async () => {
    const { data } = await cartApi.GET("/api/v1/carts/me");
    const items = (data?.items ?? []).map((i) => ({ productId: Number(i.productId), quantity: Number(i.quantity) }));
    const products = await Promise.all(
      items.map((i) => catalogApi.GET("/api/v1/products/{id}", { params: { path: { id: i.productId } } }).then((r) => r.data)),
    );
    setLines(items.map((i, k) => ({ ...i, product: products[k] })));
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  const setQuantity = async (productId: number, quantity: number) => {
    const { error } = await cartApi.PUT("/api/v1/carts/me/items/{productId}", { params: { path: { productId } }, body: { quantity } });
    if (error) return s.toast(problemMessage(error), "ko");
    idempotencyKey.current = crypto.randomUUID();   // le panier a changé : nouvelle commande
    await Promise.all([load(), s.refreshCart()]);
  };

  const remove = async (productId: number) => {
    await cartApi.DELETE("/api/v1/carts/me/items/{productId}", { params: { path: { productId } } });
    idempotencyKey.current = crypto.randomUUID();
    await Promise.all([load(), s.refreshCart()]);
  };

  const placeOrder = async () => {
    if (!lines?.length) return;
    setPlacing(true);
    const { data, error } = await orderingApi.POST("/api/v1/orders", {
      params: { header: { "Idempotency-Key": idempotencyKey.current } },
      body: { lines: lines.map((l) => ({ productId: l.productId, quantity: l.quantity })) },
    });
    if (error || !data) {
      setPlacing(false);
      return s.toast(problemMessage(error, "La commande n'a pas pu être passée."), "ko");
    }
    await cartApi.DELETE("/api/v1/carts/me");
    await s.refreshCart();
    s.toast("Commande envoyée : réservation du stock et paiement en cours…");
    navigate(`/commandes/${data.id}`);
  };

  if (!lines) return <div className="loading">Chargement du panier…</div>;
  if (lines.length === 0)
    return (
      <div className="empty">
        <h2>Votre panier est vide</h2>
        <Link to="/" className="btn">Découvrir le catalogue</Link>
      </div>
    );

  const total = lines.reduce((sum, l) => sum + Number(l.product?.price.amount ?? 0) * l.quantity, 0);
  const unavailable = lines.some((l) => !l.product || l.product.status !== "PUBLISHED");

  return (
    <div>
      <h1>Mon panier</h1>
      <div className="cart-layout">
        <div className="card list">
          {lines.map((l) => (
            <div key={l.productId} className="cart-line">
              <ProductVisual name={l.product?.name ?? "?"} categoryId={l.product?.categoryId ?? 0} size="sm" />
              <div className="grow">
                <Link to={`/produits/${l.productId}`}><strong>{l.product?.name ?? `Produit ${l.productId}`}</strong></Link>
                <div className="muted small">{l.product ? formatMoney(l.product.price.amount) : "Produit indisponible"}</div>
              </div>
              <select value={l.quantity} onChange={(e) => setQuantity(l.productId, Number(e.target.value))} aria-label="Quantité">
                {Array.from({ length: 10 }, (_, i) => i + 1).map((n) => <option key={n}>{n}</option>)}
              </select>
              <div className="line-total">{l.product && formatMoney(Number(l.product.price.amount) * l.quantity)}</div>
              <button className="link danger" onClick={() => remove(l.productId)}>Retirer</button>
            </div>
          ))}
        </div>
        <aside className="card summary">
          <h3>Récapitulatif</h3>
          <div className="row"><span>Articles</span><span>{lines.reduce((n, l) => n + l.quantity, 0)}</span></div>
          <div className="row total"><span>Total</span><span>{formatMoney(total)}</span></div>
          <p className="muted small">Le prix est figé au moment de la commande. Paiement de test : refusé au-delà de 10 000,00 MAD.</p>
          <button className="btn btn-lg full" disabled={placing || unavailable} onClick={placeOrder}>
            {placing ? "Commande en cours…" : "Valider et payer"}
          </button>
          {unavailable && <p className="alert alert-ko small">Un article n'est plus disponible : retirez-le pour continuer.</p>}
        </aside>
      </div>
    </div>
  );
}
