<#-- Page de connexion Souqly : une lampe dans la nuit. Un clic sur la lampe (ou son cordon) allume la lumière
     et fait apparaître le formulaire. Sans JavaScript, le formulaire reste simplement visible. -->
<!DOCTYPE html>
<html lang="fr">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta name="robots" content="noindex, nofollow">
  <title>Connexion — Souqly</title>
  <link rel="icon" href="${url.resourcesPath}/img/souqly-logo.svg" type="image/svg+xml">
  <link rel="stylesheet" href="${url.resourcesPath}/css/lamp.css">
</head>
<#assign hasError = message?has_content && (message.type = 'error' || message.type = 'warning')>
<body class="souqly-login<#if hasError || (login.username!'')?has_content> lit</#if>">
  <!-- Faisceau de la lampe, dessiné du bulbe jusqu'au formulaire (lamp.js) -->
  <svg class="spot" id="spot" aria-hidden="true">
    <defs>
      <radialGradient id="spot-light" gradientUnits="userSpaceOnUse">
        <stop offset="0" stop-color="#ffe7b0" stop-opacity="0.75"/>
        <stop offset="0.35" stop-color="#ffd890" stop-opacity="0.32"/>
        <stop offset="1" stop-color="#ffcf80" stop-opacity="0.08"/>
      </radialGradient>
      <filter id="spot-soft" x="-20%" y="-20%" width="140%" height="140%"><feGaussianBlur stdDeviation="7"/></filter>
    </defs>
    <polygon id="spot-cone" fill="url(#spot-light)" filter="url(#spot-soft)"/>
  </svg>
  <main class="scene">
    <section class="lamp-zone">
      <button type="button" class="lamp" id="lamp" aria-label="Allumer la lampe pour se connecter" aria-pressed="false">
        <span class="glow" aria-hidden="true"></span>
        <span class="head" id="head" aria-hidden="true">
          <span class="shade"><span class="shade-pattern"></span></span>
          <span class="bulb" id="bulb"></span>
        </span>
        <span class="stem" aria-hidden="true"></span>
        <span class="cord" id="cord" aria-hidden="true"><span class="knob" id="knob"></span></span>
        <span class="base" aria-hidden="true"></span>
      </button>
      <p class="hint" id="hint">Tirez le cordon vers le bas pour allumer</p>
    </section>

    <section class="login-card" id="card" aria-labelledby="title">
      <div class="brand">
        <img src="${url.resourcesPath}/img/souqly-logo.svg" alt="">
        <span>Souq<em>ly</em></span>
      </div>
      <h1 id="title">Marhba bik !</h1>
      <p class="subtitle">Connectez-vous pour retrouver votre panier, vos commandes et votre atelier.</p>

      <#if message?has_content && (message.type != 'warning' || !isAppInitiatedAction??)>
        <div class="alert alert-${message.type}" role="alert">${kcSanitize(message.summary)?no_esc}</div>
      </#if>

      <form id="kc-form-login" action="${url.loginAction}" method="post" novalidate>
        <div class="field">
          <label for="username">Nom d'utilisateur ou e-mail</label>
          <input id="username" name="username" type="text" value="${(login.username!'')}" autocomplete="username"
                 autocapitalize="off" spellcheck="false" placeholder="ex. sara" required
                 <#if messagesPerField.existsError('username','password')>aria-invalid="true"</#if>>
        </div>
        <div class="field">
          <div class="label-row">
            <label for="password">Mot de passe</label>
            <#if realm.resetPasswordAllowed><a href="${url.loginResetCredentialsUrl}">Mot de passe oublié ?</a></#if>
          </div>
          <div class="password">
            <input id="password" name="password" type="password" autocomplete="current-password" placeholder="Votre mot de passe" required
                   <#if messagesPerField.existsError('username','password')>aria-invalid="true"</#if>>
            <button type="button" class="reveal" id="reveal" aria-label="Afficher le mot de passe">Afficher</button>
          </div>
        </div>
        <#if realm.rememberMe && !usernameHidden??>
          <label class="remember"><input type="checkbox" name="rememberMe" <#if login.rememberMe??>checked</#if>> Rester connecté</label>
        </#if>
        <input type="hidden" id="id-hidden-input" name="credentialId" <#if auth.selectedCredential?has_content>value="${auth.selectedCredential}"</#if>>
        <button type="submit" class="submit" id="kc-login">Se connecter</button>
      </form>

      <#if realm.password && realm.registrationAllowed && !registrationDisabled??>
        <p class="register">Nouveau sur Souqly ? <a href="${url.registrationUrl}">Créer un compte</a></p>
      </#if>
      <p class="demo">Comptes de démonstration : <strong>sara</strong> (cliente), <strong>fatima</strong> et <strong>karim</strong> (vendeurs).</p>
    </section>
  </main>
  <script src="${url.resourcesPath}/js/lamp.js"></script>
</body>
</html>
