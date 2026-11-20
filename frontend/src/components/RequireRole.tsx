import type { ReactNode } from "react";
import { useSession } from "../session";
import type { Role } from "../auth";

/** Affiche la page seulement aux bons rôles (confort d'affichage : les services vérifient de toute façon). */
export function RequireRole({ role, children }: { role?: Role; children: ReactNode }) {
  const s = useSession();
  if (!s.ready) return <div className="loading">Chargement…</div>;
  if (!s.signedIn)
    return (
      <div className="empty">
        <h2>Connexion requise</h2>
        <p>Connectez-vous pour accéder à cette page.</p>
        <button className="btn" onClick={s.login}>Se connecter</button>
      </div>
    );
  if (role && !s.is(role))
    return (
      <div className="empty">
        <h2>Accès réservé</h2>
        <p>Cette page est réservée aux comptes {role === "VENDOR" ? "vendeur" : role === "CUSTOMER" ? "client" : "administrateur"}.</p>
      </div>
    );
  return <>{children}</>;
}
