#!/usr/bin/env node
import { spawn } from "node:child_process";
import { access, mkdir, mkdtemp, readFile, rm, stat } from "node:fs/promises";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

class CdpClient {
  static async connect(url) {
    const socket = new WebSocket(url);
    await new Promise((resolvePromise, rejectPromise) => {
      socket.addEventListener("open", resolvePromise, { once: true });
      socket.addEventListener("error", rejectPromise, { once: true });
    });
    return new CdpClient(socket);
  }

  constructor(socket) {
    this.socket = socket;
    this.nextId = 1;
    this.pending = new Map();
    socket.addEventListener("message", (event) => {
      const message = JSON.parse(typeof event.data === "string" ? event.data : Buffer.from(event.data).toString("utf8"));
      if (message.id && this.pending.has(message.id)) {
        const pending = this.pending.get(message.id);
        this.pending.delete(message.id);
        if (message.error) pending.reject(new Error(`${message.error.code}: ${message.error.message}`));
        else pending.resolve(message.result ?? {});
      }
    });
  }

  send(method, params = {}) {
    const id = this.nextId++;
    return new Promise((resolvePromise, rejectPromise) => {
      this.pending.set(id, { resolve: resolvePromise, reject: rejectPromise });
      this.socket.send(JSON.stringify({ id, method, params }));
    });
  }

  async close() {
    if (this.socket.readyState === WebSocket.CLOSED) return;
    await new Promise((resolvePromise) => {
      const timer = setTimeout(resolvePromise, 1000);
      this.socket.addEventListener("close", () => {
        clearTimeout(timer);
        resolvePromise();
      }, { once: true });
      this.socket.close();
    });
  }
}

const here = dirname(fileURLToPath(import.meta.url));

function argument(name, fallback) {
  const index = process.argv.indexOf(name);
  return index >= 0 && process.argv[index + 1] ? process.argv[index + 1] : fallback;
}

const source = resolve(argument("--source", join(here, "..", "..", "..", "..", "wallpaper_conversion", "sasuke_web_wallpaper", "assets", "background.jpg")));
const output = resolve(argument("--output", join(here, "..", "..", "assets", "backgrounds", "flowing-sasuke.mp4")));
const ffmpeg = resolve(argument("--ffmpeg", "ffmpeg"));
const chrome = resolve(argument("--chrome", "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe"));
const width = Number(argument("--width", "1920"));
const height = Number(argument("--height", "1080"));
const fps = Number(argument("--fps", "30"));
const seconds = Number(argument("--seconds", "15"));
const frames = Math.max(1, Math.round(fps * seconds));

if (!Number.isInteger(width) || !Number.isInteger(height) || width < 16 || height < 16) {
  throw new Error("--width and --height must be positive integers");
}
if (!Number.isInteger(fps) || fps < 1 || fps > 120 || !Number.isFinite(seconds) || seconds < 1) {
  throw new Error("invalid fps/seconds");
}

await mkdir(dirname(output), { recursive: true });
const workRoot = await mkdtemp(join(here, ".render-work-"));
const profileDirectory = join(workRoot, "profile");
await mkdir(profileDirectory, { recursive: true });

let browser;
let cdp;

