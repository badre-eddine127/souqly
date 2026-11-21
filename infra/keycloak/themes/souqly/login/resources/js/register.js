// Inscription Souqly : chaque champ correctement rempli allume une vitre de la lanterne.
// Toutes allumées : la lanterne illumine la nuit d'étoiles, des étincelles jaillissent, le bouton s'anime.
(function () {
  var body = document.body;
  var fields = Array.prototype.slice.call(document.querySelectorAll("[data-step]"));
  var panes = Array.prototype.slice.call(document.querySelectorAll(".pane"));
  var text = document.getElementById("progress-text");
  var password = document.getElementById("password");
  var confirm = document.getElementById("password-confirm");
  var bar = document.getElementById("strength-bar");
  var label = document.getElementById("strength-label");
  var match = document.getElementById("match");
  var sparkles = document.getElementById("sparkles");
  var wasComplete = false;
  var MIN_LENGTH = 12; // politique de mot de passe du realm Souqly

  var MESSAGES = [
    "Remplissez le formulaire pour allumer la lanterne",
    "Une première lueur…",
    "La flamme prend",
    "La lanterne se réchauffe",
    "Encore un peu de lumière",
    "Presque prête à briller",
  ];

  function strength(value) {
    var score = 0;
    if (value.length >= 8) score++;
    if (value.length >= MIN_LENGTH) score++;
    if (/[0-9]/.test(value)) score++;
    if (/[a-z]/.test(value) && /[A-Z]/.test(value)) score++;
    if (/[^A-Za-z0-9]/.test(value)) score++;
    return score;
  }

  function valid(input) {
    var v = input.value.trim();
    if (!v) return false;
    if (input.type === "email") return /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/.test(v);
    if (input === password) return v.length >= MIN_LENGTH;
    if (input === confirm) return v === password.value;
    return true;
  }

  function update() {
    var done = fields.filter(valid).length;
    var p = done / fields.length;
    body.style.setProperty("--p", p.toFixed(3));
    fields.forEach(function (f) { f.classList.toggle("done", valid(f)); });

    // les vitres s'allument dans l'ordre, une par champ réussi (6 vitres pour 5 ou 6 champs)
    var lit = Math.round(p * panes.length);
    panes.forEach(function (pane, i) { pane.classList.toggle("on", i < lit); });

    var complete = done === fields.length;
    body.classList.toggle("complete", complete);
    text.textContent = complete ? "La lanterne brille : bienvenue au souk !" : MESSAGES[Math.min(done, MESSAGES.length - 1)];
    if (complete && !wasComplete) burst();
    wasComplete = complete;

    // force du mot de passe
    var s = strength(password.value);
    var colors = ["#c0573f", "#c0573f", "#d08a3c", "#c99b50", "#7d9a4f", "#4f6b3a"];
    var names = ["Trop court", "Faible", "Moyen", "Correct", "Solide", "Excellent"];
    bar.style.width = (password.value ? Math.max(12, s * 20) : 0) + "%";
    bar.style.background = colors[s];
    label.textContent = password.value ? "Force : " + names[s] + (password.value.length < MIN_LENGTH ? " — " + MIN_LENGTH + " caractères au moins" : "") : MIN_LENGTH + " caractères au moins";

    if (!confirm.value) { match.textContent = ""; match.className = "match"; }
    else if (confirm.value === password.value) { match.textContent = "Les mots de passe correspondent"; match.className = "match ok"; }
    else { match.textContent = "Les mots de passe ne correspondent pas"; match.className = "match ko"; }
  }

  // Étincelles en étoile autour de la lanterne
  function burst() {
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;
    var lantern = document.getElementById("lantern").getBoundingClientRect();
    var cx = lantern.left + lantern.width / 2, cy = lantern.top + lantern.height / 2;
    for (var i = 0; i < 28; i++) {
      var s = document.createElement("span");
      var angle = Math.random() * Math.PI * 2, dist = 120 + Math.random() * 260;
      s.className = "spark";
      s.style.left = cx + "px";
      s.style.top = cy + "px";
      s.style.setProperty("--dx", Math.cos(angle) * dist + "px");
      s.style.setProperty("--dy", Math.sin(angle) * dist + "px");
      s.style.animationDelay = Math.random() * 0.25 + "s";
      var size = 6 + Math.random() * 10;
      s.style.width = s.style.height = size + "px";
      sparkles.appendChild(s);
      setTimeout(function (el) { el.remove(); }.bind(null, s), 1900);
    }
  }

  fields.forEach(function (f) { f.addEventListener("input", update); });

  Array.prototype.forEach.call(document.querySelectorAll("[data-reveal]"), function (button) {
    button.addEventListener("click", function () {
      var input = document.getElementById(button.getAttribute("data-reveal"));
      var show = input.type === "password";
      input.type = show ? "text" : "password";
      button.textContent = show ? "Masquer" : "Afficher";
    });
  });

  document.getElementById("kc-register-form").addEventListener("submit", function () {
    var button = document.getElementById("kc-register");
    button.disabled = true;
    button.textContent = "Création du compte…";
  });

  update();
})();
