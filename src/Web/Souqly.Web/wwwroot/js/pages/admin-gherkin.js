import { get, query } from "../api.js";
import { esc, formatDate } from "../format.js";
import { pager } from "../ui.js";

const productNames = new Map();

/** Remplace {produit:12} par le nom du produit (lu une fois dans le catalogue public). */
async function resolveProducts(texts) {
  const ids = [...new Set(texts.flatMap((t) => [...t.matchAll(/\{produit:(\d+)\}/g)].map((m) => m[1])))].filter((id) => !productNames.has(id));
  await Promise.all(
    ids.map(async (id) => {
      const r = await get(`/api/v1/products/${id}`);
      productNames.set(id, r.ok ? `« ${r.data.name} »` : `le produit n° ${id}`);
    }),
  );
  return (text) => esc(text).replace(/\{produit:(\d+)\}/g, (_, id) => `<b>${esc(productNames.get(id) ?? `le produit n° ${id}`)}</b>`);
}

const time = (iso) => new Intl.DateTimeFormat("fr-FR", { hour: "2-digit", minute: "2-digit", second: "2-digit" }).format(new Date(iso));
const result = (ok) => (ok ? `<span class="gk-ok">✓ OK</span>` : `<span class="gk-ko">✗ KO</span>`);

/**
 * Page admin « Gherkin » :
 * - Parcours en direct : les actions réelles des utilisateurs, écrites en Gherkin, avec OK/KO (tenu par la passerelle) ;
 * - Recette : chaque scénario des tests d'acceptation, ses étapes et OK/KO (dernière exécution des tests).
 */