try {
  browser = spawn(chrome, [
    "--headless=new",
    "--hide-scrollbars",
    "--no-first-run",
    "--no-default-browser-check",
    "--disable-background-networking",
    "--allow-file-access-from-files",
    "--remote-debugging-port=0",
    `--user-data-dir=${profileDirectory}`,
    `--window-size=${width},${height}`,
    "about:blank",
  ], { stdio: ["ignore", "ignore", "ignore"] });

  const portFile = join(profileDirectory, "DevToolsActivePort");
  let port;
  for (let attempt = 0; attempt < 200; attempt += 1) {
    try {
      const lines = (await readFile(portFile, "utf8")).trim().split(/\r?\n/);
      port = Number(lines[0]);
      if (port > 0) break;
    } catch {
      // Chrome writes DevToolsActivePort shortly after startup.
    }
    await delay(50);
  }
  if (!port) throw new Error("Chrome did not expose a DevTools port");

  const targetResponse = await fetch(`http://127.0.0.1:${port}/json/new`, {
    method: "PUT",
    body: "about:blank",
  });
  if (!targetResponse.ok) throw new Error(`Unable to create Chrome target: ${targetResponse.status}`);
  const target = await targetResponse.json();
  cdp = await CdpClient.connect(target.webSocketDebuggerUrl);

  await cdp.send("Page.enable");
  await cdp.send("Runtime.enable");
  await cdp.send("Emulation.setDeviceMetricsOverride", {
    width,
    height,
    deviceScaleFactor: 1,
    mobile: false,
  });
  await cdp.send("Browser.setDownloadBehavior", {
    behavior: "allow",
    downloadPath: workRoot,
    eventsEnabled: true,
  });

  const rendererUrl = `${pathToFileURL(join(here, "renderer.html"))}?source=${encodeURIComponent(pathToFileURL(source).href)}`;
  await cdp.send("Page.navigate", { url: rendererUrl });
  await waitUntilReady(cdp);

  const capturePath = join(workRoot, "canvas-capture.webm");
  process.stdout.write(`Recording Canvas at ${width}x${height} ${fps}fps for ${seconds}s...\n`);
  const recording = await cdp.send("Runtime.evaluate", {
    expression: `window.recordVideo(${seconds * 1000 + 500}, ${seconds * 1000}, ${fps}, "canvas-capture.webm")`,
    returnByValue: true,
    awaitPromise: true,
  });
  if (recording.exceptionDetails) {
    throw new Error(recording.exceptionDetails.exception?.description || "Canvas recording failed");
  }
  await waitForCompletedDownload(capturePath);
  process.stdout.write(`Captured ${recording.result?.value?.byteLength ?? "unknown"} bytes; encoding H.264...\n`);

  await runFfmpeg([
    "-y",
    "-hide_banner",
    "-loglevel", "warning",
    "-i", capturePath,
    "-t", String(seconds),
    "-vf", `fps=${fps},format=yuv420p`,
    "-an",
    "-c:v", "libo264rt",
    "-complexity", "2",
    "-profile:v", "high",
    "-b:v", "10M",
    "-pix_fmt", "yuv420p",
    "-g", String(frames),
    "-movflags", "+faststart",
    output,
  ]);

  process.stdout.write(`Wrote ${output}\n`);
} finally {
  if (cdp) await cdp.close().catch(() => {});
  if (browser && browser.exitCode === null) {
    const exited = new Promise((resolvePromise) => browser.once("exit", resolvePromise));
    browser.kill();
    await Promise.race([exited, delay(3000)]);
  }
  await rm(workRoot, {
    recursive: true,
    force: true,
    maxRetries: 20,
    retryDelay: 100,
  }).catch((error) => {
    process.stderr.write(`Warning: temporary render directory was not removed: ${error.message}\n`);
  });
}

function delay(milliseconds) {
  return new Promise((resolvePromise) => setTimeout(resolvePromise, milliseconds));
}

async function waitUntilReady(client) {
  for (let attempt = 0; attempt < 200; attempt += 1) {
    const result = await client.send("Runtime.evaluate", {
      expression: "document.documentElement.dataset.ready || ''",
      returnByValue: true,
    });
    if (result.result?.value === "1") return;
    if (result.result?.value === "error") throw new Error("Renderer could not load source image");
    await delay(50);
  }
  throw new Error("Renderer did not become ready");
}

async function waitForCompletedDownload(path) {
  let previousSize = -1;
  let stableCount = 0;
  for (let attempt = 0; attempt < 300; attempt += 1) {
    try {
      const current = await stat(path);
      if (current.size > 0 && current.size === previousSize) stableCount += 1;
      else stableCount = 0;
      if (stableCount >= 3) return;
      previousSize = current.size;
    } catch {
      // Download has not been created yet.
    }
    await delay(100);
  }
  throw new Error("Canvas capture download did not finish");
}

async function runFfmpeg(args) {
  await new Promise((resolvePromise, rejectPromise) => {
    const child = spawn(ffmpeg, args, { stdio: ["ignore", "inherit", "inherit"] });
    child.once("error", rejectPromise);
    child.once("exit", (code, signal) => {
      if (code === 0) resolvePromise();
      else rejectPromise(new Error(`FFmpeg failed (code=${code}, signal=${signal ?? "none"})`));
    });
  });
}
