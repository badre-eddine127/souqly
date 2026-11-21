// Appels à l'API Souqly. Le navigateur appelle /api sur le même site ; le serveur ASP.NET Core
// ajoute le jeton Keycloak et relaie vers la passerelle. Aucun jeton n'est visible ici.

export const GATEWAY_DOWN =
  "Les services Souqly ne répondent pas (passerelle arrêtée). Dans Visual Studio, choisissez Souqly.AppHost comme projet de démarrage puis F5.";

/**
 * Appelle l'API et renvoie { ok, status, data, error }.
 * @param {string} method GET, POST, PUT, DELETE
 * @param {string} path   chemin commençant par /api/v1/...
 * @param {{ body?: unknown, headers?: Record<string, string> }} [options]
 */
export async function api(method, path, options = {}) {
  const headers = { "X-Souqly": "1", Accept: "application/json", ...(options.headers ?? {}) };
  let body;
  if (options.body !== undefined) {
    headers["Content-Type"] = "application/json";
    body = JSON.stringify(options.body);
  }

  let response;
  try {
    response = await fetch(path, { method, headers, body, credentials: "same-origin" });
  } catch {
    return { ok: false, status: 0, data: null, error: { detail: "Le serveur ne répond pas." } };
  }

  const text = await response.text();
  let json = null;
  if (text) {
    try {
      json = JSON.parse(text);
    } catch {
      json = null;
    }
  }
  if (response.status === 502 || response.status === 504) {
    // Le site tourne mais la passerelle (port 8080) est arrêtée : on le dit clairement.
    return { ok: false, status: response.status, data: null, error: { detail: GATEWAY_DOWN } };
  }
  return response.ok
    ? { ok: true, status: response.status, data: json, error: null }
    : { ok: false, status: response.status, data: null, error: json ?? { title: `Erreur ${response.status}` } };
}

export const get = (path) => api("GET", path);

/** Message lisible d'une erreur de l'API (ProblemDetail RFC 9457). */
export function problemMessage(error, fallback = "Une erreur est survenue.") {
  if (!error) return fallback;
  if (error.errors) return Object.values(error.errors).flat().join(" ");
  return error.detail ?? error.title ?? fallback;
}

/** Construit une chaîne de requête en ignorant les valeurs vides. */
export function query(params) {
  const q = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) if (v !== undefined && v !== null && v !== "") q.set(k, String(v));
  const s = q.toString();
  return s ? `?${s}` : "";
}