export async function adminGherkinPage(app, _match, params) {
  let tab = params.get("onglet") === "recette" ? "recette" : "parcours";
  const filters = { user: "", failedOnly: false, page: 0 };
  let timer = 0;

  const shell = (content) => `
    <div class="page-head">
      <div><span class="eyebrow">Administration</span><h1>Gherkin</h1>
        <p class="muted">Les parcours réels des utilisateurs et les scénarios de recette, écrits en Gherkin, avec leur résultat OK ou KO.</p></div>
      <a class="btn btn-ghost" href="/admin/journal" data-link>Journal d'audit</a>
    </div>
    <div class="tabs">
      <button class="chip ${tab === "parcours" ? "active" : ""}" data-tab="parcours">Parcours en direct</button>
      <button class="chip ${tab === "recette" ? "active" : ""}" data-tab="recette">Recette (tests automatiques)</button>
    </div>
    ${content}`;

  const bindTabs = () =>
    app.querySelectorAll("[data-tab]").forEach((b) =>
      b.addEventListener("click", () => {
        tab = b.dataset.tab;
        filters.page = 0;
        draw();
      }),
    );

  const drawJourneys = async () => {
    const r = await get(`/api/v1/admin/journeys${query({ user: filters.user, failedOnly: filters.failedOnly || "", page: filters.page, size: 10 })}`);
    const page = r.data ?? { content: [], page: 0, totalPages: 0, totalElements: 0, passedSteps: 0, failedSteps: 0 };
    const render = await resolveProducts(page.content.flatMap((s) => s.lines.map((l) => l.text)));
    app.innerHTML = shell(`
      <form class="card audit-filters">
        <label>Utilisateur<input name="user" value="${esc(filters.user)}" placeholder="nom de connexion" /></label>
        <label class="check"><input type="checkbox" name="failedOnly" ${filters.failedOnly ? "checked" : ""} /> Seulement les scénarios KO</label>
        <button class="btn">Filtrer</button>
        <span class="gk-summary grow">${page.totalElements} scénario${page.totalElements > 1 ? "s" : ""} · <span class="gk-ok">${page.passedSteps} action${page.passedSteps > 1 ? "s" : ""} OK</span> · <span class="gk-ko">${page.failedSteps} KO</span> · <span class="muted small">actualisé toutes les 10 s</span></span>
      </form>
      ${!r.ok ? `<div class="alert alert-ko">Parcours indisponibles (passerelle).</div>` : ""}
      ${r.ok && page.content.length === 0 ? `<div class="empty">Aucun parcours pour l'instant : utilisez le site avec un compte (recherche, panier, commande…) et revenez ici.</div>` : ""}
      <div class="stack">
        ${page.content
          .map((s) => `
            <article class="card gherkin ${s.ok ? "" : "failed"}">
              <header>
                <div><span class="gk-kw">Scénario :</span> <strong>Parcours de ${esc(s.username ?? s.userId.slice(0, 8))}</strong>
                  <div class="muted small">${formatDate(s.startedAt)} → ${time(s.endedAt)} · ${s.passed} OK · ${s.failed} KO</div></div>
                ${result(s.ok)}
              </header>
              <ol class="gk-lines">
                ${s.lines
                  .map((l) => `<li class="${l.keyword === "Alors" ? (l.ok ? "then-ok" : "then-ko") : ""}">
                    <span class="gk-kw">${esc(l.keyword)}</span> ${render(l.text)}
                    ${l.keyword === "Alors" ? `<span class="gk-at">${time(l.at)}</span>` : ""}
                  </li>`)
                  .join("")}
              </ol>
            </article>`)
          .join("")}
      </div>
      ${pager(page.page, page.totalPages)}`);

    bindTabs();
    app.querySelector(".audit-filters").addEventListener("submit", (e) => {
      e.preventDefault();
      const f = new FormData(e.target);
      filters.user = String(f.get("user")).trim();
      filters.failedOnly = f.get("failedOnly") === "on";
      filters.page = 0;
      draw();
    });
    app.querySelectorAll("[data-page]").forEach((b) => b.addEventListener("click", () => { filters.page = Number(b.dataset.page); draw(); }));
  };

  const drawReport = async () => {
    const r = await get("/session/gherkin-report");
    if (!r.ok) {
      app.innerHTML = shell(`<div class="empty"><h2>${esc(r.error?.title ?? "Rapport indisponible")}</h2><p>${esc(r.error?.detail ?? "")}</p></div>`);
      bindTabs();
      return;
    }

    const report = r.data;
    const shown = report.scenarios.filter((s) => !filters.failedOnly || !s.ok);
    const byStory = Object.entries(Object.groupBy(shown, (s) => `${s.story} · ${s.feature}`)).sort(([a], [b]) => a.localeCompare(b, "fr", { numeric: true }));
    app.innerHTML = shell(`
      <div class="card gk-report-head">
        <div class="kpi-mini"><strong>${report.total}</strong><span>scénarios</span></div>
        <div class="kpi-mini ok"><strong>${report.passed}</strong><span>OK</span></div>
        <div class="kpi-mini ko"><strong>${report.failed}</strong><span>KO</span></div>
        <div class="grow muted small">Exécution du ${formatDate(report.finishedAt)} (${esc(report.machine)}).<br>Relancez les tests d'acceptation pour mettre à jour ce rapport.</div>
        <label class="check"><input type="checkbox" id="ko" ${filters.failedOnly ? "checked" : ""} /> Seulement les KO</label>
      </div>
      ${byStory.length === 0 ? `<div class="empty">Aucun scénario KO. 🎉</div>` : ""}
      <div class="stack">
        ${byStory
          .map(([title, scenarios]) => `
            <section class="card gherkin">
              <header><div><span class="gk-kw">Fonctionnalité :</span> <strong>${esc(title)}</strong></div>
                ${result(scenarios.every((s) => s.ok))}</header>
              ${scenarios
                .map((s) => `
                  <details ${s.ok ? "" : "open"}>
                    <summary>${result(s.ok)} <span class="gk-kw">Scénario :</span> ${esc(s.scenario)} <span class="muted small">${s.durationMs} ms</span></summary>
                    <ol class="gk-lines">
                      ${s.steps.map((st) => `<li class="${st.ok ? "" : "then-ko"}"><span class="gk-kw">${esc(st.keyword)}</span> ${esc(st.text)} ${st.ok ? `<span class="gk-ok">✓</span>` : `<span class="gk-ko">✗</span>`}</li>`).join("")}
                    </ol>
                    ${s.error ? `<p class="gk-error">${esc(s.error)}</p>` : ""}
                  </details>`)
                .join("")}
            </section>`)
          .join("")}
      </div>`);
    bindTabs();
    app.querySelector("#ko").addEventListener("change", (e) => { filters.failedOnly = e.target.checked; draw(); });
  };

  const draw = async () => {
    clearInterval(timer);
    if (tab === "parcours") {
      await drawJourneys();
      // Actualisation automatique, sauf pendant la saisie d'un filtre.
      timer = setInterval(() => {
        if (!app.contains(document.activeElement) || document.activeElement === document.body) drawJourneys();
      }, 10000);
    } else {
      await drawReport();
    }
  };

  await draw();
  return () => clearInterval(timer);
}
