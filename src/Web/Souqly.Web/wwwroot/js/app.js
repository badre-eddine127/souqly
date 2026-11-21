import { session } from "./session.js";
import { navigate } from "./nav.js";
import { esc } from "./format.js";
import { empty, loading } from "./ui.js";
import { catalogPage } from "./pages/catalog.js";
import { productPage } from "./pages/product.js";
import { cartPage } from "./pages/cart.js";
import { ordersPage, orderPage } from "./pages/orders.js";
import { notificationsPage } from "./pages/notifications.js";
import { vendorProductsPage } from "./pages/vendor-products.js";
import { vendorOrdersPage } from "./pages/vendor-orders.js";

// Routes du site (navigation sans rechargement grâce à l'API History).
const routes = [
  { path: /^\/$/, page: catalogPage },
  { path: /^\/produits\/(\d+)$/, page: productPage },
  { path: /^\/panier$/, page: cartPage, role: "CUSTOMER" },
  { path: /^\/commandes$/, page: ordersPage, role: "CUSTOMER" },
  { path: /^\/commandes\/([0-9a-f-]{36})$/, page: orderPage, role: "CUSTOMER" },
  { path: /^\/notifications$/, page: notificationsPage, role: "ANY" },
  { path: /^\/vendeur\/produits$/, page: vendorProductsPage, role: "VENDOR" },
  { path: /^\/vendeur\/commandes$/, page: vendorOrdersPage, role: "VENDOR" },
];

const app = document.getElementById("app");
let cleanup = null;


async function render() {
  if (cleanup) cleanup();
  cleanup = null;
  window.scrollTo(0, 0);
  renderNav();

  const match = routes.map((r) => ({ r, m: location.pathname.match(r.path) })).find((x) => x.m);
  if (!match) {
    app.innerHTML = empty("Page introuvable", `<a href="/" data-link>Retour au catalogue</a>`);
    return;
  }

  const { r, m } = match;
  if (r.role && !session.user.authenticated) {
    app.innerHTML = empty("Connexion requise", `<p>Connectez-vous pour accéder à cette page.</p><button class="btn" data-action="login">Se connecter</button>`);
    return;
  }
  if (r.role && r.role !== "ANY" && !session.is(r.role)) {
    const who = r.role === "VENDOR" ? "vendeur" : "client";
    app.innerHTML = empty("Accès réservé", `<p>Cette page est réservée aux comptes ${who}.</p>`);
    return;
  }

  app.innerHTML = loading();
  const params = new URLSearchParams(location.search);
  cleanup = (await r.page(app, m.slice(1), params)) ?? null;
}

// Icônes au trait (style uniforme, 20 px), plutôt que des emojis.
const ICONS = {
  bell: '<path d="M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9"/><path d="M10.3 21a1.94 1.94 0 0 0 3.4 0"/>',
  bag: '<path d="M6 2 3 6v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V6l-3-4z"/><path d="M3 6h18"/><path d="M16 10a4 4 0 0 1-8 0"/>',
  chevron: '<path d="m6 9 6 6 6-6"/>',
  box: '<path d="M21 8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z"/><path d="m3.3 7 8.7 5 8.7-5"/><path d="M12 22V12"/>',
  store: '<path d="m2 7 4.41-4.41A2 2 0 0 1 7.83 2h8.34a2 2 0 0 1 1.42.59L22 7"/><path d="M4 12v8a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-8"/><path d="M2 7h20v3a2 2 0 0 1-2 2 2.7 2.7 0 0 1-2-1 2.7 2.7 0 0 1-2 1 2.7 2.7 0 0 1-2-1 2.7 2.7 0 0 1-2 1 2.7 2.7 0 0 1-2-1 2.7 2.7 0 0 1-2 1 2 2 0 0 1-2-2z"/>',
  truck: '<path d="M14 18V6a2 2 0 0 0-2-2H4a2 2 0 0 0-2 2v11a1 1 0 0 0 1 1h2"/><path d="M15 18H9"/><path d="M19 18h2a1 1 0 0 0 1-1v-3.65a1 1 0 0 0-.22-.62l-3.48-4.35A1 1 0 0 0 17.52 8H14"/><circle cx="17" cy="18" r="2"/><circle cx="7" cy="18" r="2"/>',
  logout: '<path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"/><path d="m16 17 5-5-5-5"/><path d="M21 12H9"/>',
  user: '<path d="M19 21v-2a4 4 0 0 0-4-4H9a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/>',
};
const icon = (name) => `<svg class="ico" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${ICONS[name]}</svg>`;

