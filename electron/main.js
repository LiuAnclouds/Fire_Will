const { app, BrowserWindow, ipcMain, screen } = require("electron");
const { spawn } = require("node:child_process");
const fs = require("node:fs");
const path = require("node:path");
const { pathToFileURL } = require("node:url");

const farmNames = [
  "妙木山挑战自我x20",
  "妙木山挑战自我x5",
  "家里挑战自我x10",
  "家里挑战自我x5",
  "家里追捕逃忍",
  "去尾兽处",
  "尾兽处追捕逃忍",
];

const releaseTypeOptions = ["无", "技能按键", "装备按键", "技能槽位", "装备槽位"];
const preTypeOptions = ["无", "按键", "公屏"];
let mainWindow = null;
let overlayWindow = null;
let overlaySyncTimer = null;
let lastOverlaySignature = "";

function isPackaged() {
  return app.isPackaged;
}

function uiRoot() {
  return isPackaged()
    ? path.join(process.resourcesPath, "ui")
    : path.resolve(__dirname, "..", "ui");
}

function bundledBackendRoot() {
  return path.join(process.resourcesPath, "backend");
}

function runtimeRoot() {
  return isPackaged()
    ? path.join(app.getPath("userData"), "backend")
    : path.join(__dirname, "backend");
}

function ensureRuntimeFiles() {
  if (!isPackaged()) return;

  const source = bundledBackendRoot();
  const target = runtimeRoot();
  fs.mkdirSync(target, { recursive: true });

  for (const name of ["war3_macro_gui.exe", "icon.ico"]) {
    fs.copyFileSync(path.join(source, name), path.join(target, name));
  }

  const config = path.join(target, "war3_macro_gui.ini");
  if (!fs.existsSync(config)) {
    fs.copyFileSync(path.join(source, "war3_macro_gui.ini"), config);
  }

  const sourceProfiles = path.join(source, "profiles");
  const targetProfiles = path.join(target, "profiles");
  fs.mkdirSync(targetProfiles, { recursive: true });
  for (const name of fs.readdirSync(sourceProfiles)) {
    const targetProfile = path.join(targetProfiles, name);
    if (!fs.existsSync(targetProfile)) {
      fs.copyFileSync(path.join(sourceProfiles, name), targetProfile);
    }
  }
}

function configPath() {
  return path.join(runtimeRoot(), "war3_macro_gui.ini");
}

function sessionPath() {
  return path.join(runtimeRoot(), "war3_session.ini");
}

function cooldownPath() {
  return path.join(runtimeRoot(), "war3_cooldown.ini");
}

function parseIni(filePath) {
  const sections = {};
  let section = "";
  if (!fs.existsSync(filePath)) return sections;

  for (const raw of fs.readFileSync(filePath, "utf8").split(/\r?\n/)) {
    const line = raw.trim();
    if (!line || line.startsWith(";") || line.startsWith("#")) continue;
    if (line.startsWith("[") && line.endsWith("]")) {
      section = line.slice(1, -1).trim();
      sections[section] ||= {};
      continue;
    }
    const index = line.indexOf("=");
    if (index < 0) continue;
    sections[section] ||= {};
    sections[section][line.slice(0, index).trim()] = line.slice(index + 1).trim();
  }
  return sections;
}

function iniGet(ini, section, key, fallback = "") {
  return ini[section]?.[key] ?? fallback;
}

function intValue(value, fallback = 0) {
  const parsed = Number.parseInt(value, 10);
  return Number.isFinite(parsed) ? parsed : fallback;
}

function readGameSession() {
  const ini = parseIni(sessionPath());
  const session = ini.Session || {};
  return {
    bound: session.bound === "1",
    ready: session.ready === "1",
    state: session.state || "未初始化",
    message: session.message || "请先绑定并初始化游戏窗口。",
    projectionReady: session.projectionReady === "1",
    active: session.active === "1",
    left: intValue(session.clientLeft, 0),
    top: intValue(session.clientTop, 0),
    width: intValue(session.clientWidth, 0),
    height: intValue(session.clientHeight, 0),
  };
}

