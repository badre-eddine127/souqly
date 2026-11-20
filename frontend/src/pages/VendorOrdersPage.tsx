import { useCallback, useEffect, useState } from "react";
import { orderingApi, problemMessage, type VendorOrder } from "../api/client";
import { CARRIERS, formatDate, formatMoney, shortId } from "../format";
import { StatusBadge } from "../components/ui";
import { useSession } from "../session";

const TABS = [
  { status: "CONFIRMED", label: "À préparer" },
  { status: "SHIPPED", label: "Expédiées" },
  { status: "DELIVERED", label: "Livrées" },
  { status: "", label: "Toutes" },
];

/** US-13 : commandes payées à préparer, expédition avec numéro de suivi ; livraison simulée (US-14). */
export function VendorOrdersPage() {
  const s = useSession();
  const [tab, setTab] = useState("CONFIRMED");
  const [orders, setOrders] = useState<VendorOrder[] | null>(null);
  const [shipping, setShipping] = useState<VendorOrder | null>(null);

  const load = useCallback(async () => {
    setOrders(null);
    const { data } = await orderingApi.GET("/api/v1/vendor/orders", { params: { query: { status: tab || undefined, size: 50 } } });
    setOrders(data?.content ?? []);
  }, [tab]);

  useEffect(() => {
    load();
  }, [load]);

  const deliver = async (o: VendorOrder) => {
    const { error } = await orderingApi.POST("/api/v1/vendor/orders/{id}/delivery", { params: { path: { id: o.orderId } } });
    if (error) return s.toast(problemMessage(error), "ko");
    s.toast("Livraison confirmée — le client est prévenu");
    load();
  };

  return (
    <div>
      <h1>Commandes à préparer</h1>
      <div className="tabs">
        {TABS.map((t) => <button key={t.label} className={`chip ${tab === t.status ? "active" : ""}`} onClick={() => setTab(t.status)}>{t.label}</button>)}
      </div>
      {!orders && <div className="loading">Chargement…</div>}
      {orders?.length === 0 && <div className="empty">Rien ici pour le moment.</div>}
      <div className="stack">
        {orders?.map((o) => (
          <div key={o.orderId} className="card vendor-order">
            <div className="order-head">
              <div>
                <strong>Commande {shortId(o.orderId)}</strong>
                <div className="muted small">{formatDate(o.createdAt)}</div>
              </div>
              <StatusBadge status={o.status} />
            </div>
            <ul className="plain">
              {o.lines.map((l) => <li key={String(l.productId)}>{l.quantity} × {l.productName} <span className="muted">({formatMoney(l.lineTotal.amount)})</span></li>)}
            </ul>
            <div className="order-head">
              <span>Ma part : <strong>{formatMoney(o.vendorTotal.amount)}</strong></span>
              {o.shipment ? (
                <span className="muted">
                  {CARRIERS.find((c) => c.code === o.shipment!.carrier)?.label} · <code>{o.shipment.trackingNumber}</code> ·{" "}
                  {o.shipment.status === "DELIVERED" ? "livré" : <button className="link" onClick={() => deliver(o)}>Confirmer la livraison (transporteur simulé)</button>}
                </span>
              ) : o.status === "CONFIRMED" ? (
                <button className="btn" onClick={() => setShipping(o)}>Marquer expédiée</button>
              ) : null}
            </div>
          </div>
        ))}
      </div>
      {shipping && <ShipDialog order={shipping} onClose={(done) => { setShipping(null); if (done) load(); }} />}
    </div>
  );
}

function ShipDialog({ order, onClose }: { order: VendorOrder; onClose: (done: boolean) => void }) {
  const s = useSession();
  const [carrier, setCarrier] = useState("AMANA");
  const [tracking, setTracking] = useState("");
  const submit = async () => {
    const { error } = await orderingApi.POST("/api/v1/vendor/orders/{id}/shipment", {
      params: { path: { id: order.orderId } },
      body: { carrier, trackingNumber: tracking.trim().toUpperCase() },
    });
    if (error) return s.toast(problemMessage(error), "ko");
    s.toast("Colis expédié — le client reçoit le numéro de suivi");
    onClose(true);
  };
  return (
    <div className="modal-backdrop" onClick={() => onClose(false)}>
      <form className="modal card" onClick={(e) => e.stopPropagation()} onSubmit={(e) => { e.preventDefault(); submit(); }}>
        <h2>Expédier la commande {shortId(order.orderId)}</h2>
        <label>Transporteur
          <select value={carrier} onChange={(e) => setCarrier(e.target.value)}>
            {CARRIERS.map((c) => <option key={c.code} value={c.code}>{c.label}</option>)}
          </select>
        </label>
        <label>Numéro de suivi<input required pattern="[A-Za-z0-9-]{6,30}" value={tracking} onChange={(e) => setTracking(e.target.value)} placeholder="AM123456789MA" /></label>
        <div className="modal-actions">
          <button type="button" className="btn btn-ghost" onClick={() => onClose(false)}>Annuler</button>
          <button className="btn">Confirmer l'expédition</button>
        </div>
      </form>
    </div>
  );
}
