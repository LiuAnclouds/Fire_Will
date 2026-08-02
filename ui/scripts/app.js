const state = {
  project: null,
  selectedFlow: 1,
  cooldownEnds: new Map(),
};

const $ = (id) => document.getElementById(id);

function post(type, payload = {}) {
  if (window.chrome?.webview) {
    window.chrome.webview.postMessage({ type, payload });
  }
}

function setStatus(text) {
  $("status-bar").textContent = text;
}

function render(project) {
  state.project = project;
  $("profile-pill").textContent = `英雄：${project.profileName || "默认/未读取"}`;
  $("window-pill").textContent = project.gameWindowMatcher ? "窗口：已绑定" : "窗口：未绑定";
  $("stop-pill").textContent = `停止键：${project.stopHotkey || "Z"}`;
  $("profile-count").textContent = `${project.profiles.length} 个`;
  $("flow-count").textContent = `${project.checks.enabledFlows} 启用`;
  $("health-label").textContent = project.checks.missingNpc === 0 ? "可运行" : "需标定";
  $("npc-check").textContent = project.checks.missingNpc;
  $("skill-check").textContent = `${project.checks.mappedSkills}/12`;
  $("item-check").textContent = `${project.checks.mappedItems}/6`;
  $("enabled-check").textContent = project.checks.enabledFlows;

  renderProfiles(project);
  renderFlows(project);
  renderSelectedFlow(project);
  renderBindings(project);
  renderCooldowns(project);
  setStatus(project.toast || "配置已载入。新版 UI 目前负责展示与保存用户按键，宏执行仍使用旧版 AHK 逻辑。");
}

function renderProfiles(project) {
  $("profile-list").innerHTML = project.profiles.map((name) => (
    `<div class="profile-card ${name === project.profileName ? "active" : ""}">${escapeHtml(name)}</div>`
  )).join("");
}

function renderFlows(project) {
  $("flow-list").innerHTML = project.flows.map((flow) => (
    `<div class="flow-card ${flow.slot === state.selectedFlow ? "active" : ""}" data-flow="${flow.slot}">
      <b>${escapeHtml(flow.name)}</b>
      <span>${flow.enabled ? "已启用" : "未启用"} · ${flow.hotkey || "无热键"}</span>
    </div>`
  )).join("");

  document.querySelectorAll(".flow-card").forEach((card) => {
    card.addEventListener("click", () => {
      state.selectedFlow = Number(card.dataset.flow);
      renderFlows(state.project);
      renderSelectedFlow(state.project);
    });
  });
}

function renderSelectedFlow(project) {
  const flow = project.flows.find((item) => item.slot === state.selectedFlow) || project.flows[0];
  if (!flow) return;

  $("flow-title").textContent = flow.name;
  $("flow-hotkey").textContent = `触发键：${flow.hotkey || "未设置"}`;
  $("flow-enabled").textContent = flow.enabled ? "已启用" : "未启用";

  const activeGroups = flow.groups.filter((group) => group.enabled);
  $("flow-steps").innerHTML = (activeGroups.length ? activeGroups : flow.groups.slice(0, 3)).map((group) => {
    const name = group.farm && group.farm !== "无" ? group.farm : "空步骤";
    const pre = group.preType && group.preType !== "无" ? `${group.preType}：${group.preValue || "未设置"}` : "组前动作：无";
    return `<article class="step">
      <span class="step-index">${group.group}</span>
      <div>
        <strong>${escapeHtml(name)}</strong>
        <p>${escapeHtml(pre)}</p>
      </div>
      <small>${group.wait || group.duration || 0}ms</small>
    </article>`;
  }).join("");
}