function readOverlaySettings(ini) {
  return {
    enabled: iniGet(ini, "Overlay", "enabled", "1") === "1",
    opacity: Math.max(30, Math.min(100, intValue(iniGet(ini, "Overlay", "opacity"), 92))),
    scale: Math.max(70, Math.min(140, intValue(iniGet(ini, "Overlay", "scale"), 100))),
    offsetX: Math.max(-500, Math.min(500, intValue(iniGet(ini, "Overlay", "offsetX"), 0))),
    offsetY: Math.max(-500, Math.min(500, intValue(iniGet(ini, "Overlay", "offsetY"), 0))),
  };
}

function readCooldownState() {
  const ini = parseIni(cooldownPath());
  const cooldown = ini.Cooldown || {};
  return Array.from({ length: 12 }, (_, index) => {
    const slot = index + 1;
    return {
      slot,
      endAt: Number(cooldown["skill" + slot + "End"] || 0),
      duration: Number(cooldown["skill" + slot + "Duration"] || 0),
    };
  });
}

function readState(toast = "") {
  const ini = parseIni(configPath());
  const profileDir = path.join(runtimeRoot(), "profiles");
  const profiles = fs.existsSync(profileDir)
    ? fs.readdirSync(profileDir)
        .filter((name) => name.toLowerCase().endsWith(".ini"))
        .map((name) => path.basename(name, ".ini"))
        .sort((a, b) => a.localeCompare(b, "zh-CN"))
    : [];

  const flows = Array.from({ length: 8 }, (_, index) => {
    const slot = index + 1;
    const section = "Flow." + slot;
    const generalDelay = (key, fallback) => intValue(iniGet(ini, "General", key), fallback);
    return {
      slot,
      name: iniGet(ini, section, "name", "自定义流程" + slot),
      enabled: iniGet(ini, section, "enabled", "0") === "1",
      hotkey: iniGet(ini, section, "hotkey"),
      delays: {
        key: intValue(iniGet(ini, section, "keyDelay"), generalDelay("keyDelayMs", 40)),
        skillKey: intValue(iniGet(ini, section, "skillKeyDelay"), generalDelay("skillKeyDelayMs", 100)),
        teleport: intValue(iniGet(ini, section, "teleportKeyDelay"), generalDelay("teleportKeyDelayMs", 200)),
        npcClick: intValue(iniGet(ini, section, "npcClickDelay"), generalDelay("npcClickDelayMs", 100)),
        mouse: intValue(iniGet(ini, section, "mouseMoveDelay"), generalDelay("mouseMoveDelayMs", 30)),
        releaseMouse: intValue(iniGet(ini, section, "releaseMouseMoveDelay"), generalDelay("releaseMouseMoveDelayMs", 80)),
        chat: intValue(iniGet(ini, section, "chatDelay"), generalDelay("chatDelayMs", 500)),
        heroSelect: intValue(
          iniGet(ini, section, "heroSelectDelay", iniGet(ini, section, "f1Delay")),
          generalDelay("heroSelectDelayMs", 80),
        ),
      },
      groups: Array.from({ length: 8 }, (_, groupIndex) => {
        const group = groupIndex + 1;
        const groupSection = section + ".Group." + group;
        const wait = intValue(iniGet(ini, groupSection, "wait"), 0);
        const duration = intValue(iniGet(ini, groupSection, "duration"), 0);
        return {
          group,
          enabled: iniGet(ini, groupSection, "enabled", "0") === "1",
          preType: iniGet(ini, groupSection, "preType", "无"),
          preValue: iniGet(ini, groupSection, "preValue"),
          farm: iniGet(ini, groupSection, "farm", "无"),
          wait,
          duration,
          used: Math.max(0, duration - wait),
        };
      }),
    };
  });

  return {
    toast,
    gameSession: readGameSession(),
    profileName: iniGet(ini, "General", "currentProfileName", "默认/未读取"),
    stopHotkey: iniGet(ini, "General", "stopHotkey", "Z"),
    overlay: readOverlaySettings(ini),
    profiles,
    options: { farmNames, releaseTypeOptions, preTypeOptions },
    farms: farmNames.map((name) => ({
      name,
      actionKey: iniGet(ini, "Farm." + name, "actionKey"),
      releaseType: iniGet(ini, "Farm." + name, "releaseType", "无"),
      releaseKey: iniGet(ini, "Farm." + name, "releaseKey"),
      targetX: iniGet(ini, "Farm." + name, "targetX"),
      targetY: iniGet(ini, "Farm." + name, "targetY"),
    })),
    keyMap: {
      skills: Array.from({ length: 12 }, (_, index) => {
        const slot = index + 1;
        return {
          slot,
          key: iniGet(ini, "KeyMap", "skill" + slot),
          cooldown: Number(iniGet(ini, "SkillCooldown", "skill" + slot, "0")) || 0,
        };
      }),
      items: Array.from({ length: 6 }, (_, index) => {
        const slot = index + 1;
        return { slot, key: iniGet(ini, "KeyMap", "item" + slot) };
      }),
    },
    flows,
    checks: {
      mappedSkills: Array.from({ length: 12 }, (_, index) => iniGet(ini, "KeyMap", "skill" + (index + 1))).filter(Boolean).length,
      mappedItems: Array.from({ length: 6 }, (_, index) => iniGet(ini, "KeyMap", "item" + (index + 1))).filter(Boolean).length,
      enabledFlows: Array.from({ length: 8 }, (_, index) => iniGet(ini, "Flow." + (index + 1), "enabled", "0") === "1").filter(Boolean).length,
    },
  };
}

