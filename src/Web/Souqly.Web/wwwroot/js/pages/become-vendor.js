import { api, get, problemMessage } from "../api.js";
import { esc, formatDate } from "../format.js";
import { navigate } from "../nav.js";
import { session } from "../session.js";
import { toast } from "../ui.js";

const PERKS = [
  ["Une vitrine nationale", "Vos créations présentées aux clients de tout le Maroc, avec vos photos et votre histoire."],
  ["Paiement sécurisé", "Le client paie en ligne ; vous préparez la commande seulement quand elle est payée."],
  ["Un tableau de bord simple", "Ventes du jour, commandes à préparer, ruptures de stock : tout sur un écran."],
];

/** US-16 : demander l'ouverture d'une boutique, puis suivre la décision de l'équipe Souqly. */
export async function becomeVendorPage(app) {
  if (session.is("VENDOR")) {
    app.innerHTML = `
      <section class="vendor-pitch card">
        <span class="eyebrow">Espace vendeur</span>
        <h1>Votre boutique est ouverte</h1>
        <p class="muted">Retrouvez vos ventes du jour et vos commandes à préparer.</p>
        <a class="btn btn-clay" href="/vendeur/tableau-de-bord" data-link>Ouvrir mon tableau de bord</a>
      </section>`;
    return;
  }

  const pitch = `
    <section class="vendor-pitch card">
      <span class="eyebrow">Vendre sur Souqly</span>
      <h1>Ouvrez votre boutique d'artisan</h1>
      <p class="muted">Chaque boutique est vérifiée par l'équipe Souqly avant d'être publiée : c'est la promesse faite à nos clients.</p>
      <div class="perks">${PERKS.map(([t, d]) => `<div><strong>${esc(t)}</strong><p class="muted small">${esc(d)}</p></div>`).join("")}</div>
    </section>`;

  if (!session.user.authenticated) {
    app.innerHTML = `${pitch}
      <div class="empty"><h2>Connectez-vous pour faire votre demande</h2>
        <p>Pas encore de compte ? La page de connexion propose aussi l'inscription.</p>
        <button class="btn" data-action="login">Se connecter ou créer un compte</button></div>`;
    return;
  }

  const mine = await get("/api/v1/vendors/me");
  const application = mine.ok ? mine.data : null;

  const form = (values = {}) => `
    <form class="card vendor-form" novalidate>
      <h2>${application ? "Soumettre à nouveau" : "Votre demande"}</h2>
      <label>Nom de la boutique<input name="shopName" required maxlength="80" value="${esc(values.shopName ?? "")}" placeholder="Atelier Zellige de Fès" /></label>
      <label>Ville<input name="city" required maxlength="60" value="${esc(values.city ?? "")}" placeholder="Fès" /></label>
      <label>Ce que vous fabriquez<textarea name="description" rows="4" maxlength="1000" placeholder="Matières, savoir-faire, années d'expérience…">${esc(values.description ?? "")}</textarea></label>
      <p class="error small" hidden></p>
      <button class="btn btn-clay">Envoyer ma demande</button>
    </form>`;

  let body;
  if (!application) {
    body = form();
  } else if (application.status === "PENDING") {
    body = `
      <div class="card application-status">
        <span class="status status-wait">En cours d'examen</span>
        <h2>${esc(application.shopName)} · ${esc(application.city)}</h2>
        <p class="muted">Demande envoyée le ${formatDate(application.submittedAt)}. Vous serez prévenu dès que l'équipe Souqly aura décidé.</p>
      </div>`;
  } else if (application.status === "APPROVED") {
    body = `
      <div class="card application-status approved">
        <span class="status status-ok">Boutique validée</span>
        <h2>Bienvenue, ${esc(application.shopName)} !</h2>
        <p class="muted">Votre boutique a été validée le ${formatDate(application.decidedAt)}. Activez votre espace vendeur pour publier vos premiers produits.</p>
        <button class="btn btn-clay" data-activate>Activer mon espace vendeur</button>
      </div>`;
  } else {
    body = `
      <div class="card application-status">
        <span class="status status-ko">Demande refusée</span>
        <h2>${esc(application.shopName)}</h2>
        <p><strong>Motif :</strong> ${esc(application.rejectionReason ?? "")}</p>
        <p class="muted small">Vous pouvez corriger votre demande et la soumettre à nouveau.</p>
      </div>
      ${form(application)}`;
  }

  app.innerHTML = `${pitch}<div class="vendor-apply">${body}</div>`;

  app.querySelector("[data-activate]")?.addEventListener("click", async (e) => {
    e.target.disabled = true;
    // Le jeton est renouvelé côté serveur : il porte désormais le rôle VENDOR.
    const { ok } = await api("POST", "/session/refresh");
    await session.load();
    if (ok && session.is("VENDOR")) {
      toast("Espace vendeur activé");
      navigate("/vendeur/tableau-de-bord");
    } else {
      session.login();   // repli : une reconnexion récupère aussi le nouveau rôle
    }
  });

  app.querySelector("form")?.addEventListener("submit", async (e) => {
    e.preventDefault();
    const f = new FormData(e.target);
    const error = e.target.querySelector(".error");
    const shopName = String(f.get("shopName")).trim();
    const city = String(f.get("city")).trim();
    if (!shopName || !city) {
      error.textContent = "Le nom de la boutique et la ville sont obligatoires.";
      error.hidden = false;
      return;
    }
    const { ok, error: problem } = await api("POST", "/api/v1/vendors", {
      body: { shopName, city, description: String(f.get("description")).trim() },
    });
    if (!ok) {
      error.textContent = problemMessage(problem);
      error.hidden = false;
      return;
    }
    toast("Demande envoyée — l'équipe Souqly l'examine");
    becomeVendorPage(app);
  });
}
