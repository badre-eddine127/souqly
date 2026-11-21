import { api, get } from "./api.js";

// État de la session : utilisateur connecté (lu sur le serveur), compteurs du panier et des notifications.
export const session = {
  user: { authenticated: false, username: null, name: null, roles: [] },
  cartUnits: 0,
  vendors: {},
  unread: 0,
  listeners: new Set(),

  is(role) {
    return this.user.roles.includes(role);
  },

  async load() {
    const { data } = await get("/session/user");
    this.user = data ?? this.user;
    // Annuaire des ateliers : démonstration (serveur web) + boutiques validées par l'équipe Souqly (US-16).
    const [directory, shops] = await Promise.all([get("/session/vendors"), get("/api/v1/vendors/shops")]);
    this.vendors = { ...(directory.data ?? {}) };
    for (const shop of shops.data ?? []) this.vendors[shop.vendorId] = { name: shop.shopName, city: shop.city };
    await Promise.all([this.refreshCart(), this.refreshNotifications()]);
  },

  async refreshCart() {
    if (!this.is("CUSTOMER")) {
      this.cartUnits = 0;
    } else {
      const { data } = await get("/api/v1/carts/me");
      this.cartUnits = Number(data?.totalUnits ?? 0);
    }
    this.notify();
  },

  async refreshNotifications() {
    if (!this.user.authenticated) {
      this.unread = 0;
    } else {
      const { data } = await get("/api/v1/notifications?size=50");
      this.unread = data?.content?.filter((n) => !n.read).length ?? 0;
    }
    this.notify();
  },

  /** Ouverture de la cloche : toutes les notifications passent « lues » (compteur à 0 tout de suite). */
  async markAllRead() {
    this.unread = 0;
    this.notify();
    await api("POST", "/api/v1/notifications/read-all");
  },

  login() {
    window.location.href = `/session/login?returnUrl=${encodeURIComponent(location.pathname + location.search)}`;
  },

  logout() {
    // Déconnexion côté serveur puis chez Keycloak : un vrai formulaire POST (navigation).
    const form = document.createElement("form");
    form.method = "post";
    form.action = "/session/logout";
    document.body.append(form);
    form.submit();
  },

  /** Nom de l'atelier et ville d'un vendeur (annuaire du site), sinon un libellé neutre. */
  vendor(vendorId) {
    return this.vendors[vendorId] ?? { name: "Artisan Souqly", city: "" };
  },

  onChange(listener) {
    this.listeners.add(listener);
  },

  notify() {
    for (const l of this.listeners) l();
  },
};