function saveLayout(payload) {
  const updates = {
    General: {
      stopHotkey: String(payload.stopHotkey || "Z"),
    },
  };

  for (const farm of payload.farms || []) {
    if (!farmNames.includes(farm.name)) continue;
    updates["Farm." + farm.name] = {
      actionKey: String(farm.actionKey || ""),
      releaseType: releaseTypeOptions.includes(farm.releaseType) ? farm.releaseType : "无",
      releaseKey: String(farm.releaseKey || ""),
      targetX: String(farm.targetX || ""),
      targetY: String(farm.targetY || ""),
    };
  }

  for (const flow of payload.flows || []) {
    const slot = Number(flow.slot);
    if (slot < 1 || slot > 8) continue;
    const section = "Flow." + slot;
    updates[section] = {
      name: String(flow.name || "自定义流程" + slot),
      enabled: flow.enabled ? "1" : "0",
      hotkey: String(flow.hotkey || ""),
      keyDelay: String(Math.max(0, Number(flow.delays?.key) || 0)),
      skillKeyDelay: String(Math.max(0, Number(flow.delays?.skillKey) || 0)),
      teleportKeyDelay: String(Math.max(0, Number(flow.delays?.teleport) || 0)),
      npcClickDelay: String(Math.max(0, Number(flow.delays?.npcClick) || 0)),
      mouseMoveDelay: String(Math.max(0, Number(flow.delays?.mouse) || 0)),
      releaseMouseMoveDelay: String(Math.max(0, Number(flow.delays?.releaseMouse) || 0)),
      chatDelay: String(Math.max(0, Number(flow.delays?.chat) || 0)),
      heroSelectDelay: String(Math.max(0, Number(flow.delays?.heroSelect) || 0)),
    };
    for (const group of flow.groups || []) {
      const groupIndex = Number(group.group);
      if (groupIndex < 1 || groupIndex > 8) continue;
      updates[section + ".Group." + groupIndex] = {
        enabled: group.enabled ? "1" : "0",
        preType: preTypeOptions.includes(group.preType) ? group.preType : "无",
        preValue: String(group.preValue || ""),
        farm: farmNames.includes(group.farm) ? group.farm : "无",
        wait: String(Math.max(0, Number(group.wait) || 0)),
        duration: String(Math.max(0, Number(group.duration) || 0)),
      };
    }
  }

  updateIni(configPath(), updates);
  return readState("已按旧版 AHK 字段保存当前面板配置。");
}

