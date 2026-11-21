// Fiche produit : le produit tourne sur lui-même, comme sur un plateau de vitrine, et montre ses deux faces.
// Rotation automatique lente ; on peut aussi le faire tourner à la souris ou au doigt (glisser), avec élan.
// La rotation automatique reprend 3 s après la dernière manipulation. Coupé si l'utilisateur réduit les animations.

const AUTO_SPEED = 24;   // degrés par seconde (un tour en 15 s)
const RESUME_AFTER = 3000;

/**
 * @param {HTMLElement} stage élément .turntable qui contient .spin
 * @returns {() => void} nettoyage (arrête l'animation, retire les écouteurs)
 */
export function mountSpin(stage) {
  const spin = stage.querySelector(".spin");
  if (!spin) return () => {};
  const reduced = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

  let angle = -25;        // départ de trois quarts : on voit tout de suite que l'objet tourne
  let velocity = 0;       // élan après un glissé (degrés par seconde)
  let dragging = false;
  let lastX = 0;
  let lastMove = 0;
  let pausedUntil = reduced ? Infinity : 0;
  let previous = performance.now();
  let frame = 0;

  const draw = () => {
    const a = ((angle % 360) + 360) % 360;
    spin.style.transform = `rotateY(${angle}deg)`;
    // L'ombre se resserre quand le produit est de profil, et la lumière glisse sur la face.
    const facing = Math.abs(Math.cos((a * Math.PI) / 180));
    stage.style.setProperty("--shadow-scale", (0.55 + 0.45 * facing).toFixed(3));
    stage.style.setProperty("--light", `${50 + Math.sin((a * Math.PI) / 180) * 45}%`);
    stage.classList.toggle("showing-back", a > 90 && a < 270);
  };

  const tick = (now) => {
    const dt = Math.min(0.05, (now - previous) / 1000);
    previous = now;
    if (!dragging) {
      if (Math.abs(velocity) > 1) {
        angle += velocity * dt;
        velocity *= Math.pow(0.04, dt);   // frottement : l'élan s'éteint en ~1 s
      } else if (now > pausedUntil) {
        angle += AUTO_SPEED * dt;
      }
    }
    draw();
    frame = requestAnimationFrame(tick);
  };

  const down = (e) => {
    dragging = true;
    velocity = 0;
    lastX = e.clientX;
    lastMove = performance.now();
    stage.setPointerCapture(e.pointerId);
    stage.classList.add("grabbing");
  };
  const move = (e) => {
    if (!dragging) return;
    const now = performance.now();
    const delta = (e.clientX - lastX) * 0.6;   // 1 px ≈ 0,6 degré
    angle += delta;
    velocity = (delta / Math.max(1, now - lastMove)) * 1000;
    lastX = e.clientX;
    lastMove = now;
  };
  const up = () => {
    if (!dragging) return;
    dragging = false;
    pausedUntil = reduced ? Infinity : performance.now() + RESUME_AFTER;
    stage.classList.remove("grabbing");
  };
  // Bouton « retourner » : un demi-tour animé vers l'autre face.
  const flip = () => {
    velocity = 0;
    const start = angle;
    const end = (Math.round(angle / 180) + 1) * 180;   // la face opposée à celle qu'on voit, bien de face
    const began = performance.now();
    pausedUntil = Infinity;
    const animate = (now) => {
      const t = Math.min(1, (now - began) / 700);
      angle = start + (end - start) * (1 - Math.pow(1 - t, 3));
      if (t < 1) requestAnimationFrame(animate);
      else pausedUntil = reduced ? Infinity : performance.now() + RESUME_AFTER;
    };
    requestAnimationFrame(animate);
  };

  const button = stage.parentElement.querySelector("[data-flip]");
  stage.addEventListener("pointerdown", down);
  stage.addEventListener("pointermove", move);
  stage.addEventListener("pointerup", up);
  stage.addEventListener("pointercancel", up);
  button?.addEventListener("click", flip);

  draw();
  frame = requestAnimationFrame(tick);

  return () => {
    cancelAnimationFrame(frame);
    stage.removeEventListener("pointerdown", down);
    stage.removeEventListener("pointermove", move);
    stage.removeEventListener("pointerup", up);
    stage.removeEventListener("pointercancel", up);
    button?.removeEventListener("click", flip);
  };
}