function renderBindings(project) {
  $("farm-bindings").innerHTML = project.farms.map((farm) => (
    `<label class="binding-row" data-farm="${escapeAttr(farm.name)}">
      <span>${escapeHtml(farm.name)}</span>
      <input data-field="actionKey" value="${escapeAttr(farm.actionKey)}" placeholder="菜单键" />
      <input data-field="releaseKey" value="${escapeAttr(farm.releaseKey)}" placeholder="释放" />
    </label>`
  )).join("");

  $("skill-bindings").innerHTML = project.keyMap.skills.map((skill) => (
    `<label class="binding-row" data-skill="${skill.slot}">
      <span>S${skill.slot}</span>
      <input data-field="key" value="${escapeAttr(skill.key)}" placeholder="快捷键" />
      <input data-field="cooldown" type="number" min="0" max="600" value="${skill.cooldown || 0}" title="CD 秒数" />
    </label>`
  )).join("");

  $("item-bindings").innerHTML = project.keyMap.items.map((item) => (
    `<label class="binding-row" data-item="${item.slot}">
      <span>I${item.slot}</span>
      <input data-field="key" value="${escapeAttr(item.key)}" placeholder="快捷键" />
    </label>`
  )).join("");
}

function renderCooldowns(project) {
  $("cooldown-list").innerHTML = project.keyMap.skills.map((skill) => (
    `<div class="cooldown-card" data-cd="${skill.slot}">
      <strong>S${skill.slot} ${escapeHtml(skill.key || "-")}</strong>
      <span class="cooldown-time" data-time="${skill.slot}">就绪</span>
      <button type="button" data-start="${skill.slot}">${skill.cooldown || 0}s</button>
    </div>`
  )).join("");

  document.querySelectorAll("[data-start]").forEach((button) => {
    button.addEventListener("click", () => {
      const slot = Number(button.dataset.start);
      const skill = state.project.keyMap.skills.find((item) => item.slot === slot);
      const seconds = Number(skill?.cooldown || 0);
      if (seconds <= 0) {
        setStatus(`S${slot} 未设置 CD 秒数。`);
        return;
      }
      state.cooldownEnds.set(slot, Date.now() + seconds * 1000);
      tickCooldowns();
    });
  });
}

function collectBindings() {
  const skills = Array.from(document.querySelectorAll("[data-skill]")).map((row) => ({
    slot: Number(row.dataset.skill),
    key: row.querySelector('[data-field="key"]').value.trim(),
    cooldown: Number(row.querySelector('[data-field="cooldown"]').value || 0),
  }));
  const items = Array.from(document.querySelectorAll("[data-item]")).map((row) => ({
    slot: Number(row.dataset.item),
    key: row.querySelector('[data-field="key"]').value.trim(),
  }));
  const farms = Array.from(document.querySelectorAll("[data-farm]")).map((row) => {
    const original = state.project.farms.find((farm) => farm.name === row.dataset.farm);
    return {
      name: row.dataset.farm,
      actionKey: row.querySelector('[data-field="actionKey"]').value.trim(),
      releaseType: original?.releaseType || "无",
      releaseKey: row.querySelector('[data-field="releaseKey"]').value.trim(),
    };
  });

  return { skills, items, farms };
}

function tickCooldowns() {
  const now = Date.now();
  document.querySelectorAll("[data-time]").forEach((node) => {
    const slot = Number(node.dataset.time);
    const end = state.cooldownEnds.get(slot) || 0;
    const left = Math.max(0, Math.ceil((end - now) / 1000));
    node.textContent = left > 0 ? `${left}s` : "就绪";
  });
}

function escapeHtml(value = "") {
  return String(value).replace(/[&<>"']/g, (char) => ({
    "&": "&amp;",
    "<": "&lt;",
    ">": "&gt;",
    '"': "&quot;",
    "'": "&#039;",
  }[char]));
}

function escapeAttr(value = "") {
  return escapeHtml(value).replace(/\n/g, " ");
}

$("save-btn").addEventListener("click", () => post("save-user-bindings", collectBindings()));
$("legacy-btn").addEventListener("click", () => post("open-legacy-ahk"));

if (window.chrome?.webview) {
  window.chrome.webview.addEventListener("message", (event) => {
    if (event.data?.type === "state") {
      render(event.data.payload);
    }
  });
  post("request-state");
} else {
  setStatus("请在 FireWill.App WebView2 壳中打开此界面。");
}

setInterval(tickCooldowns, 250);

