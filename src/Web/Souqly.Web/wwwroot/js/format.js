// Formats d'affichage : 1 200,00 MAD, dates en français, libellés des statuts.

const money = new Intl.NumberFormat("fr-FR", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const dateTime = new Intl.DateTimeFormat("fr-FR", { dateStyle: "medium", timeStyle: "short" });

export const formatMoney = (amount, currency = "MAD") => `${money.format(Number(amount))} ${currency}`;
export const formatDate = (iso) => dateTime.format(new Date(iso));
export const shortId = (id) => String(id).slice(0, 8).toUpperCase();

/** Échappe le texte avant de l'insérer dans du HTML (protection XSS). */
export const esc = (value) =>
  String(value ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);

export const STATUS = {
  PENDING: ["En attente", "wait"],
  STOCK_RESERVED: ["Stock réservé, paiement en cours", "wait"],
  CONFIRMED: ["Payée — en préparation", "ok"],
  SHIPPED: ["Expédiée", "info"],
  DELIVERED: ["Livrée", "ok"],
  REJECTED: ["Refusée", "ko"],
  CANCELLED: ["Annulée", "ko"],
  IN_TRANSIT: ["En transit", "info"],
};

export const REASONS = {
  OUT_OF_STOCK: "un article n'est plus disponible",
  PAYMENT_DECLINED: "le paiement a été refusé",
  CUSTOMER_REQUEST: "annulée à votre demande",
};

export const CARRIERS = [
  { code: "AMANA", label: "Amana" },
  { code: "CTM", label: "CTM Messagerie" },
  { code: "ARAMEX", label: "Aramex" },
  { code: "DHL", label: "DHL" },
];

export const carrierLabel = (code) => CARRIERS.find((c) => c.code === code)?.label ?? code;

/** Statut final : on arrête d'interroger le serveur toutes les secondes. */
export const isFinal = (status) => ["CONFIRMED", "SHIPPED", "DELIVERED", "REJECTED", "CANCELLED"].includes(status);
