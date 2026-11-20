import { STATUS_LABELS } from "../format";

export function StatusBadge({ status }: { status: string }) {
  const s = STATUS_LABELS[status] ?? { label: status, tone: "info" as const };
  return <span className={`status status-${s.tone}`}>{s.label}</span>;
}

/** Vignette décorative générée à partir du nom (le catalogue n'a pas encore de photos). */
export function ProductVisual({ name, categoryId, size = "md" }: { name: string; categoryId: number | string; size?: "sm" | "md" | "lg" }) {
  const icons: Record<number, string> = { 1: "🏺", 2: "🧶", 3: "🏺", 4: "👜", 5: "🌿", 6: "🫒", 7: "🍯", 8: "👘" };
  const hue = [...name].reduce((h, c) => (h * 31 + c.charCodeAt(0)) % 360, 7);
  return (
    <div className={`visual visual-${size}`} style={{ "--hue": hue } as React.CSSProperties} aria-hidden>
      <span>{icons[Number(categoryId)] ?? "✦"}</span>
    </div>
  );
}

export function Pager({ page, totalPages, onChange }: { page: number; totalPages: number; onChange: (page: number) => void }) {
  if (totalPages <= 1) return null;
  return (
    <div className="pager">
      <button className="btn btn-ghost" disabled={page <= 0} onClick={() => onChange(page - 1)}>← Précédente</button>
      <span>Page {page + 1} / {totalPages}</span>
      <button className="btn btn-ghost" disabled={page >= totalPages - 1} onClick={() => onChange(page + 1)}>Suivante →</button>
    </div>
  );
}
