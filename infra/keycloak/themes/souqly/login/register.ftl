<#-- Inscription Souqly : une lanterne marocaine s'allume vitre après vitre à mesure que le formulaire se remplit.
     Quand tout est prêt, elle illumine la nuit d'étoiles. Sans JavaScript, la page reste un formulaire classique. -->
<!DOCTYPE html>
<html lang="fr">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta name="robots" content="noindex, nofollow">
  <title>Créer un compte — Souqly</title>
  <link rel="icon" href="${url.resourcesPath}/img/souqly-logo.svg" type="image/svg+xml">
  <link rel="stylesheet" href="${url.resourcesPath}/css/lamp.css">
  <link rel="stylesheet" href="${url.resourcesPath}/css/register.css">
</head>
<#macro fieldError name>
  <#if messagesPerField.existsError(name)><span class="field-error" role="alert">${kcSanitize(messagesPerField.get(name))?no_esc}</span></#if>
</#macro>
<#function value name><#return (profile.attributesByName[name].value)!''></#function>
<body class="souqly-register">
  <div class="stars" aria-hidden="true"></div>
  <div class="sparkles" id="sparkles" aria-hidden="true"></div>

  <main class="scene register-scene">
    <section class="lantern-zone" aria-hidden="true">
      <div class="lantern" id="lantern">
        <svg viewBox="0 0 200 330" class="lantern-svg">
          <defs>
            <radialGradient id="flame" cx="50%" cy="55%" r="50%">
              <stop offset="0" stop-color="#fff6d8"/>
              <stop offset="0.45" stop-color="#ffd27a"/>
              <stop offset="1" stop-color="#ff9b3d" stop-opacity="0"/>
            </radialGradient>
            <pattern id="cut" width="20" height="20" patternUnits="userSpaceOnUse">
              <path d="M10 2l2.4 5.6L18 10l-5.6 2.4L10 18l-2.4-5.6L2 10l5.6-2.4z" fill="#000"/>
            </pattern>
          </defs>
          <!-- chaîne et anneau -->
          <line x1="100" y1="0" x2="100" y2="26" stroke="#9c7434" stroke-width="3"/>
          <circle cx="100" cy="32" r="8" fill="none" stroke="#c99b50" stroke-width="3"/>
          <!-- coupole -->
          <path d="M62 78 Q100 26 138 78 Z" fill="#b8893f"/>
          <path d="M100 40 L100 30" stroke="#c99b50" stroke-width="4"/>
          <rect x="54" y="76" width="92" height="10" rx="3" fill="#c99b50"/>
          <!-- corps : six vitres -->
          <g id="panes">
            <path class="pane" d="M58 88 H80 V250 H64 L58 240 Z"/>
            <path class="pane" d="M80 88 H100 V250 H80 Z"/>
            <path class="pane" d="M100 88 H120 V250 H100 Z"/>
            <path class="pane" d="M120 88 H142 V240 L136 250 H120 Z"/>
            <path class="pane" d="M66 250 H100 V268 H74 Z"/>
            <path class="pane" d="M100 250 H134 L126 268 H100 Z"/>
          </g>
          <!-- flamme -->
          <ellipse class="flame" cx="100" cy="176" rx="34" ry="60" fill="url(#flame)"/>
          <!-- armature ciselée en laiton par-dessus -->
          <g class="frame" fill="none" stroke="#c99b50" stroke-width="3">
            <path d="M58 88 H142 V240 L126 268 H74 L58 240 Z"/>
            <path d="M80 88 V250 M100 88 V268 M120 88 V250 M64 250 H136"/>
            <path d="M58 130 H142 M58 210 H142" stroke-width="2"/>
          </g>
          <g class="motifs" fill="none" stroke="#e8c27a" stroke-width="1.4">
            <path d="M69 160l3 7 7 3-7 3-3 7-3-7-7-3 7-3z M90 160l3 7 7 3-7 3-3 7-3-7-7-3 7-3z M110 160l3 7 7 3-7 3-3 7-3-7-7-3 7-3z M131 160l3 7 7 3-7 3-3 7-3-7-7-3 7-3z"/>
          </g>
          <!-- pied -->
          <path d="M84 268 H116 L110 286 H90 Z" fill="#b8893f"/>
          <circle cx="100" cy="294" r="6" fill="#c99b50"/>
        </svg>
        <div class="lantern-glow"></div>
      </div>
      <p class="progress-text" id="progress-text">Remplissez le formulaire pour allumer la lanterne</p>
      <div class="progress" aria-hidden="true"><span id="progress-bar"></span></div>
    </section>

    <section class="login-card register-card" id="card" aria-labelledby="title">
      <div class="brand">
        <img src="${url.resourcesPath}/img/souqly-logo.svg" alt="">
        <span>Souq<em>ly</em></span>
      </div>
      <h1 id="title">Rejoindre le souk</h1>
      <p class="subtitle">Créez votre compte client pour commander auprès des artisans.</p>

      <#if message?has_content && message.type != 'success'>
        <div class="alert alert-${message.type}" role="alert">${kcSanitize(message.summary)?no_esc}</div>
      </#if>

      <form id="kc-register-form" action="${url.registrationAction}" method="post" novalidate>
        <div class="row">
          <div class="field">
            <label for="firstName">Prénom</label>
            <input id="firstName" name="firstName" type="text" value="${value('firstName')}" autocomplete="given-name" required data-step
                   <#if messagesPerField.existsError('firstName')>aria-invalid="true"</#if>>
            <@fieldError "firstName"/>
          </div>
          <div class="field">
            <label for="lastName">Nom</label>
            <input id="lastName" name="lastName" type="text" value="${value('lastName')}" autocomplete="family-name" required data-step
                   <#if messagesPerField.existsError('lastName')>aria-invalid="true"</#if>>
            <@fieldError "lastName"/>
          </div>
        </div>

        <div class="field">
          <label for="email">E-mail</label>
          <input id="email" name="email" type="email" value="${value('email')}" autocomplete="email" placeholder="vous@exemple.ma" required data-step
                 <#if messagesPerField.existsError('email')>aria-invalid="true"</#if>>
          <@fieldError "email"/>
        </div>

        <#if !realm.registrationEmailAsUsername>
          <div class="field">
            <label for="username">Nom d'utilisateur</label>
            <input id="username" name="username" type="text" value="${value('username')}" autocomplete="username" autocapitalize="off" spellcheck="false" required data-step
                   <#if messagesPerField.existsError('username')>aria-invalid="true"</#if>>
            <@fieldError "username"/>
          </div>
        </#if>

        <div class="field">
          <label for="password">Mot de passe</label>
          <div class="password">
            <input id="password" name="password" type="password" autocomplete="new-password" required data-step
                   <#if messagesPerField.existsError('password','password-confirm')>aria-invalid="true"</#if>>
            <button type="button" class="reveal" data-reveal="password" aria-label="Afficher le mot de passe">Afficher</button>
          </div>
          <div class="strength" aria-hidden="true"><span id="strength-bar"></span></div>
          <span class="strength-label" id="strength-label">12 caractères au moins</span>
          <@fieldError "password"/>
        </div>

        <div class="field">
          <label for="password-confirm">Confirmer le mot de passe</label>
          <div class="password">
            <input id="password-confirm" name="password-confirm" type="password" autocomplete="new-password" required data-step
                   <#if messagesPerField.existsError('password-confirm')>aria-invalid="true"</#if>>
            <button type="button" class="reveal" data-reveal="password-confirm" aria-label="Afficher la confirmation">Afficher</button>
          </div>
          <span class="match" id="match" aria-live="polite"></span>
          <@fieldError "password-confirm"/>
        </div>

        <button type="submit" class="submit" id="kc-register">Créer mon compte</button>
      </form>

      <p class="register">Déjà inscrit ? <a href="${url.loginUrl}">Se connecter</a></p>
    </section>
  </main>
  <script src="${url.resourcesPath}/js/register.js"></script>
</body>
</html>
