// Formats d'affichage marocains : 1 200,00 MAD, dates en français.

const money = new Intl.NumberFormat("fr-MA", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const dateTime = new Intl.DateTimeFormat("fr-FR", { dateStyle: "medium", timeStyle: "short" });

export const formatMoney = (amount: string | number, currency = "MAD") => `${money.format(Number(amount))} ${currency}`;

export const formatDate = (iso: string) => dateTime.format(new Date(iso));

export const shortId = (id: string) => id.slice(0, 8).toUpperCase();

export const STATUS_LABELS: Record<string, { label: string; tone: "wait" | "ok" | "ko" | "info" }> = {
  PENDING: { label: "En attente", tone: "wait" },
  STOCK_RESERVED: { label: "Stock réservé, paiement en cours", tone: "wait" },
  CONFIRMED: { label: "Payée — en préparation", tone: "ok" },
  SHIPPED: { label: "Expédiée", tone: "info" },
  DELIVERED: { label: "Livrée", tone: "ok" },
  REJECTED: { label: "Refusée", tone: "ko" },
  CANCELLED: { label: "Annulée", tone: "ko" },
  IN_TRANSIT: { label: "En transit", tone: "info" },
};

export const REASON_LABELS: Record<string, string> = {
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

/** Statut final : on arrête d'interroger le serveur. */
export const isFinal = (status: string) => ["CONFIRMED", "SHIPPED", "DELIVERED", "REJECTED", "CANCELLED"].includes(status);
