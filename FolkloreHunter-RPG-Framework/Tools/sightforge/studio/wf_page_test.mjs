// Drives Studio's WorldForge page in a real browser (Edge, headless, through its DevTools port; Node 22+ has WebSocket):
// starts Studio on a spare port, opens #worldforge, changes a value and saves it (then puts it back), switches to the
// JSON tab, optionally starts Build & test from the page and waits for it, then opens the last test. Screenshots and
// a log go to the folder given (default: %TEMP%\wf_page_test).
//   node wf_page_test.mjs [outDir] [--run]
import { spawn } from "node:child_process";
import fs from "node:fs";
import path from "node:path";

const out = process.argv[2] && !process.argv[2].startsWith("--") ? process.argv[2] : path.join(process.env.TEMP || ".", "wf_page_test");
const RUN = process.argv.includes("--run");
fs.mkdirSync(out, { recursive: true });
const log = (...a) => { const s = new Date().toTimeString().slice(0, 8) + " " + a.join(" "); console.log(s); fs.appendFileSync(path.join(out, "log.txt"), s + "\n"); };
const sleep = ms => new Promise(r => setTimeout(r, ms));
const app = path.join(process.env.LOCALAPPDATA, "ClaudeEyesStudio", "app");
const edge = ["C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe", "C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe"].find(p => fs.existsSync(p));

const studio = spawn("python", ["studio.py", "--port", "8795"], { cwd: app });
const url = await new Promise((ok, bad) => { let buf = ""; studio.stdout.on("data", d => { buf += d; const m = buf.match(/(http:\/\/127\.0\.0\.1:\d+\/\?t=\S+)/); if (m) ok(m[1]); });
  studio.stderr.on("data", d => fs.appendFileSync(path.join(out, "studio_err.txt"), d)); setTimeout(() => bad(new Error("Studio did not start")), 20000); });
log("studio up");
const prof = path.join(out, "edge_profile");
const browser = spawn(edge, ["--headless=new", "--remote-debugging-port=9333", "--user-data-dir=" + prof, "--window-size=1440,1100", "--no-first-run", "--autoplay-policy=no-user-gesture-required", "about:blank"]);
let target;
for (let i = 0; i < 40 && !target; i++) { await sleep(500); try { target = (await (await fetch("http://127.0.0.1:9333/json/list")).json()).find(t => t.type === "page"); } catch (e) {} }
const ws = new WebSocket(target.webSocketDebuggerUrl);
await new Promise(r => ws.addEventListener("open", r, { once: true }));
let seq = 0; const waiting = new Map(); const consoleErrors = [];
ws.addEventListener("message", e => { const m = JSON.parse(e.data); if (m.id && waiting.has(m.id)) { waiting.get(m.id)(m); waiting.delete(m.id); }
  if (m.method === "Runtime.exceptionThrown") consoleErrors.push(m.params.exceptionDetails.exception?.description || m.params.exceptionDetails.text);
  if (m.method === "Runtime.consoleAPICalled" && m.params.type === "error") consoleErrors.push(m.params.args.map(a => a.value ?? a.description).join(" ")); });
const cdp = (method, params = {}) => new Promise(ok => { const id = ++seq; waiting.set(id, ok); ws.send(JSON.stringify({ id, method, params })); });
const js = async expr => { const r = await cdp("Runtime.evaluate", { expression: expr, awaitPromise: true, returnByValue: true }); if (r.result?.exceptionDetails) throw new Error(expr.slice(0, 80) + ": " + JSON.stringify(r.result.exceptionDetails).slice(0, 400)); return r.result?.result?.value; };
// the whole page at a desktop size (the main column scrolls, so it is measured, not the window)
const shot = async name => { const h = Math.min(7000, Math.max(1000, await js("Math.max(document.getElementById('main').scrollHeight, document.body.scrollHeight)").catch(() => 1000)));
  await cdp("Emulation.setDeviceMetricsOverride", { width: 1440, height: h, deviceScaleFactor: 1, mobile: false }); await sleep(400);
  const r = await cdp("Page.captureScreenshot", { format: "png" }); fs.writeFileSync(path.join(out, name + ".png"), Buffer.from(r.result.data, "base64"));
  await cdp("Emulation.setDeviceMetricsOverride", { width: 1440, height: 1000, deviceScaleFactor: 1, mobile: false }); log("screenshot", name); };
const until = async (expr, ms = 15000) => { const t0 = Date.now(); while (Date.now() - t0 < ms) { if (await js(expr).catch(() => false)) return true; await sleep(250); } throw new Error("timed out: " + expr); };
const report = { checks: [] }; const check = (name, ok, detail) => { report.checks.push({ name, ok: !!ok, detail }); log((ok ? "PASS " : "FAIL ") + name + (detail ? ": " + detail : "")); };