function renderNav() {
  const link = (href, label) => {
    const active = href === "/" ? location.pathname === "/" || location.pathname.startsWith("/produits") : location.pathname.startsWith(href);
    return `<a href="${href}" data-link class="${active ? "active" : ""}">${label}</a>`;
  };
  const count = (n) => (n > 0 ? `<span class="count">${n > 9 ? "9+" : n}</span>` : "");

  let nav = link("/", "Catalogue");
  if (session.is("CUSTOMER")) nav += link("/commandes", "Mes commandes");
  if (session.is("VENDOR")) nav += link("/vendeur/produits", "Mes produits") + link("/vendeur/commandes", "Commandes à préparer");
  document.getElementById("nav").innerHTML = nav;

  const u = session.user;
  const roleLabels = { CUSTOMER: "Client", VENDOR: "Vendeur", ADMIN: "Administrateur" };
  const name = u.name ?? u.username ?? "";
  const roles = u.roles.map((r) => roleLabels[r] ?? r).join(" · ");
  const menuItem = (href, ico, label) => `<a href="${href}" data-link role="menuitem">${icon(ico)}${label}</a>`;

  document.getElementById("account").innerHTML = u.authenticated
    ? `<a href="/notifications" data-link class="iconbtn" aria-label="Notifications${session.unread ? ` (${session.unread} non lues)` : ""}" title="Notifications">${icon("bell")}${count(session.unread)}</a>
       ${session.is("CUSTOMER")
         ? `<a href="/panier" data-link class="cartbtn" aria-label="Panier">${icon("bag")}<span>Panier</span>${session.cartUnits > 0 ? `<span class="cart-count">${session.cartUnits}</span>` : ""}</a>`
         : ""}
       <div class="usermenu">
         <button class="userbtn" aria-haspopup="menu" aria-expanded="false" data-usermenu>
           <span class="avatar">${esc(name.charAt(0).toUpperCase())}</span>
           <span class="who"><strong>${esc(name)}</strong><small>${esc(roles)}</small></span>
           ${icon("chevron")}
         </button>
         <div class="dropdown" role="menu" hidden>
           <div class="dropdown-head"><strong>${esc(name)}</strong><span>${esc(u.username ?? "")} · ${esc(roles)}</span></div>
           ${session.is("CUSTOMER") ? menuItem("/commandes", "box", "Mes commandes") + menuItem("/panier", "bag", "Mon panier") : ""}
           ${session.is("VENDOR") ? menuItem("/vendeur/produits", "store", "Mes produits") + menuItem("/vendeur/commandes", "truck", "Commandes à préparer") : ""}
           ${menuItem("/notifications", "bell", "Notifications")}
           <button class="logout" data-action="logout" role="menuitem">${icon("logout")}Se déconnecter</button>
         </div>
       </div>`
    : `<button class="btn btn-ghost signin" data-action="login">${icon("user")}Se connecter</button>`;
}

// Menu du compte : ouverture au clic, fermeture au clic ailleurs ou avec Échap.
function closeUserMenu() {
  const button = document.querySelector("[data-usermenu]");
  button?.setAttribute("aria-expanded", "false");
  document.querySelector(".dropdown")?.setAttribute("hidden", "");
}
document.addEventListener("click", (e) => {
  // Cloche : le compteur passe à 0 dès le clic (la page des notifications les marque lues sur le serveur).
  if (e.target.closest(".iconbtn[href='/notifications']") && session.unread > 0) {
    session.unread = 0;
    session.notify();
  }
  const toggle = e.target.closest("[data-usermenu]");
  if (toggle) {
    const open = toggle.getAttribute("aria-expanded") === "true";
    toggle.setAttribute("aria-expanded", String(!open));
    toggle.nextElementSibling.toggleAttribute("hidden", open);
    return;
  }
  if (!e.target.closest(".dropdown")) closeUserMenu();
});
document.addEventListener("keydown", (e) => {
  if (e.key === "Escape") closeUserMenu();
});

// Liens internes et boutons de session, gérés une seule fois pour toute la page.
document.addEventListener("click", (e) => {
  const link = e.target.closest("a[data-link]");
  if (link && !e.ctrlKey && !e.metaKey && link.origin === location.origin) {
    e.preventDefault();
    navigate(link.pathname + link.search);
    return;
  }
  const action = e.target.closest("[data-action]")?.dataset.action;
  if (action === "login") session.login();
  if (action === "logout") session.logout();
});

window.addEventListener("popstate", render);
// Ombre sous l'en-tête dès qu'on fait défiler la page.
const header = document.querySelector(".header");
window.addEventListener("scroll", () => header.classList.toggle("scrolled", window.scrollY > 8), { passive: true });
session.onChange(renderNav);
setInterval(() => session.refreshNotifications(), 15000);

await session.load();
render();
