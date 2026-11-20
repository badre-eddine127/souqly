import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { notificationApi, type AppNotification } from "../api/client";
import { formatDate } from "../format";
import { useSession } from "../session";

/** US-15 : les messages envoyés à chaque étape (e-mails simulés, stockés par le service Notification). */
export function NotificationsPage() {
  const s = useSession();
  const [items, setItems] = useState<AppNotification[] | null>(null);

  const load = () => notificationApi.GET("/api/v1/notifications", { params: { query: { size: 50 } } }).then(({ data }) => setItems(data?.content ?? []));

  useEffect(() => {
    load();
  }, []);

  const markRead = async (n: AppNotification) => {
    if (n.read) return;
    await notificationApi.POST("/api/v1/notifications/{id}/read", { params: { path: { id: n.id } } });
    await Promise.all([load(), s.refreshNotifications()]);
  };

  if (!items) return <div className="loading">Chargement…</div>;
  return (
    <div>
      <h1>Notifications</h1>
      {items.length === 0 && <div className="empty">Aucune notification.</div>}
      <div className="card list">
        {items.map((n) => (
          <div key={n.id} className={`notification ${n.read ? "" : "unread"}`} onClick={() => markRead(n)}>
            <div className="grow">
              <strong>{n.title}</strong>
              <p>{n.body}</p>
              <span className="muted small">{formatDate(n.createdAt)}</span>
            </div>
            {s.is("CUSTOMER") && !n.kind.startsWith("NEW_ORDER") && <Link to={`/commandes/${n.orderId}`} onClick={(e) => e.stopPropagation()}>Voir la commande</Link>}
            {n.kind === "NEW_ORDER" && <Link to="/vendeur/commandes" onClick={(e) => e.stopPropagation()}>Préparer</Link>}
          </div>
        ))}
      </div>
    </div>
  );
}
