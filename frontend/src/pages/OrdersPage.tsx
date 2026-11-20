import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { orderingApi, type Order } from "../api/client";
import { formatDate, formatMoney, shortId } from "../format";
import { Pager, StatusBadge } from "../components/ui";

/** US-07 : mes commandes. */
export function OrdersPage() {
  const [orders, setOrders] = useState<Order[] | null>(null);
  const [page, setPage] = useState(0);
  const [pages, setPages] = useState(0);

  useEffect(() => {
    orderingApi.GET("/api/v1/orders", { params: { query: { page, size: 10 } } }).then(({ data }) => {
      setOrders(data?.content ?? []);
      setPages(Number(data?.totalPages ?? 0));
    });
  }, [page]);

  if (!orders) return <div className="loading">Chargement…</div>;
  if (orders.length === 0)
    return (
      <div className="empty">
        <h2>Aucune commande pour l'instant</h2>
        <Link to="/" className="btn">Découvrir le catalogue</Link>
      </div>
    );

  return (
    <div>
      <h1>Mes commandes</h1>
      <div className="card list">
        {orders.map((o) => (
          <Link key={o.id} to={`/commandes/${o.id}`} className="order-row">
            <div>
              <strong>Commande {shortId(o.id)}</strong>
              <div className="muted small">{formatDate(o.createdAt)} · {o.lines.reduce((n, l) => n + Number(l.quantity), 0)} article(s)</div>
            </div>
            <StatusBadge status={o.status} />
            <div className="price">{formatMoney(o.total.amount, o.total.currency)}</div>
          </Link>
        ))}
      </div>
      <Pager page={page} totalPages={pages} onChange={setPage} />
    </div>
  );
}
