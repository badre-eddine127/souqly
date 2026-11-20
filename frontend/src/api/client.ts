import createClient, { type Middleware } from "openapi-fetch";
import { accessToken } from "../auth";
import type { paths as CatalogPaths } from "./generated/catalog";
import type { paths as InventoryPaths } from "./generated/inventory";
import type { paths as CartPaths } from "./generated/cart";
import type { paths as OrderingPaths } from "./generated/ordering";
import type { paths as PaymentPaths } from "./generated/payment";
import type { paths as NotificationPaths } from "./generated/notification";
import type { components as Catalog } from "./generated/catalog";
import type { components as Ordering } from "./generated/ordering";
import type { components as Inventory } from "./generated/inventory";
import type { components as Notification } from "./generated/notification";
import type { components as Payment } from "./generated/payment";

// Clients typés générés depuis les contrats OpenAPI (npm run api) : aucun DTO écrit à la main.
// Tout passe par la passerelle (/api), qui applique quotas et routage.

const bearer: Middleware = {
  onRequest({ request }) {
    const token = accessToken();
    if (token) request.headers.set("Authorization", `Bearer ${token}`);
    return request;
  },
};

function client<P extends {}>() {
  const c = createClient<P>({ baseUrl: "" });
  c.use(bearer);
  return c;
}

export const catalogApi = client<CatalogPaths>();
export const inventoryApi = client<InventoryPaths>();
export const cartApi = client<CartPaths>();
export const orderingApi = client<OrderingPaths>();
export const paymentApi = client<PaymentPaths>();
export const notificationApi = client<NotificationPaths>();

export type Product = Catalog["schemas"]["ProductResponse"];
export type Category = Catalog["schemas"]["CategoryResponse"];
export type Order = Ordering["schemas"]["OrderResponse"];
export type VendorOrder = Ordering["schemas"]["VendorOrderResponse"];
export type StockItem = Inventory["schemas"]["StockItemResponse"];
export type Availability = Inventory["schemas"]["AvailabilityResponse"];
export type AppNotification = Notification["schemas"]["NotificationResponse"];
export type PaymentInfo = Payment["schemas"]["PaymentResponse"];

/** Message lisible d'une erreur de l'API (ProblemDetail RFC 9457). */
export function problemMessage(error: unknown, fallback = "Une erreur est survenue."): string {
  if (!error || typeof error !== "object") return fallback;
  const p = error as { detail?: string | null; title?: string | null; errors?: Record<string, string[]> };
  if (p.errors) return Object.values(p.errors).flat().join(" ");
  return p.detail ?? p.title ?? fallback;
}
