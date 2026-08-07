const state = {
  project: null,
  selectedFlow: 1,
};

let cancelActiveCapture = null;

const $ = (id) => document.getElementById(id);

function setStatus(text) {
  $("status-bar").textContent = `状态：${text}`;
}

function renderGameSession(session = {}) {
  const stateText = session.bound && !session.ready
    ? "已绑定 · CD叠加可用"
    : session.ready
    ? (session.projectionReady ? "已初始化 · 投影可用" : "已绑定 · 等待镜头校验")
    : (session.state || "未初始化");
  const node = $("game-session-state");
  if (node) {
    node.textContent = stateText;
    node.title = session.message || "";
    node.dataset.ready = session.ready ? "1" : "0";
  }
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

function optionsHtml(options, selected, includeNone = false) {
  const values = includeNone ? ["无", ...options] : options;
  return values.map((value) => (
    `<option value="${escapeAttr(value)}" ${value === selected ? "selected" : ""}>${escapeHtml(value)}</option>`
  )).join("");
}

function render(project) {
  state.project = project;
  $("current-profile").textContent = project.profileName || "默认/未读取";
  $("stop-hotkey").value = project.stopHotkey || "Z";
  renderFarmRows(project);
  renderFarmTarget(project);
  renderFlowSelector(project);
  renderFlow(project);
  renderGameSession(project.gameSession);
  setStatus(project.toast || "已加载配置。布局字段对应旧版 AHK 配置器。");
}

function renderFarmRows(project) {
  const farmNames = project.options?.farmNames || project.farms.map((farm) => farm.name);
  $("farm-rows").innerHTML = project.farms.map((farm) => (
    `<label class="farm-row" data-farm="${escapeAttr(farm.name)}">
      <span>${escapeHtml(farm.name)}</span>
      <span class="capture-group"><input data-field="actionKey" value="${escapeAttr(farm.actionKey)}" /><button type="button" class="capture-button" data-legacy="${escapeAttr(farm.name)}动作键">采</button></span>
      <select data-field="releaseType">${optionsHtml(project.options?.releaseTypeOptions || [], farm.releaseType)}</select>
      <span class="capture-group"><input data-field="releaseKey" value="${escapeAttr(farm.releaseKey)}" /><button type="button" class="capture-button" data-legacy="${escapeAttr(farm.name)}释放键">采</button></span>
      <input data-field="targetX" value="${escapeAttr(farm.targetX)}" />
      <input data-field="targetY" value="${escapeAttr(farm.targetY)}" />
      <button type="button" class="point-capture" data-legacy="${escapeAttr(farm.name)}鼠标点">采鼠标点</button>
    </label>`
  )).join("");
  bindLegacyActions();
}

function renderFarmTarget(project) {
  const farmNames = project.options?.farmNames || project.farms.map((farm) => farm.name);
  $("farm-target").innerHTML = optionsHtml(farmNames, farmNames[0]);
}

function renderFlowSelector(project) {
  $("flow-select").innerHTML = project.flows.map((flow) => (
    `<option value="${flow.slot}" ${flow.slot === state.selectedFlow ? "selected" : ""}>${escapeHtml(flow.name)}</option>`
  )).join("");
}

function renderFlow(project) {
  const flow = project.flows.find((item) => item.slot === state.selectedFlow) || project.flows[0];
  if (!flow) return;
  state.selectedFlow = flow.slot;
  $("flow-select").value = String(flow.slot);
  $("flow-name").value = flow.name;
  $("flow-enabled").checked = Boolean(flow.enabled);
  $("flow-hotkey").value = flow.hotkey || "";
  renderDelayGrid(flow);
  renderGroupRows(project, flow);
  bindLegacyActions();
}

const delayFields = [
  ["key", "普通键耗时"],
  ["skillKey", "技能键耗时"],
  ["teleport", "F2/F3耗时"],
  ["npcClick", "NPC点击耗时"],
  ["mouse", "NPC移鼠耗时"],
  ["releaseMouse", "技能移鼠耗时"],
  ["chat", "公屏耗时"],
  ["heroSelect", "F1按住（最少50）"],
];

function renderDelayGrid(flow) {
  $("delay-grid").innerHTML = delayFields.map(([key, label]) => (
    `<label class="delay-item">
      <span>${label}</span>
      <input data-delay="${key}" type="number" min="0" max="60000" value="${Number(flow.delays?.[key] || 0)}" />
      <button type="button" data-delay-step="${key}" data-step="-10">-</button>
      <button type="button" data-delay-step="${key}" data-step="10">+</button>
    </label>`
  )).join("");

  document.querySelectorAll("[data-delay-step]").forEach((button) => {
    button.addEventListener("click", () => {
      const field = document.querySelector(`[data-delay="${button.dataset.delayStep}"]`);
      field.value = Math.max(0, Number(field.value || 0) + Number(button.dataset.step));
    });
  });
}

function renderGroupRows(project, flow) {
  const farms = project.options?.farmNames || project.farms.map((farm) => farm.name);
  const preTypes = project.options?.preTypeOptions || ["无", "按键", "公屏"];
  $("group-rows").innerHTML = flow.groups.map((group) => (
    `<div class="group-row" data-group="${group.group}">
      <span>${group.group}</span>
      <label class="check-label"><input data-field="enabled" type="checkbox" ${group.enabled ? "checked" : ""} /></label>
      <select data-field="preType">${optionsHtml(preTypes, group.preType)}</select>
      <span class="capture-field"><input data-field="preValue" value="${escapeAttr(group.preValue)}" /><button type="button" data-legacy="ID${group.group}组前按键">采</button></span>
      <select data-field="farm">${optionsHtml(farms, group.farm, true)}</select>
      <input class="duration-input" data-field="used" value="${Number(group.used || 0)}" disabled />
      <input class="duration-input" data-field="duration" value="${Number(group.duration || 0)}" disabled />
      <input data-field="wait" type="number" min="0" value="${Number(group.wait || 0)}" />
      <span class="delay-buttons"><button type="button" data-group-step="${group.group}" data-step="-10">-</button><button type="button" data-group-step="${group.group}" data-step="10">+</button></span>
    </div>`
  )).join("");

  document.querySelectorAll("[data-group-step]").forEach((button) => {
    button.addEventListener("click", () => {
      const row = document.querySelector(`[data-group="${button.dataset.groupStep}"]`);
      const wait = row.querySelector('[data-field="wait"]');
      wait.value = Math.max(0, Number(wait.value || 0) + Number(button.dataset.step));
      updateUsedDuration(row);
    });
  });

  document.querySelectorAll('[data-field="wait"]').forEach((input) => {
    input.addEventListener("input", () => updateUsedDuration(input.closest("[data-group]")));
  });
}

function updateUsedDuration(row) {
  if (!row) return;
  const duration = Number(row.querySelector('[data-field="duration"]').value || 0);
  const wait = Number(row.querySelector('[data-field="wait"]').value || 0);
  row.querySelector('[data-field="used"]').value = Math.max(0, duration - wait);
}

function syncFlowToState() {
  const flow = state.project?.flows.find((item) => item.slot === state.selectedFlow);
  if (!flow) return;
  flow.name = $("flow-name").value.trim() || `自定义流程${flow.slot}`;
  flow.enabled = $("flow-enabled").checked;
  flow.hotkey = $("flow-hotkey").value.trim();
  flow.delays = Object.fromEntries(delayFields.map(([key]) => [key, Math.max(0, Number(document.querySelector(`[data-delay="${key}"]`).value || 0))]));
  flow.groups = Array.from(document.querySelectorAll("[data-group]")).map((row) => ({
    group: Number(row.dataset.group),
    enabled: row.querySelector('[data-field="enabled"]').checked,
    preType: row.querySelector('[data-field="preType"]').value,
    preValue: row.querySelector('[data-field="preValue"]').value.trim(),
    farm: row.querySelector('[data-field="farm"]').value,
    used: Number(row.querySelector('[data-field="used"]').value || 0),
    duration: Number(row.querySelector('[data-field="duration"]').value || 0),
    wait: Math.max(0, Number(row.querySelector('[data-field="wait"]').value || 0)),
  }));
}

function collectLayout() {
  syncFlowToState();
  const farms = Array.from(document.querySelectorAll("[data-farm]")).map((row) => ({
    name: row.dataset.farm,
    actionKey: row.querySelector('[data-field="actionKey"]').value.trim(),
    releaseType: row.querySelector('[data-field="releaseType"]').value,
    releaseKey: row.querySelector('[data-field="releaseKey"]').value.trim(),
    targetX: row.querySelector('[data-field="targetX"]').value.trim(),
    targetY: row.querySelector('[data-field="targetY"]').value.trim(),
  }));
  return {
    stopHotkey: $("stop-hotkey").value.trim() || "Z",
    farms,
    flows: state.project.flows,
  };
}

async function saveLayout() {
  if (!window.fireWill) return;
  const project = await window.fireWill.saveLayout(collectLayout());
  render(project);
}

function clearFarmSettings() {
  document.querySelectorAll("[data-farm] input").forEach((input) => { input.value = ""; });
  document.querySelectorAll("[data-farm] select").forEach((select) => { select.value = "无"; });
  setStatus("已清空当前页面的刷本字段，保存后写入配置。");
}

function clearCurrentFlow() {
  $("flow-name").value = `自定义流程${state.selectedFlow}`;
  $("flow-enabled").checked = false;
  $("flow-hotkey").value = "";
  document.querySelectorAll("[data-delay]").forEach((input) => { input.value = "0"; });
  document.querySelectorAll("[data-group]").forEach((row) => {
    row.querySelector('[data-field="enabled"]').checked = false;
    row.querySelector('[data-field="preType"]').value = "无";
    row.querySelector('[data-field="preValue"]').value = "";
    row.querySelector('[data-field="farm"]').value = "无";
    row.querySelector('[data-field="wait"]').value = "0";
  });
  setStatus("已清空当前流程字段，保存后写入配置。");
}

function bindLegacyActions() {
  document.querySelectorAll("[data-legacy]").forEach((button) => {
    if (button.dataset.captureBound === "1") return;
    button.dataset.captureBound = "1";
    button.addEventListener("click", () => {
      if (button.classList.contains("point-capture")) {
        captureCursorPoint(button);
        return;
      }
      const container = button.closest(".capture-field, .capture-group, .key-map-row");
      const input = container?.querySelector("input");
      if (input) beginKeyCapture(button, input);
    });
  });
}

function capturedKeyboardKey(event) {
  const namedKeys = {
    " ": "Space",
    ArrowUp: "Up",
    ArrowDown: "Down",
    ArrowLeft: "Left",
    ArrowRight: "Right",
    PageUp: "PgUp",
    PageDown: "PgDn",
  };
  const numpadKeys = {
    NumpadAdd: "NumpadAdd",
    NumpadSubtract: "NumpadSub",
    NumpadMultiply: "NumpadMult",
    NumpadDivide: "NumpadDiv",
    NumpadDecimal: "NumpadDot",
    NumpadEnter: "NumpadEnter",
  };
  if (/^Numpad\d$/.test(event.code)) return event.code;
  if (numpadKeys[event.code]) return numpadKeys[event.code];
  if (namedKeys[event.key]) return namedKeys[event.key];
  if (/^F(?:[1-9]|1[0-2])$/.test(event.key)) return event.key;
  if (event.key.length === 1) return event.key.toUpperCase();
  return event.key;
}

function beginKeyCapture(button, input) {
  if (cancelActiveCapture) cancelActiveCapture();

  const originalText = button.textContent;
  const isHotkey = input.id === "flow-hotkey" || input.id === "stop-hotkey";
  const modifierKeys = new Set(["Control", "Alt", "Shift", "Meta"]);
  const finish = (value, message) => {
    document.removeEventListener("keydown", onKeyDown, true);
    document.removeEventListener("mousedown", onMouseDown, true);
    button.textContent = originalText;
    cancelActiveCapture = null;
    if (value) {
      input.value = value;
      input.dispatchEvent(new Event("input", { bubbles: true }));
    }
    setStatus(message);
  };
  const onKeyDown = (event) => {
    event.preventDefault();
    event.stopPropagation();
    if (event.key === "Escape") {
      finish("", `${button.dataset.legacy} 已取消采集。`);
      return;
    }
    if (modifierKeys.has(event.key)) return;
    const key = capturedKeyboardKey(event);
    if (!key || key === "Unidentified") return;
    const prefix = isHotkey
      ? `${event.ctrlKey ? "^" : ""}${event.altKey ? "!" : ""}${event.shiftKey ? "+" : ""}${event.metaKey ? "#" : ""}`
      : "";
    finish(prefix + key, `已采集 ${button.dataset.legacy}：${prefix + key}。`);
  };
  const onMouseDown = (event) => {
    const mouseKeys = { 1: "MButton", 3: "XButton1", 4: "XButton2" };
    const key = mouseKeys[event.button];
    if (!key) return;
    event.preventDefault();
    event.stopPropagation();
    finish(key, `已采集 ${button.dataset.legacy}：${key}。`);
  };

  cancelActiveCapture = () => finish("", `${button.dataset.legacy} 已取消采集。`);
  button.textContent = "等待...";
  setStatus(`正在采集 ${button.dataset.legacy}，按键或点击中键/侧键，Esc 取消。`);
  window.setTimeout(() => {
    document.addEventListener("keydown", onKeyDown, true);
    document.addEventListener("mousedown", onMouseDown, true);
  }, 0);
}

async function captureCursorPoint(button) {
  if (!window.fireWill) return;
  if (cancelActiveCapture) cancelActiveCapture();

  const row = button.closest("[data-farm]");
  const targetX = row?.querySelector('[data-field="targetX"]');
  const targetY = row?.querySelector('[data-field="targetY"]');
  if (!targetX || !targetY) return;

  const originalText = button.textContent;
  button.disabled = true;
  let remaining = 3;
  button.textContent = `${remaining}秒`;
  setStatus(`${button.dataset.legacy}：3 秒内把鼠标移到游戏里的技能目标点。`);
  const timer = window.setInterval(() => {
    remaining -= 1;
    if (remaining > 0) button.textContent = `${remaining}秒`;
  }, 1000);

  window.setTimeout(async () => {
    window.clearInterval(timer);
    try {
      const point = await window.fireWill.getCursorPosition();
      targetX.value = String(Math.round(point.x));
      targetY.value = String(Math.round(point.y));
      setStatus(`已采集 ${button.dataset.legacy}：${targetX.value}, ${targetY.value}。`);
    } catch (error) {
      setStatus(`采集鼠标点失败：${error.message}`);
    } finally {
      button.disabled = false;
      button.textContent = originalText;
    }
  }, 3000);
}

async function initializeGameSession() {
  if (!window.fireWill) return;
  const result = await window.fireWill.initializeGameSession();
  render(result);
  setStatus("正在绑定游戏窗口并初始化本局。");

  let attempts = 0;
  const poll = async () => {
    attempts += 1;
    const session = await window.fireWill.getGameSession();
    renderGameSession(session);
    if (session.bound || session.ready || attempts >= 20) {
      setStatus(session.message || (session.bound ? "游戏窗口已绑定。" : "初始化未完成，请检查游戏是否已进入地图。"));
      return;
    }
    window.setTimeout(poll, 500);
  };
  window.setTimeout(poll, 500);
}

function openKeymap() {
  renderKeymap();
  $("keymap-dialog").showModal();
}

function renderKeymap() {
  const skills = state.project?.keyMap.skills || [];
  const items = state.project?.keyMap.items || [];
  $("skill-map-grid").innerHTML = skills.map((skill) => (
    `<label class="key-map-row skill-key-map-row">
      <span>${skill.slot}</span>
      <input data-key-slot="${skill.slot}" value="${escapeAttr(skill.key)}" />
      <button type="button" data-legacy="技能${skill.slot}">采</button>
      <input data-cooldown-slot="${skill.slot}" type="number" min="0" max="600" step="0.1" value="${Number(skill.cooldown) || 0}" />
      <span class="cooldown-unit">s</span>
    </label>`
  )).join("");
  $("item-map-grid").innerHTML = items.map((item) => (
    `<label class="key-map-row"><span>${item.slot}</span><input data-item-slot="${item.slot}" value="${escapeAttr(item.key)}" /><button type="button" data-legacy="装备${item.slot}">采</button></label>`
  )).join("");
  const overlay = state.project?.overlay || {};
  $("overlay-enabled").checked = overlay.enabled !== false;
  $("overlay-opacity").value = Number(overlay.opacity) || 92;
  $("overlay-scale").value = Number(overlay.scale) || 100;
  $("overlay-offset-x").value = Number(overlay.offsetX) || 0;
  $("overlay-offset-y").value = Number(overlay.offsetY) || 0;
  bindLegacyActions();
}

async function saveKeymap() {
  const skills = Array.from(document.querySelectorAll("[data-key-slot]")).map((input) => ({
    slot: Number(input.dataset.keySlot),
    key: input.value.trim(),
    cooldown: Number(document.querySelector(`[data-cooldown-slot="${input.dataset.keySlot}"]`)?.value) || 0,
  }));
  const items = Array.from(document.querySelectorAll("[data-item-slot]")).map((input) => ({
    slot: Number(input.dataset.itemSlot),
    key: input.value.trim(),
  }));
  const overlay = {
    enabled: $("overlay-enabled").checked,
    opacity: Number($("overlay-opacity").value) || 92,
    scale: Number($("overlay-scale").value) || 100,
    offsetX: Number($("overlay-offset-x").value) || 0,
    offsetY: Number($("overlay-offset-y").value) || 0,
  };
  const project = await window.fireWill.saveBindings({ skills, items, farms: [], overlay });
  $("keymap-dialog").close();
  render(project);
}

$("flow-select").addEventListener("change", () => {
  syncFlowToState();
  state.selectedFlow = Number($("flow-select").value);
  renderFlow(state.project);
});

$("save-profile").addEventListener("click", saveLayout);
$("clear-farm").addEventListener("click", clearFarmSettings);
$("clear-flow").addEventListener("click", clearCurrentFlow);
$("open-keymap-top").addEventListener("click", openKeymap);
$("save-keymap").addEventListener("click", saveKeymap);
$("stop-flow").addEventListener("click", () => setStatus("停止热键仍由内置 AHK 执行器接管。"));
$("initialize-game").addEventListener("click", initializeGameSession);

$("save-profile-as").addEventListener("click", async () => {
  const profileName = window.prompt("请输入英雄名称", state.project?.profileName || "");
  if (!profileName || !window.fireWill) return;
  const current = await window.fireWill.saveLayout(collectLayout());
  const project = await window.fireWill.saveProfileAs(profileName.trim());
  render(project || current);
});

$("load-profile").addEventListener("click", async () => {
  if (!window.fireWill || !state.project?.profiles.length) return;
  const selected = window.prompt(`请输入要读取的英雄名称：\n${state.project.profiles.join("、")}`, state.project.profileName);
  if (!selected) return;
  const project = await window.fireWill.loadProfile(selected.trim());
  render(project);
});

async function initialize() {
  if (!window.fireWill) {
    setStatus("此界面需要通过 Fire Will 本地客户端打开。");
    return;
  }
  const assets = await window.fireWill.getAssets();
  $("background-video").src = assets.backgroundVideo;
  const project = await window.fireWill.getState();
  render(project);
}

initialize();

window.setInterval(async () => {
  if (!window.fireWill) return;
  try {
    renderGameSession(await window.fireWill.getGameSession());
  } catch {
    // The next poll will refresh the state after a transient IPC failure.
  }
}, 1000);
