const root = document.getElementById("hud-root");
const grid = document.getElementById("ability-grid");

for (let index = 0; index < 12; index += 1) {
  const slot = document.createElement("span");
  slot.className = "ability-slot";
  grid.appendChild(slot);
}

function applyAppearance(appearance = {}) {
  const style = document.documentElement.style;
  style.setProperty("--hud-opacity", String((Number(appearance.opacity) || 82) / 100));
  style.setProperty("--hud-scale", String((Number(appearance.scale) || 100) / 100));
  style.setProperty("--hud-offset-x", `${Number(appearance.offsetX) || 0}px`);
  style.setProperty("--hud-offset-y", `${Number(appearance.offsetY) || 0}px`);
}

window.fireHud?.onState((payload = {}) => {
  applyAppearance(payload.appearance);
  document.body.classList.toggle("preview", Boolean(payload.preview));
  root.classList.add("visible");
});
