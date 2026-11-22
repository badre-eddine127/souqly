// Suivi de commande : une camionnette de livraison roule le long des étapes avec le colis sur le toit.
// Elle part de l'étape déjà affichée (ou de « Reçue » à l'ouverture) et s'arrête à l'étape actuelle ;
// à la livraison, elle atteint la fin, dépose le colis et la page annonce « Commande livrée ! ».

const STEP_MS = 650;   // durée par étape parcourue

// Dessin original : fourgonnette terre cuite, vitre, phare, colis attaché sur le toit.
export const VAN_SVG = `
<svg viewBox="0 0 96 64" aria-hidden="true">
  <g class="van-body">
    <rect class="parcel" x="16" y="2" width="30" height="20" rx="3"/>
    <path class="parcel-tape" d="M31 2v20M16 12h30"/>
    <path class="body" d="M6 22h52v26H6a4 4 0 0 1-4-4V26a4 4 0 0 1 4-4Z"/>
    <path class="cab" d="M58 26h14c3 0 5 1 7 3l9 10c1 1 2 3 2 5v4H58Z"/>
    <path class="window" d="M62 30h9c2 0 3 1 4 2l7 8H62Z"/>
    <rect class="stripe" x="2" y="36" width="56" height="4"/>
    <circle class="lamp" cx="88" cy="44" r="2"/>
    <text x="30" y="34" text-anchor="middle" class="logo">S</text>
  </g>
  <g class="wheel" style="transform-origin:22px 52px"><circle cx="22" cy="52" r="9"/><circle class="hub" cx="22" cy="52" r="3.5"/><path d="M22 44v16M14 52h16"/></g>
  <g class="wheel" style="transform-origin:72px 52px"><circle cx="72" cy="52" r="9"/><circle class="hub" cx="72" cy="52" r="3.5"/><path d="M72 44v16M64 52h16"/></g>
</svg>`;

/** Position (en %) du centre de l'étape i sur n. */
const position = (i, n) => ((i + 0.5) / n) * 100;

/**
 * @param {HTMLElement} timeline la liste ol.timeline (une ligne par étape), dans un conteneur .tracker
 * @param {number} from étape de départ (déjà vue)
 * @param {number} to étape actuelle
 * @param {boolean} delivered la commande est livrée (dernière étape)
 */
export function driveVan(timeline, from, to, delivered) {
  const steps = timeline.children.length;
  const road = document.createElement("div");
  road.className = "van-road";
  road.innerHTML = `<div class="van">${VAN_SVG}<span class="smoke"></span></div>`;
  timeline.parentElement.append(road);   // à côté de la liste (une liste ne contient que des étapes)
  const van = road.querySelector(".van");
  const reduced = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

  const finish = (animated = true) => {
    van.classList.remove("driving");
    if (delivered) {
      // Le colis descend sur la dernière étape (sans rejouer l'animation à chaque rafraîchissement de la page).
      van.classList.add(animated ? "dropped" : "emptied");
      timeline.classList.add("arrived");
    }
  };

  van.style.left = `${position(from, steps)}%`;
  if (reduced || from === to) {
    van.style.left = `${position(to, steps)}%`;
    finish(from !== to);
    return;
  }

  const duration = Math.max(1, Math.abs(to - from)) * STEP_MS;
  van.style.transitionDuration = `${duration}ms`;
  requestAnimationFrame(() =>
    requestAnimationFrame(() => {
      van.classList.add("driving");
      van.style.left = `${position(to, steps)}%`;
    }),
  );
  van.addEventListener("transitionend", () => finish(), { once: true });
}
