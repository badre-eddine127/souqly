import { UserManager } from "oidc-client-ts";

new UserManager({ authority: "", client_id: "", redirect_uri: "" }).signinSilentCallback().catch(() => undefined);