try {
  await cdp("Runtime.enable"); await cdp("Page.enable");
  await cdp("Emulation.setDeviceMetricsOverride", { width: 1440, height: 1000, deviceScaleFactor: 1, mobile: false });
  await cdp("Page.navigate", { url: url + "#worldforge" });
  await until("typeof PRJ !== 'undefined' && PRJ !== null && typeof R !== 'undefined'", 20000);
  // the game project (the one with a WorldForge folder), whatever Studio opened first
  const prj = await js("(async () => { const ps = await api('/api/projects'); const p = ps.find(p => /FolkloreHunter-RPG-Framework$/i.test(p.root)); return p ? p.id : null; })()");
  check("the game project is in Studio's list", prj, prj);
  await js(`(() => { PRJ = ${JSON.stringify(prj)}; const sel = document.querySelector('header select, #project'); if (sel) sel.value = PRJ; const t = document.getElementById('tour'); if (t) t.style.display = 'none'; go('worldforge'); return 1; })()`);
  await until("!!document.querySelector('.wf-recipes') || !!document.querySelector('#main .notice')");
  check("the menu has WorldForge under Tools", await js("!!document.querySelector('#nav a[data-v=worldforge]')"));
  check("the page lists the recipes", await js("document.querySelectorAll('tr[data-wfr]').length"), await js("Array.from(document.querySelectorAll('tr[data-wfr] b')).map(b => b.textContent).join(', ')"));
  await shot("1_values");
  const nInputs = await js("document.querySelectorAll('[data-wfp]').length");
  check("the values form shows the recipe's numbers", nInputs > 100, nInputs + " fields");
  // change the seed, save, read it back, put it back
  const seed0 = await js("WFS.recipe.seed");
  await js("(() => { const i = document.querySelector('[data-wfp=seed]'); i.value = String(WFS.recipe.seed + 1); i.dispatchEvent(new Event('input', {bubbles: true})); return 1; })()");
  check("an edit is marked not saved", await js("!!document.querySelector('.panel h3 .pill')"));
  await js("document.getElementById('wfSave').click()");
  await until(`(async () => (await api('/api/wf/recipe?name=' + enc(WFS.name))).recipe.seed === ${seed0 + 1})()`);
  check("Save wrote the changed value to the recipe file", true, "seed " + seed0 + " -> " + (seed0 + 1));
  await until("!!document.querySelector('[data-wfp=seed]')");
  await js(`(() => { const i = document.querySelector('[data-wfp=seed]'); i.value = '${seed0}'; i.dispatchEvent(new Event('input', {bubbles: true})); document.getElementById('wfSave').click(); return 1; })()`);
  await until(`(async () => (await api('/api/wf/recipe?name=' + enc(WFS.name))).recipe.seed === ${seed0})()`);
  check("and the original value went back", true);
  // a bad value is refused by the page, a bad recipe by the server
  await until("!!document.querySelector('[data-wfp=voxel]')");
  const refused = await js("(async () => { try { await api('/api/wf/save', {name: WFS.name, recipe: Object.assign({}, WFS.recipe, {voxel: 0.01})}); return false; } catch (e) { return e.message; } })()");
  check("a voxel too small is refused by Studio", refused, refused);
  // JSON tab
  await js("document.querySelector('[data-wft=json]').click()");
  await until("!!document.getElementById('wfJson')");
  check("the JSON tab holds the recipe", await js("JSON.parse(document.getElementById('wfJson').value).name === WFS.name"));
  await shot("2_json");
  await js("document.querySelector('[data-wft=values]').click()"); await until("!!document.querySelector('[data-wfp=seed]')");
  // a feature opened, with its points
  await js("(() => { document.querySelectorAll('details.wf-item').forEach(d => d.open = true); return 1; })()");
  await shot("3_values_open");
  if (RUN) {
    await js("document.getElementById('wfRun').click()");
    await until("!!document.querySelector('#wfJob .spin')", 20000);
    check("Build & test started from the page", true);
    await sleep(60000); await shot("4_running");
    const t0 = Date.now();
    while (Date.now() - t0 < 40 * 60000) { await sleep(15000); const st = await js("(async () => (await api('/api/wf/state')).job)()"); if (st && st.status !== "running") { log("job:", st.status, st.seconds + " s"); check("the job finished", st.status === "done", st.status); break; } }
  }
  await js("WFS.tab = 'results'; route(); 1");
  await until("!!document.querySelector('.wf-pre') || /not been tested/.test(document.getElementById('main').textContent)", 20000);
  await sleep(2500);
  check("the last test shows its video", await js("!!document.querySelector('video')"));
  const vid = await js("(async () => { const v = document.querySelector('video'); if (!v) return 'none'; for (let i = 0; i < 40 && v.readyState < 1; i++) await new Promise(r => setTimeout(r, 250)); return v.readyState + ' ' + v.duration.toFixed(1) + ' s ' + v.videoWidth + 'x' + v.videoHeight; })()");
  check("the video loads in the page", /^\d+ \d/.test(vid) && !/^0 /.test(vid), vid);
  await js("(() => { document.querySelectorAll('img[loading=lazy]').forEach(i => i.loading = 'eager'); return 1; })()"); await sleep(2500);
  check("the viewpoints show", await js("Array.from(document.querySelectorAll('.wf-shots img')).filter(i => i.complete && i.naturalWidth > 0).length"));
  await shot("5_results");
} catch (e) { check("the run went through", false, e.message); }
check("no script errors in the page", consoleErrors.length === 0, consoleErrors.slice(0, 5).join(" | "));
fs.writeFileSync(path.join(out, "result.json"), JSON.stringify(report, null, 1));
ws.close(); browser.kill(); studio.kill();
log(report.checks.every(c => c.ok) ? "ALL PASSED" : "SOME FAILED");
process.exit(0);
