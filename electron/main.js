const { app, BrowserWindow, ipcMain } = require("electron");
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

function readState(toast = "") {
  const ini = parseIni(configPath());
  const profileDir = path.join(runtimeRoot(), "profiles");
  const profiles = fs.existsSync(profileDir)
    ? fs.readdirSync(profileDir)
        .filter((name) => name.toLowerCase().endsWith(".ini"))
        .map((name) => path.basename(name, ".ini"))
        .sort((a, b) => a.localeCompare(b, "zh-CN"))
    : [];

  return {
    toast,
    profileName: iniGet(ini, "General", "currentProfileName", "默认/未读取"),
    stopHotkey: iniGet(ini, "General", "stopHotkey", "Z"),
    gameWindowMatcher: iniGet(ini, "General", "gameWindowMatcher"),
    profiles,
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
          cooldown: intValue(iniGet(ini, "SkillCooldown", "skill" + slot), 0),
        };
      }),
      items: Array.from({ length: 6 }, (_, index) => {
        const slot = index + 1;
        return { slot, key: iniGet(ini, "KeyMap", "item" + slot) };
      }),
    },
    flows: Array.from({ length: 8 }, (_, index) => {
      const slot = index + 1;
      return {
        slot,
        name: iniGet(ini, "Flow." + slot, "name", "自定义流程" + slot),
        enabled: iniGet(ini, "Flow." + slot, "enabled", "0") === "1",
        hotkey: iniGet(ini, "Flow." + slot, "hotkey"),
        groups: Array.from({ length: 8 }, (_, groupIndex) => {
          const group = groupIndex + 1;
          const section = "Flow." + slot + ".Group." + group;
          return {
            group,
            enabled: iniGet(ini, section, "enabled", "0") === "1",
            preType: iniGet(ini, section, "preType", "无"),
            preValue: iniGet(ini, section, "preValue"),
            farm: iniGet(ini, section, "farm", "无"),
            wait: intValue(iniGet(ini, section, "wait"), 0),
            duration: intValue(iniGet(ini, section, "duration"), 0),
          };
        }),
      };
    }),
    checks: {
      missingNpc: 0,
      mappedSkills: Array.from({ length: 12 }, (_, index) => iniGet(ini, "KeyMap", "skill" + (index + 1))).filter(Boolean).length,
      mappedItems: Array.from({ length: 6 }, (_, index) => iniGet(ini, "KeyMap", "item" + (index + 1))).filter(Boolean).length,
      enabledFlows: Array.from({ length: 8 }, (_, index) => iniGet(ini, "Flow." + (index + 1), "enabled", "0") === "1").filter(Boolean).length,
    },
  };
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
  const updates = { KeyMap: {}, SkillCooldown: {} };
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

function launchBackend() {
  const executable = path.join(runtimeRoot(), "war3_macro_gui.exe");
  if (!fs.existsSync(executable)) return readState("找不到内置 AHK 执行器。");

  try {
    const child = spawn(executable, [], {
      cwd: runtimeRoot(),
      detached: true,
      stdio: "ignore",
      windowsHide: false,
    });
    child.unref();
    return readState("已启动内置 AHK 执行器。");
  } catch (error) {
    return readState("启动 AHK 执行器失败：" + error.message);
  }
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
  ipcMain.handle("project:save-bindings", (_, payload) => saveBindings(payload));
  ipcMain.handle("project:get-assets", () => ({
    backgroundVideo: pathToFileURL(path.join(uiRoot(), "assets", "background.mp4")).href,
    iconPng: pathToFileURL(path.join(uiRoot(), "assets", "icon.png")).href,
  }));
  ipcMain.handle("backend:launch", () => launchBackend());
  ipcMain.handle("window:set-zoom", (event, action) => {
    const percent = updateZoom(event.sender, action);
    event.sender.send("window:zoom-changed", percent);
    return percent;
  });
  createWindow();
});

app.on("window-all-closed", () => {
  if (process.platform !== "darwin") app.quit();
});
