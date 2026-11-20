import { createContext, useCallback, useContext, useEffect, useRef, useState, type ReactNode } from "react";
import { useAuth } from "react-oidc-context";
import { cartApi, notificationApi } from "./api/client";
import { rolesOf, setAccessToken, type Role } from "./auth";

type Session = {
  ready: boolean;
  signedIn: boolean;
  name: string;
  roles: Role[];
  is: (role: Role) => boolean;
  login: () => void;
  logout: () => void;
  cartUnits: number;
  refreshCart: () => Promise<void>;
  unread: number;
  refreshNotifications: () => Promise<void>;
  toast: (message: string, tone?: "ok" | "ko") => void;
};

const SessionContext = createContext<Session | null>(null);

export const useSession = () => useContext(SessionContext)!;

/** Session de l'utilisateur : jeton, rôles, compteurs du panier et des notifications, messages éphémères. */
export function SessionProvider({ children }: { children: ReactNode }) {
  const auth = useAuth();
  const triedSilent = useRef(false);
  const [cartUnits, setCartUnits] = useState(0);
  const [unread, setUnread] = useState(0);
  const [toasts, setToasts] = useState<{ id: number; message: string; tone: "ok" | "ko" }[]>([]);

  setAccessToken(auth.user?.access_token);
  const roles = rolesOf(auth.user);

  // À l'ouverture, réutilise la session Keycloak si elle existe (le jeton n'est gardé qu'en mémoire).
  useEffect(() => {
    if (!auth.isLoading && !auth.isAuthenticated && !auth.activeNavigator && !triedSilent.current) {
      triedSilent.current = true;
      auth.signinSilent().catch(() => undefined);
    }
  }, [auth]);

  const refreshCart = useCallback(async () => {
    if (!roles.includes("CUSTOMER")) return setCartUnits(0);
    const { data } = await cartApi.GET("/api/v1/carts/me");
    setCartUnits(Number(data?.totalUnits ?? 0));
  }, [auth.user?.access_token]); // eslint-disable-line react-hooks/exhaustive-deps

  const refreshNotifications = useCallback(async () => {
    if (!auth.isAuthenticated) return setUnread(0);
    const { data } = await notificationApi.GET("/api/v1/notifications", { params: { query: { size: 50 } } });
    setUnread(data?.content.filter((n) => !n.read).length ?? 0);
  }, [auth.isAuthenticated, auth.user?.access_token]); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    refreshCart();
    refreshNotifications();
    const timer = window.setInterval(refreshNotifications, 15000);
    return () => window.clearInterval(timer);
  }, [refreshCart, refreshNotifications]);

  const toast = useCallback((message: string, tone: "ok" | "ko" = "ok") => {
    const id = Date.now() + Math.random();
    setToasts((t) => [...t, { id, message, tone }]);
    window.setTimeout(() => setToasts((t) => t.filter((x) => x.id !== id)), 4500);
  }, []);

  const value: Session = {
    ready: !auth.isLoading,
    signedIn: auth.isAuthenticated,
    name: auth.user?.profile.given_name ?? auth.user?.profile.preferred_username ?? "",
    roles,
    is: (role) => roles.includes(role),
    login: () => auth.signinRedirect({ state: window.location.pathname }),
    logout: () => auth.signoutRedirect(),
    cartUnits,
    refreshCart,
    unread,
    refreshNotifications,
    toast,
  };

  return (
    <SessionContext.Provider value={value}>
      {children}
      <div className="toasts" role="status">
        {toasts.map((t) => (
          <div key={t.id} className={`toast toast-${t.tone}`}>{t.message}</div>
        ))}
      </div>
    </SessionContext.Provider>
  );
}
