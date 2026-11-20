import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { orderingApi, paymentApi, problemMessage, type Order, type PaymentInfo } from "../api/client";
import { CARRIERS, formatDate, formatMoney, isFinal, REASON_LABELS, shortId } from "../format";
import { StatusBadge } from "../components/ui";
import { useSession } from "../session";

const STEPS = ["PENDING", "STOCK_RESERVED", "CONFIRMED", "SHIPPED", "DELIVERED"];

/** US-07 (suivi rafraîchi par interrogation), US-08 (annulation), US-14 (suivi de livraison). */
export function OrderPage() {
  const { id } = useParams();
  const s = useSession();
  const [order, setOrder] = useState<Order | null>();
  const [payment, setPayment] = useState<PaymentInfo | null>(null);
  const [cancelling, setCancelling] = useState(false);

  useEffect(() => {
    let stop = false;
    let timer = 0;
    const poll = async () => {
      const { data } = await orderingApi.GET("/api/v1/orders/{id}", { params: { path: { id: id! } } });
      if (stop) return;
      setOrder(data ?? null);
      const pay = await paymentApi.GET("/api/v1/payments/{orderId}", { params: { path: { orderId: id! } } });
      if (!stop) setPayment(pay.data ?? null);
      // La saga est asynchrone : on interroge toutes les secondes tant que la commande évolue, puis plus lentement.
      timer = window.setTimeout(poll, data && isFinal(data.status) ? 10000 : 1000);
    };
    poll();
    return () => {
      stop = true;
      window.clearTimeout(timer);
    };
  }, [id]);

  if (order === undefined) return <div className="loading">Chargement…</div>;
  if (order === null) return <div className="empty"><h2>Commande introuvable</h2><Link to="/commandes">Mes commandes</Link></div>;

  const cancel = async () => {
    if (!window.confirm("Annuler cette commande ? Le paiement sera remboursé.")) return;
    setCancelling(true);
    const { data, error } = await orderingApi.POST("/api/v1/orders/{id}/cancellation", { params: { path: { id: order.id } } });
    setCancelling(false);
    if (error) return s.toast(problemMessage(error), "ko");
    setOrder(data!);
    s.toast("Commande annulée : remboursement en cours.");
  };

  const failed = order.status === "REJECTED" || order.status === "CANCELLED";
  const current = STEPS.indexOf(order.status);
  const canCancel = ["PENDING", "STOCK_RESERVED", "CONFIRMED"].includes(order.status) && order.shipments.length === 0;

  return (
    <div>
      <Link to="/commandes" className="back">← Mes commandes</Link>
      <div className="order-head">
        <h1>Commande {shortId(order.id)}</h1>
        <StatusBadge status={order.status} />
      </div>
      <p className="muted">Passée le {formatDate(order.createdAt)}</p>

      {failed ? (
        <div className="alert alert-ko">
          Commande {order.status === "CANCELLED" ? "annulée" : "refusée"}
          {order.statusReason && ` : ${REASON_LABELS[order.statusReason] ?? order.statusReason}`}.
        </div>
      ) : (
        <ol className="timeline">
          {STEPS.map((step, i) => (
            <li key={step} className={i < current ? "done" : i === current ? "current" : ""}>
              {["Reçue", "Stock réservé", "Payée", "Expédiée", "Livrée"][i]}
            </li>
          ))}
        </ol>
      )}

      <div className="cart-layout">
        <div className="card list">
          {order.lines.map((l) => (
            <div key={String(l.productId)} className="cart-line">
              <div className="grow">
                <Link to={`/produits/${l.productId}`}><strong>{l.productName}</strong></Link>
                <div className="muted small">{l.quantity} × {formatMoney(l.unitPrice.amount)} · vendu par {l.vendorId}</div>
              </div>
              <div className="line-total">{formatMoney(l.lineTotal.amount)}</div>
            </div>
          ))}
          <div className="cart-line total-line">
            <div className="grow"><strong>Total</strong></div>
            <div className="line-total"><strong>{formatMoney(order.total.amount, order.total.currency)}</strong></div>
          </div>
        </div>

        <aside className="card summary">
          <h3>Paiement</h3>
          {payment ? (
            <p>
              {{ CAPTURED: "Encaissé", DECLINED: "Refusé", REFUNDED: "Remboursé" }[payment.status] ?? payment.status} —{" "}
              {formatMoney(payment.amount, payment.currency)}
            </p>
          ) : (
            <p className="muted">{failed ? "Aucun paiement." : "En attente…"}</p>
          )}

          <h3>Livraison</h3>
          {order.shipments.length === 0 ? (
            <p className="muted">{order.status === "CONFIRMED" ? "En préparation chez les vendeurs." : "—"}</p>
          ) : (
            order.shipments.map((sh) => (
              <div key={sh.vendorId} className="parcel">
                <StatusBadge status={sh.status} />
                <div><strong>{CARRIERS.find((c) => c.code === sh.carrier)?.label ?? sh.carrier}</strong> · suivi <code>{sh.trackingNumber}</code></div>
                <div className="muted small">Expédié le {formatDate(sh.shippedAt)}{sh.deliveredAt && ` · livré le ${formatDate(sh.deliveredAt)}`}</div>
              </div>
            ))
          )}

          {canCancel && (
            <button className="btn btn-danger full" disabled={cancelling} onClick={cancel}>
              Annuler la commande
            </button>
          )}
        </aside>
      </div>
    </div>
  );
}
