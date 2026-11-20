import { Link, NavLink, Route, Routes } from "react-router-dom";
import { SessionProvider, useSession } from "./session";
import { CatalogPage } from "./pages/CatalogPage";
import { ProductPage } from "./pages/ProductPage";
import { CartPage } from "./pages/CartPage";
import { OrdersPage } from "./pages/OrdersPage";
import { OrderPage } from "./pages/OrderPage";
import { NotificationsPage } from "./pages/NotificationsPage";
import { VendorProductsPage } from "./pages/VendorProductsPage";
import { VendorOrdersPage } from "./pages/VendorOrdersPage";
import { RequireRole } from "./components/RequireRole";

export function App() {
  return (
    <SessionProvider>
      <Header />
      <main className="container">
        <Routes>
          <Route path="/" element={<CatalogPage />} />
          <Route path="/produits/:id" element={<ProductPage />} />
          <Route path="/panier" element={<RequireRole role="CUSTOMER"><CartPage /></RequireRole>} />
          <Route path="/commandes" element={<RequireRole role="CUSTOMER"><OrdersPage /></RequireRole>} />
          <Route path="/commandes/:id" element={<RequireRole role="CUSTOMER"><OrderPage /></RequireRole>} />
          <Route path="/notifications" element={<RequireRole><NotificationsPage /></RequireRole>} />
          <Route path="/vendeur/produits" element={<RequireRole role="VENDOR"><VendorProductsPage /></RequireRole>} />
          <Route path="/vendeur/commandes" element={<RequireRole role="VENDOR"><VendorOrdersPage /></RequireRole>} />
          <Route path="*" element={<div className="empty"><h2>Page introuvable</h2><Link to="/">Retour au catalogue</Link></div>} />
        </Routes>
      </main>
      <footer className="footer">
        Souqly · projet SUP351 · microservices .NET 10, Kafka, Keycloak · <a href="http://localhost:8080/scalar/catalog" target="_blank" rel="noreferrer">Documentation de l'API</a>
      </footer>
    </SessionProvider>
  );
}

function Header() {
  const s = useSession();
  return (
    <header className="header">
      <div className="container header-row">
        <Link to="/" className="brand">
          <span className="brand-mark">✦</span> Souqly
        </Link>
        <nav className="nav">
          <NavLink to="/" end>Catalogue</NavLink>
          {s.is("CUSTOMER") && (
            <>
              <NavLink to="/commandes">Mes commandes</NavLink>
              <NavLink to="/panier" className="cart-link">
                Panier {s.cartUnits > 0 && <span className="badge">{s.cartUnits}</span>}
              </NavLink>
            </>
          )}
          {s.is("VENDOR") && (
            <>
              <NavLink to="/vendeur/produits">Mes produits</NavLink>
              <NavLink to="/vendeur/commandes">Commandes à préparer</NavLink>
            </>
          )}
          {s.signedIn && (
            <NavLink to="/notifications" title="Notifications">
              🔔 {s.unread > 0 && <span className="badge">{s.unread}</span>}
            </NavLink>
          )}
        </nav>
        <div className="account">
          {!s.ready ? null : s.signedIn ? (
            <>
              <span className="hello">
                {s.name} <small>{s.roles.filter((r) => ["CUSTOMER", "VENDOR", "ADMIN"].includes(r)).map(roleLabel).join(" · ")}</small>
              </span>
              <button className="btn btn-ghost" onClick={s.logout}>Se déconnecter</button>
            </>
          ) : (
            <button className="btn" onClick={s.login}>Se connecter</button>
          )}
        </div>
      </div>
    </header>
  );
}

const roleLabel = (r: string) => ({ CUSTOMER: "client", VENDOR: "vendeur", ADMIN: "admin" })[r] ?? r;