function safeProfileName(value) {
  return String(value || "")
    .replace(/[\\/:*?"<>|]/g, "_")
    .trim()
    .slice(0, 80);
}

function saveProfileAs(profileName) {
  const safeName = safeProfileName(profileName);
  if (!safeName) return readState("英雄名称不能为空。");
  const profileDir = path.join(runtimeRoot(), "profiles");
  fs.mkdirSync(profileDir, { recursive: true });
  const target = path.join(profileDir, safeName + ".ini");
  updateIni(configPath(), {
    General: {
      currentProfileName: safeName,
      currentProfilePath: target,
    },
  });
  fs.copyFileSync(configPath(), target);
  return readState("已保存新英雄配置：" + safeName);
}

function loadProfile(profileName) {
  const safeName = safeProfileName(profileName);
  const target = path.join(runtimeRoot(), "profiles", safeName + ".ini");
  if (!safeName || !fs.existsSync(target)) return readState("找不到英雄配置：" + profileName);
  fs.copyFileSync(target, configPath());
  updateIni(configPath(), {
    General: {
      currentProfileName: safeName,
      currentProfilePath: target,
    },
  });
  return readState("已读取英雄配置：" + safeName);
}

function updateIni(filePath, updates) {
  const lines = fs.existsSync(filePath) ? fs.readFileSync(filePath, "utf8").split(/\r?\n/) : [];
  const seen = new Set();
  let section = "";
  const output = lines.map((raw) => {
    const trimmed = raw.trim();
    if (trimmed.startsWith("[") && trimmed.endsWith("]")) {
      section = trimmed.slice(1, -1).trim();
      return raw;
    }
    const equals = raw.indexOf("=");
    if (equals < 0) return raw;
    const key = raw.slice(0, equals).trim();
    if (updates[section]?.[key] === undefined) return raw;
    seen.add(section + "::" + key);
    return key + "=" + updates[section][key];
  });

  for (const [sectionName, values] of Object.entries(updates)) {
    const missing = Object.entries(values).filter(([key]) => !seen.has(sectionName + "::" + key));
    if (!missing.length) continue;
    output.push("", "[" + sectionName + "]", ...missing.map(([key, value]) => key + "=" + value));
  }
  fs.writeFileSync(filePath, output.join("\r\n"), "utf8");
}

function saveBindings(payload) {
  const overlay = payload.overlay || {};
  const updates = {
    KeyMap: {},
    SkillCooldown: {},
    Overlay: {
      enabled: overlay.enabled === false ? "0" : "1",
      opacity: String(Math.max(30, Math.min(100, Number(overlay.opacity) || 92))),
      scale: String(Math.max(70, Math.min(140, Number(overlay.scale) || 100))),
      offsetX: String(Math.max(-500, Math.min(500, Number(overlay.offsetX) || 0))),
      offsetY: String(Math.max(-500, Math.min(500, Number(overlay.offsetY) || 0))),
    },
  };
  for (const skill of payload.skills || []) {
    if (skill.slot < 1 || skill.slot > 12) continue;
    updates.KeyMap["skill" + skill.slot] = String(skill.key || "");
    updates.SkillCooldown["skill" + skill.slot] = String(Math.max(0, Math.min(600, Number(skill.cooldown) || 0)));
  }
  for (const item of payload.items || []) {
    if (item.slot < 1 || item.slot > 6) continue;
    updates.KeyMap["item" + item.slot] = String(item.key || "");
  }
  for (const farm of payload.farms || []) {
    if (!farmNames.includes(farm.name)) continue;
    updates["Farm." + farm.name] = {
      actionKey: String(farm.actionKey || ""),
      releaseKey: String(farm.releaseKey || ""),
    };
  }
  updateIni(configPath(), updates);
  return readState("已保存用户快捷键和技能 CD 设置。");
}

function launchBackend(options = {}) {
  const executable = path.join(runtimeRoot(), "war3_macro_gui.exe");
  if (!fs.existsSync(executable)) return readState("找不到内置 AHK 执行器。");

  try {
    const args = options.initialize
      ? ["--initialize"]
      : options.background
        ? ["--background"]
        : [];
    let child;
    if (options.initialize || options.elevated) {
      // Memory/session initialization needs the same elevation as the game.
      const fileArg = "'" + executable.replace(/'/g, "''") + "'";
      const argumentList = args.length
        ? " -ArgumentList @(" + args.map((arg) => "'" + arg + "'").join(",") + ")"
        : "";
      const command = "Start-Process -FilePath " + fileArg + " -Verb RunAs" + argumentList + " -WindowStyle Hidden";
      child = spawn("powershell.exe", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", command], {
        cwd: runtimeRoot(),
        detached: true,
        stdio: "ignore",
        windowsHide: true,
      });
    } else {
      child = spawn(executable, args, {
        cwd: runtimeRoot(),
        detached: true,
        stdio: "ignore",
        windowsHide: false,
      });
    }
    child.unref();
    return readState(options.initialize
      ? "已请求管理员权限，正在绑定并初始化游戏窗口。"
      : options.background
        ? "F9 初始化热键执行器已启动。"
        : "已启动内置 AHK 执行器。");
  } catch (error) {
    return readState("启动 AHK 执行器失败：" + error.message);
  }
}

function physicalClientToDipBounds(session) {
  const displays = screen.getAllDisplays();
  const display = displays.find((candidate) => {
    const factor = candidate.scaleFactor || 1;
    const left = candidate.bounds.x * factor;
    const top = candidate.bounds.y * factor;
    return session.left >= left
      && session.left < left + candidate.bounds.width * factor
      && session.top >= top
      && session.top < top + candidate.bounds.height * factor;
  }) || screen.getPrimaryDisplay();
  const factor = display.scaleFactor || 1;
  return {
    x: Math.round(display.bounds.x + (session.left - display.bounds.x * factor) / factor),
    y: Math.round(display.bounds.y + (session.top - display.bounds.y * factor) / factor),
    width: Math.max(1, Math.round(session.width / factor)),
    height: Math.max(1, Math.round(session.height / factor)),
  };
}

function readOverlayPayload() {
  const ini = parseIni(configPath());
  const cooldowns = readCooldownState();
  return {
    settings: readOverlaySettings(ini),
    profileName: iniGet(ini, "General", "currentProfileName", ""),
    skills: cooldowns.map((cooldown) => ({
      ...cooldown,
      key: iniGet(ini, "KeyMap", "skill" + cooldown.slot),
      configuredDuration: Number(iniGet(ini, "SkillCooldown", "skill" + cooldown.slot, "0")) || 0,
    })),
  };
}

function createOverlayWindow() {
  overlayWindow = new BrowserWindow({
    show: false,
    frame: false,
    transparent: true,
    focusable: false,
    resizable: false,
    movable: false,
    minimizable: false,
    maximizable: false,
    fullscreenable: false,
    skipTaskbar: true,
    hasShadow: false,
    backgroundColor: "#00000000",
    webPreferences: {
      preload: path.join(__dirname, "overlay-preload.js"),
      contextIsolation: true,
      nodeIntegration: false,
      backgroundThrottling: false,
    },
  });
  overlayWindow.setIgnoreMouseEvents(true, { forward: true });
  overlayWindow.setAlwaysOnTop(true, "screen-saver", 1);
  overlayWindow.loadFile(path.join(uiRoot(), "overlay.html"));
  if (process.env.FIREWILL_OVERLAY_SCREENSHOT) {
    overlayWindow.webContents.once("did-finish-load", () => {
      setTimeout(async () => {
        syncOverlayWindow();
        const image = await overlayWindow.webContents.capturePage();
        fs.writeFileSync(process.env.FIREWILL_OVERLAY_SCREENSHOT, image.toPNG());
        app.quit();
      }, 900);
    });
  }
  overlayWindow.on("closed", () => { overlayWindow = null; });
}

function syncOverlayWindow() {
  if (!overlayWindow || overlayWindow.isDestroyed()) return;
  const session = readGameSession();
  const payload = readOverlayPayload();
  const preview = Boolean(process.env.FIREWILL_OVERLAY_PREVIEW);

  if (preview) {
    payload.preview = true;
    const now = Date.now();
    payload.skills = payload.skills.map((skill, index) => ({
      ...skill,
      key: skill.key || ["Q", "W", "E", "R"][index % 4],
      configuredDuration: skill.configuredDuration || 12 + index * 3,
      duration: skill.configuredDuration || 12 + index * 3,
      endAt: index < 7 ? now + (2.8 + index * 1.9) * 1000 : 0,
    }));
  }

  const shouldShow = preview || (
    payload.settings.enabled
    && session.bound
    && session.active
    && session.width > 0
    && session.height > 0
  );
  if (!shouldShow) {
    if (overlayWindow.isVisible()) overlayWindow.hide();
    return;
  }

  const bounds = preview
    ? { x: 80, y: 80, width: 1280, height: 720 }
    : physicalClientToDipBounds(session);
  const currentBounds = overlayWindow.getBounds();
  if (JSON.stringify(currentBounds) !== JSON.stringify(bounds)) {
    overlayWindow.setBounds(bounds, false);
  }
  const signature = JSON.stringify(payload);
  if (signature !== lastOverlaySignature) {
    lastOverlaySignature = signature;
    overlayWindow.webContents.send("overlay:state", payload);
  }
  if (!overlayWindow.isVisible()) overlayWindow.showInactive();
}

function createWindow() {
  const window = new BrowserWindow({
    width: 1500,
    height: 940,
    minWidth: 1120,
    minHeight: 720,
    backgroundColor: "#080b0f",
    icon: path.join(uiRoot(), "assets", "icon.ico"),
    webPreferences: {
      preload: path.join(__dirname, "preload.js"),
      contextIsolation: true,
      nodeIntegration: false,
    },
  });
  mainWindow = window;
  window.on("closed", () => {
    mainWindow = null;
    if (overlayWindow && !overlayWindow.isDestroyed()) overlayWindow.close();
  });
  window.webContents.on("before-input-event", (event, input) => {
    if (!input.control || input.type !== "keyDown") return;

    const action = input.key === "0"
      ? "reset"
      : input.key === "+" || input.key === "="
        ? "in"
        : input.key === "-"
          ? "out"
          : "";
    if (!action) return;

    event.preventDefault();
    const percent = updateZoom(window.webContents, action);
    window.webContents.send("window:zoom-changed", percent);
  });
  window.loadFile(path.join(uiRoot(), "index.html"));
  if (process.env.FIREWILL_SCREENSHOT) {
    window.webContents.once("did-finish-load", () => {
      setTimeout(async () => {
        const image = await window.webContents.capturePage();
        fs.writeFileSync(process.env.FIREWILL_SCREENSHOT, image.toPNG());
        app.quit();
      }, 1800);
    });
  }
}

function updateZoom(webContents, action) {
  const current = webContents.getZoomFactor();
  const requested = action === "reset"
    ? 1
    : current + (action === "in" ? 0.1 : -0.1);
  const next = Math.max(0.75, Math.min(1.5, Math.round(requested * 10) / 10));
  webContents.setZoomFactor(next);
  return Math.round(next * 100);
}

app.whenReady().then(() => {
  ensureRuntimeFiles();
  ipcMain.handle("project:get-state", () => readState());
  ipcMain.handle("project:save-layout", (_, payload) => saveLayout(payload));
  ipcMain.handle("project:save-profile-as", (_, profileName) => saveProfileAs(profileName));
  ipcMain.handle("project:load-profile", (_, profileName) => loadProfile(profileName));
  ipcMain.handle("project:save-bindings", (_, payload) => saveBindings(payload));
  ipcMain.handle("project:get-assets", () => ({
    backgroundVideo: pathToFileURL(path.join(uiRoot(), "assets", "background.mp4")).href,
    iconPng: pathToFileURL(path.join(uiRoot(), "assets", "icon.png")).href,
  }));
  ipcMain.handle("game:initialize", () => launchBackend({ initialize: true }));
  ipcMain.handle("game:get-session", () => readGameSession());
  ipcMain.handle("input:get-cursor-position", () => screen.getCursorScreenPoint());
  ipcMain.handle("window:set-zoom", (event, action) => {
    const percent = updateZoom(event.sender, action);
    event.sender.send("window:zoom-changed", percent);
    return percent;
  });
  createWindow();
  createOverlayWindow();
  overlaySyncTimer = setInterval(syncOverlayWindow, 100);
  if (!process.env.FIREWILL_SCREENSHOT && !process.env.FIREWILL_OVERLAY_PREVIEW) {
    setTimeout(() => launchBackend({ background: true, elevated: true }), 500);
  }
});

app.on("window-all-closed", () => {
  if (overlaySyncTimer) clearInterval(overlaySyncTimer);
  if (process.platform !== "darwin") app.quit();
});
