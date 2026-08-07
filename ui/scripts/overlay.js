const grid = document.getElementById("skill-grid");
const slots = [];
let overlayState = { skills: [], settings: {} };
let previousActive = Array(12).fill(false);

for (let index = 0; index < 12; index += 1) {
  const slot = document.createElement("div");
  slot.className = "skill-slot";
  slot.innerHTML = '<span class="cooldown-value"></span><span class="skill-key"></span>';
  grid.appendChild(slot);
  slots.push(slot);
}

function applySettings(settings = {}) {
  const root = document.documentElement;
  root.style.setProperty("--overlay-opacity", String((Number(settings.opacity) || 92) / 100));
  root.style.setProperty("--overlay-scale", String((Number(settings.scale) || 100) / 100));
  root.style.setProperty("--overlay-x", `${Number(settings.offsetX) || 0}px`);
  root.style.setProperty("--overlay-y", `${Number(settings.offsetY) || 0}px`);
}

function updateCooldowns() {
  const now = Date.now();
  slots.forEach((slot, index) => {
    const skill = overlayState.skills[index] || {};
    const endAt = Number(skill.endAt) || 0;
    const duration = Math.max(0, Number(skill.duration) || Number(skill.configuredDuration) || 0);
    const remaining = Math.max(0, (endAt - now) / 1000);
    const active = remaining > 0.02 && duration > 0;
    const key = slot.querySelector(".skill-key");
    const value = slot.querySelector(".cooldown-value");

    key.textContent = skill.key || "";
    slot.classList.toggle("active", active);
    if (active) {
      const ratio = Math.max(0, Math.min(1, remaining / duration));
      slot.style.setProperty("--cooldown-angle", `${ratio * 360}deg`);
      value.textContent = remaining < 10 ? remaining.toFixed(1) : String(Math.ceil(remaining));
    } else {
      value.textContent = "";
      if (previousActive[index]) {
        slot.classList.remove("ready");
        void slot.offsetWidth;
        slot.classList.add("ready");
      }
    }
    previousActive[index] = active;
  });
}

window.fireOverlay?.onState((payload) => {
  overlayState = payload || { skills: [], settings: {} };
  document.body.classList.toggle("preview", Boolean(payload.preview));
  applySettings(overlayState.settings);
  updateCooldowns();
});

window.setInterval(updateCooldowns, 50);
