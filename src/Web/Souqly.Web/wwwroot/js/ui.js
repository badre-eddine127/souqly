import { esc, STATUS } from "./format.js";
import { productImage } from "./images.js";

export function statusBadge(status) {
  const [label, tone] = STATUS[status] ?? [status, "info"];
  return `<span class="status status-${tone}">${esc(label)}</span>`;
}

/** Photo du produit (choisie d'après son nom ou sa catégorie) ; un dégradé sable si l'image ne charge pas. */
export function productVisual(name, categoryId, size = "md") {
  const width = size === "lg" ? 1200 : size === "sm" ? 160 : 600;
  return `<div class="visual visual-${size}"><img src="${productImage(name, categoryId, width)}" alt="${esc(name)}" loading="lazy" onerror="this.remove()" /></div>`;
}

export function pager(page, totalPages) {
  if (totalPages <= 1) return "";
  return `<div class="pager">
    <button class="btn btn-ghost" data-page="${page - 1}" ${page <= 0 ? "disabled" : ""}>← Précédente</button>
    <span>Page ${page + 1} / ${totalPages}</span>
    <button class="btn btn-ghost" data-page="${page + 1}" ${page >= totalPages - 1 ? "disabled" : ""}>Suivante →</button>
  </div>`;
}

export const loading = (text = "Chargement…") => `<div class="loading">${esc(text)}</div>`;

export const empty = (title, extra = "") => `<div class="empty"><h2>${esc(title)}</h2>${extra}</div>`;

export function toast(message, tone = "ok") {
  const el = document.createElement("div");
  el.className = `toast toast-${tone}`;
  el.textContent = message;
  document.getElementById("toasts").append(el);
  setTimeout(() => el.remove(), 4500);
}

/** Fenêtre modale ; renvoie l'élément (fermée par close()). */
export function modal(html) {
  const backdrop = document.createElement("div");
  backdrop.className = "modal-backdrop";
  backdrop.innerHTML = `<div class="modal card">${html}</div>`;
  backdrop.addEventListener("click", (e) => {
    if (e.target === backdrop) backdrop.remove();
  });
  document.body.append(backdrop);
  return { root: backdrop.firstElementChild, close: () => backdrop.remove() };
}
