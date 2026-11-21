// Page de connexion Souqly : on tire le cordon de la lampe vers le bas pour l'allumer (ou l'éteindre).
// Allumée, la tête de la lampe se tourne vers le formulaire et l'éclaire comme un projecteur.
// Un clic ou Entrée / Espace sur la lampe fait aussi l'affaire (clavier, lecteurs d'écran).
(function () {
  var body = document.body;
  var lamp = document.getElementById("lamp");
  var head = document.getElementById("head");
  var bulb = document.getElementById("bulb");
  var cord = document.getElementById("cord");
  var knob = document.getElementById("knob");
  var hint = document.getElementById("hint");
  var card = document.getElementById("card");
  var spot = document.getElementById("spot");
  var cone = document.getElementById("spot-cone");
  var gradient = document.getElementById("spot-light");
  var username = document.getElementById("username");
  var password = document.getElementById("password");
  var reveal = document.getElementById("reveal");

  var CORD = 84;        // longueur du cordon au repos (px)
  var MAX_PULL = 90;    // on ne peut pas l'étirer davantage
  var THRESHOLD = 38;   // il faut tirer au moins de cette longueur pour actionner l'interrupteur

  body.classList.add("js"); // sans JavaScript, le formulaire reste visible

  // ---------- allumer / éteindre ----------
  function setLit(lit) {
    body.classList.toggle("lit", lit);
    lamp.setAttribute("aria-pressed", String(lit));
    lamp.setAttribute("aria-label", lit ? "Éteindre la lampe" : "Allumer la lampe pour se connecter");
    hint.textContent = lit ? "Bienvenue dans le souk" : "Tirez le cordon vers le bas pour allumer";
    aim();
    follow(900); // le faisceau suit la tête pendant qu'elle pivote
    if (lit) {
      setTimeout(function () {
        (username.value ? password : username).focus({ preventScroll: true });
      }, 550);
    }
  }
  var toggle = function () { setLit(!body.classList.contains("lit")); };

  // ---------- orientation de la tête et faisceau ----------
  function center(el) {
    var r = el.getBoundingClientRect();
    return { x: r.left + r.width / 2, y: r.top + r.height / 2 };
  }

  // Tourne la tête vers le centre du formulaire (vers la droite sur ordinateur, vers le bas sur téléphone).
  function aim() {
    if (!body.classList.contains("lit")) {
      head.style.setProperty("--tilt", "0deg");
      return;
    }
    var h = head.getBoundingClientRect();
    var pivot = { x: h.left + h.width / 2, y: h.top + h.height * 0.92 };
    var target = center(card);
    // angle entre la verticale (lumière vers le bas) et la direction du formulaire
    var angle = Math.atan2(target.x - pivot.x, target.y - pivot.y) * 180 / Math.PI;
    // inclinaison mesurée : la tête se penche vers le formulaire sans se renverser
    var tilt = Math.max(-30, Math.min(30, -angle * 0.4));
    head.style.setProperty("--tilt", tilt.toFixed(1) + "deg");
  }

  // Enveloppe convexe : le cône part du bulbe et englobe tout le formulaire.
  function hull(points) {
    points.sort(function (a, b) { return a.x - b.x || a.y - b.y; });
    var cross = function (o, a, b) { return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x); };
    var lower = [], upper = [];
    points.forEach(function (p) {
      while (lower.length >= 2 && cross(lower[lower.length - 2], lower[lower.length - 1], p) <= 0) lower.pop();
      lower.push(p);
    });
    points.slice().reverse().forEach(function (p) {
      while (upper.length >= 2 && cross(upper[upper.length - 2], upper[upper.length - 1], p) <= 0) upper.pop();
      upper.push(p);
    });
    return lower.slice(0, -1).concat(upper.slice(0, -1));
  }

  function drawBeam() {
    spot.setAttribute("width", window.innerWidth);
    spot.setAttribute("height", window.innerHeight);
    var b = center(bulb);
    var c = card.getBoundingClientRect();
    var m = 34; // la lumière déborde un peu autour du formulaire
    var dx = (c.left + c.width / 2) - b.x, dy = (c.top + c.height / 2) - b.y;
    var len = Math.hypot(dx, dy) || 1;
    var px = -dy / len * 12, py = dx / len * 12; // largeur de la source lumineuse
    var pts = hull([
      { x: b.x + px, y: b.y + py }, { x: b.x - px, y: b.y - py },
      { x: c.left - m, y: c.top - m }, { x: c.right + m, y: c.top - m },
      { x: c.right + m, y: c.bottom + m }, { x: c.left - m, y: c.bottom + m },
    ]);
    cone.setAttribute("points", pts.map(function (p) { return p.x.toFixed(1) + "," + p.y.toFixed(1); }).join(" "));
    gradient.setAttribute("cx", b.x);
    gradient.setAttribute("cy", b.y);
    gradient.setAttribute("r", Math.hypot(Math.max(Math.abs(c.left - b.x), Math.abs(c.right - b.x)), Math.max(Math.abs(c.top - b.y), Math.abs(c.bottom - b.y))) + m);
  }

  var followUntil = 0;
  function follow(ms) {
    var start = !followUntil || followUntil < performance.now();
    followUntil = performance.now() + ms;
    if (!start) return;
    (function frame() {
      drawBeam();
      if (performance.now() < followUntil) requestAnimationFrame(frame);
      else followUntil = 0;
    })();
  }

  window.addEventListener("resize", function () { aim(); follow(700); });

  // ---------- le cordon : on l'attrape et on le tire vers le bas ----------
  var drag = null;
  var justDragged = false;

  function setPull(px) {
    cord.style.height = (CORD + px) + "px";
  }

  function grab(e) {
    e.preventDefault();
    e.stopPropagation();
    drag = { y: e.clientY, pull: 0, moved: false };
    cord.classList.add("dragging");
    lamp.classList.remove("pull");
    knob.setPointerCapture(e.pointerId);
  }

  knob.addEventListener("pointerdown", grab);
  knob.addEventListener("pointermove", function (e) {
    if (!drag) return;
    var dy = e.clientY - drag.y;
    // résistance : plus on tire, plus c'est dur
    drag.pull = dy <= 0 ? 0 : MAX_PULL * (1 - Math.exp(-dy / MAX_PULL));
    if (Math.abs(dy) > 3) drag.moved = true;
    setPull(drag.pull);
    cord.classList.toggle("ready", drag.pull >= THRESHOLD);
  });
  function release() {
    if (!drag) return;
    var pulled = drag.pull >= THRESHOLD;
    var tapped = !drag.moved;
    cord.classList.remove("dragging", "ready");
    setPull(0); // retour élastique (transition CSS)
    lamp.classList.add("pull");
    justDragged = true;
    setTimeout(function () { justDragged = false; }, 50);
    drag = null;
    if (pulled || tapped) toggle(); // un simple tap sur la perle marche aussi (écrans tactiles)
  }
  knob.addEventListener("pointerup", release);
  knob.addEventListener("pointercancel", release);

  // Clic ou clavier sur la lampe (accessibilité) ; ignoré juste après un glissement du cordon.
  lamp.addEventListener("click", function (e) {
    if (justDragged || e.target === knob) return;
    lamp.classList.remove("pull");
    void lamp.offsetWidth;
    lamp.classList.add("pull");
    toggle();
  });

  if (body.classList.contains("lit")) setLit(true); // erreur ou nom déjà saisi : on garde la lumière

  // ---------- formulaire ----------
  reveal.addEventListener("click", function () {
    var show = password.type === "password";
    password.type = show ? "text" : "password";
    reveal.textContent = show ? "Masquer" : "Afficher";
    reveal.setAttribute("aria-label", show ? "Masquer le mot de passe" : "Afficher le mot de passe");
  });

  document.getElementById("kc-form-login").addEventListener("submit", function () {
    var button = document.getElementById("kc-login");
    button.disabled = true;
    button.textContent = "Connexion…";
  });
})();
