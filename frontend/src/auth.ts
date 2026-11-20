import { InMemoryWebStorage, WebStorageStateStore, type User } from "oidc-client-ts";
import type { AuthProviderProps } from "react-oidc-context";

// Client public « souqly-web » de Keycloak : Authorization Code + PKCE, aucun secret dans le navigateur.
// Le jeton reste en mémoire (pas de localStorage) ; à l'ouverture, une connexion silencieuse réutilise la session Keycloak.
export const oidcConfig: AuthProviderProps = {
  authority: import.meta.env.VITE_OIDC_AUTHORITY ?? "http://localhost:8180/realms/souqly",
  client_id: "souqly-web",
  redirect_uri: `${window.location.origin}/`,
  post_logout_redirect_uri: `${window.location.origin}/`,
  silent_redirect_uri: `${window.location.origin}/silent-renew.html`,
  scope: "openid profile email",
  automaticSilentRenew: true,
  userStore: new WebStorageStateStore({ store: new InMemoryWebStorage() }),
  onSigninCallback: () => window.history.replaceState({}, document.title, window.location.pathname),
};

export type Role = "CUSTOMER" | "VENDOR" | "ADMIN";

/** Rôles du realm lus dans le jeton d'accès (pour l'affichage seulement : le contrôle réel est côté serveur). */
export function rolesOf(user: User | null | undefined): Role[] {
  if (!user?.access_token) return [];
  try {
    const payload = JSON.parse(atob(user.access_token.split(".")[1].replace(/-/g, "+").replace(/_/g, "/")));
    return (payload.realm_access?.roles ?? []) as Role[];
  } catch {
    return [];
  }
}

// Le client d'API lit le jeton courant ici, sans dépendre de React.
let currentToken: string | undefined;
export const setAccessToken = (token: string | undefined) => {
  currentToken = token;
};
export const accessToken = () => currentToken;
