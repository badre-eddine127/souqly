// Navigation sans rechargement : change l'adresse puis demande au routeur (app.js) d'afficher la page.
export function navigate(path) {
  history.pushState({}, "", path);
  window.dispatchEvent(new PopStateEvent("popstate"));
}
