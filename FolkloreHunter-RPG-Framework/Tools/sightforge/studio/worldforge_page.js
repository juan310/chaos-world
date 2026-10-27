// ---- WorldForge (0.19): environments from an editable recipe, built and tested in the sandbox
const WFS = {name: null, recipe: null, tab: "values", timer: null, dirty: false};
const WF_HELP = {
  seed: "changes every random shape at once (same seed, same world)", voxel: "mesh detail: one sample cell in metres (smaller is finer and slower)",
  chunk: "the world is cut into chunks this size (lighting, culling, collision)", min: "one corner of the generation box", max: "the other corner of the generation box",
  quality: "lights and effects preset the preview starts with (F1 in the preview)", hills: "rolling hills: height (m)", hillScale: "size of the hills (m)", detail: "small bumps (m)",
  height: "how tall (m)", width: "how wide (m)", depth: "how deep (m)", radius: "radius (m)", length: "length (m)", curvature: "extra winding added to the drawn path (m, sideways)",
  roughness: "how far the surface is pushed in and out by noise (m)", noiseScale: "the size of those bumps (m)", strata: "rock layering (m)", blend: "how softly it merges with what is around it (m)",
  floor: "unevenness of a walkable floor (m)", flare: "tunnels: how much wider the mouth is (0 = none)", spacing: "how far apart (m)", count: "how many",
  material: "rock, dirt, moss or ruin (empty: by type)", points: "the path drawn through these points (or a centre); +Z is the way in",
  feature: "which feature they follow", offset: "the first one this far along (m)", mount: "height above the floor (m)", intensity: "brightness", range: "how far the light reaches (m)",
  flicker: "how much the flame flickers", level: "water height", ripples: "ripple strength", time: "day, dusk or night (F2 in the preview)", sunAngles: "sun direction (degrees)",
  fog: "fog density", mist: "low mist in caves and hollows (0 to 1)", dust: "dust in light shafts (0 to 1)", rain: "outdoor rain (0 to 1; F3 in the preview)", drips: "dripping water in caves (0 to 1)",
  lightShafts: "light falling through cave mouths and openings", wetness: "everything a little damp (0 to 1)", tiling: "texture size on the surface (m)",
  route: "features walked in order by the test (their points)", arena: "the chamber checked for a boss fight", bossRadius: "boss size for the navigation check (m)", bossHeight: "boss height (m)"};
const WF_CHOICES = {
  "features.*.type": ["hills", "mountain", "cliff", "rockwall", "ledge", "arch", "ruin", "boulders", "path", "tunnel", "cave", "chamber", "canyon", "ravine", "river", "crevice", "crater", "hole"],
  "lights.*.type": ["torch", "torch_row", "torch_pair", "campfire", "brazier_ring", "magic", "eyes"], "water.*.type": ["puddles", "pool", "river"],
  "atmosphere.time": ["day", "dusk", "night"], "quality": ["Low", "Medium", "High"], "features.*.material": ["", "rock", "dirt", "moss", "ruin"]};
const WF_GROUPS = [["General", ["seed", "voxel", "chunk", "quality", "min", "max"]], ["Ground", ["ground"]], ["Features (shapes)", ["features"]], ["Lights", ["lights"]],
  ["Water", ["water"]], ["Atmosphere", ["atmosphere"]], ["Colours", ["palette"]], ["Test", ["test"]]];
function wfCss() {
  if ($("#wfCss")) return;
  const s = document.createElement("style"); s.id = "wfCss";
  s.textContent = `.wf-f{display:grid;grid-template-columns:150px 1fr;gap:8px;align-items:center;margin:4px 0}.wf-f>span:first-child{font-size:13px}
  .wf-f input[type=number]{width:96px}.wf-f input[type=text],.wf-f select{max-width:260px}.wf-vec{display:flex;gap:6px;flex-wrap:wrap;align-items:center}.wf-vec input{width:76px}
  .wf-vec i{font-style:normal;color:var(--muted);font-size:12px}.wf-item{border:1px solid var(--line);border-radius:8px;padding:6px 10px;margin:6px 0}.wf-item>summary{cursor:pointer;font-weight:600}
  .wf-help{color:var(--muted);font-size:12px;grid-column:2}.wf-json{width:100%;min-height:460px;font:12.5px ui-monospace,Consolas,monospace}
  .wf-shots{display:grid;grid-template-columns:repeat(auto-fill,minmax(220px,1fr));gap:8px}.wf-shots img{width:100%;border-radius:6px;cursor:zoom-in}
  .wf-pre{white-space:pre-wrap;font:12.5px ui-monospace,Consolas,monospace;background:var(--panel2,rgba(127,127,127,.08));padding:10px;border-radius:8px;max-height:360px;overflow:auto}
  .wf-bar{display:flex;gap:8px;flex-wrap:wrap;align-items:center;margin:10px 0}.wf-recipes tr[data-wfr]{cursor:pointer}.wf-recipes tr.on{background:rgba(127,127,127,.12)}
  @media (max-width:700px){.wf-f{grid-template-columns:1fr}.wf-help{grid-column:1}}`;
  document.head.appendChild(s);
}
const wfLabel = k => String(k).replace(/([a-z])([A-Z])/g, "$1 $2").replace(/^./, c => c.toUpperCase());
const wfChoice = path => { const g = path.replace(/\.\d+\./g, ".*."); return WF_CHOICES[g]; };
const wfHex = c => "#" + ["r", "g", "b"].map(k => Math.round(Math.max(0, Math.min(1, +c[k] || 0)) * 255).toString(16).padStart(2, "0")).join("");
function wfField(path, v, key) {
  const help = WF_HELP[key] ? `<span class="wf-help">${esc(WF_HELP[key])}</span>` : "";
  const lab = `<span>${esc(wfLabel(key))}</span>`;
  const ch = wfChoice(path);
  if (ch && typeof v === "string") return `<label class="wf-f">${lab}<select data-wfp="${esc(path)}" data-t="s">${(ch.includes(v) ? ch : [v, ...ch]).map(o => `<option ${o === v ? "selected" : ""}>${esc(o)}</option>`).join("")}</select>${help}</label>`;
  if (typeof v === "boolean") return `<label class="wf-f">${lab}<input type="checkbox" data-wfp="${esc(path)}" data-t="b" ${v ? "checked" : ""}>${help}</label>`;
  if (typeof v === "number") return `<label class="wf-f">${lab}<input type="number" step="any" data-wfp="${esc(path)}" data-t="n" value="${esc(v)}">${help}</label>`;
  if (typeof v === "string") return `<label class="wf-f">${lab}<input type="text" data-wfp="${esc(path)}" data-t="s" value="${esc(v)}">${help}</label>`;
  if (Array.isArray(v)) {
    if (key === "route" || (v.length && v.every(x => typeof x === "string"))) return `<label class="wf-f">${lab}<input type="text" data-wfp="${esc(path)}" data-t="sa" value="${esc(v.join(", "))}" placeholder="names, separated by commas">${help}</label>`;
    if (key === "points" || key === "viewpoints" || (v.length && v.every(x => x && typeof x === "object" && "x" in x && "z" in x && Object.keys(x).length <= 3)))
      return `<div class="wf-f">${lab}<div>${v.map((p, i) => `<div class="wf-vec">${["x", "y", "z"].map(a => `<i>${a}</i><input type="number" step="any" data-wfp="${esc(path + "." + i + "." + a)}" data-t="n" value="${esc(p[a] ?? 0)}" aria-label="${esc(key + " " + (i + 1) + " " + a)}">`).join("")}
        <button class="small" data-wfdel="${esc(path + "." + i)}" title="remove this point">✕</button></div>`).join("")}<button class="small" data-wfadd="${esc(path)}">+ point</button></div>${help}</div>`;
    return `<div>${v.map((it, i) => { const nm = (it && (it.name || it.type)) || "#" + (i + 1);
      return `<details class="wf-item"><summary>${esc(nm)}${it && it.type && it.name ? ` <span class="muted small">${esc(it.type)}</span>` : ""}</summary>
        ${it && typeof it === "object" ? Object.keys(it).map(k => wfField(path + "." + i + "." + k, it[k], k)).join("") : ""}
        <div class="wf-bar"><button class="small" data-wfdel="${esc(path + "." + i)}">Remove ${esc(nm)}</button><button class="small" data-wfdup="${esc(path + "." + i)}">Copy it</button></div></details>`; }).join("")}
      ${v.length ? "" : '<p class="muted small">None.</p>'}</div>`;
  }
  if (v && typeof v === "object") {
    const ks = Object.keys(v);
    if (ks.length >= 3 && ["r", "g", "b"].every(k => typeof v[k] === "number"))
      return `<label class="wf-f">${lab}<span class="wf-vec"><input type="color" data-wfp="${esc(path)}" data-t="c" value="${wfHex(v)}">${"a" in v ? `<i>opacity</i><input type="number" step="0.05" min="0" max="1" data-wfp="${esc(path + ".a")}" data-t="n" value="${esc(v.a)}">` : ""}</span>${help}</label>`;
    if (ks.length && ks.length <= 3 && ks.every(k => typeof v[k] === "number") && ks.every(k => "xyz".includes(k)))
      return `<div class="wf-f">${lab}<span class="wf-vec">${ks.map(a => `<i>${a}</i><input type="number" step="any" data-wfp="${esc(path + "." + a)}" data-t="n" value="${esc(v[a])}" aria-label="${esc(key + " " + a)}">`).join("")}</span>${help}</div>`;
    return ks.map(k => wfField(path + "." + k, v[k], k)).join("");
  }
  return "";
}
function wfWalk(obj, path) { const ps = path.split("."); let o = obj; for (let i = 0; i < ps.length - 1; i++) o = o[Array.isArray(o) ? +ps[i] : ps[i]]; return [o, Array.isArray(o) ? +ps[ps.length - 1] : ps[ps.length - 1]]; }
function wfCollect(m) {
  const r = JSON.parse(JSON.stringify(WFS.recipe));
  for (const el of $$("[data-wfp]", m)) {
    const [o, k] = wfWalk(r, el.dataset.wfp), t = el.dataset.t;
    if (t === "n") { const x = parseFloat(el.value); if (!isFinite(x)) throw new Error(wfLabel(el.dataset.wfp) + " needs a number"); o[k] = x; }
    else if (t === "b") o[k] = el.checked;
    else if (t === "s") o[k] = el.value;
    else if (t === "sa") o[k] = el.value.split(",").map(x => x.trim()).filter(Boolean);
    else if (t === "c") { const h = el.value; o[k] = Object.assign(o[k] || {}, {r: parseInt(h.slice(1, 3), 16) / 255, g: parseInt(h.slice(3, 5), 16) / 255, b: parseInt(h.slice(5, 7), 16) / 255}); }
  }
  return r;
}
function wfJobHtml(j) {
  if (!j) return "";
  const live = j.status === "running";
  return `<div class="panel"><h3>${live ? '<span class="spin"></span> ' : ""}${esc(j.name)}: ${esc(live ? j.step : j.status)} <span class="muted small">(${esc(j.seconds)} s, started ${esc(j.started)})</span></h3>
    ${j.progress ? `<div class="wf-pre">${esc(j.progress.join("\n"))}</div>` : ""}${j.log && j.log.length ? `<details ${live ? "" : "open"}><summary class="small">Output</summary><div class="wf-pre">${esc(j.log.join("\n"))}</div></details>` : ""}
    ${live ? '<div class="wf-bar"><button id="wfCancel">Cancel</button><span class="muted small">A test takes about 4 to 6 minutes (a build first adds 2 to 4).</span></div>' : ""}</div>`;
}
R.worldforge = async (m, arg, alive) => {
  wfCss(); clearTimeout(WFS.timer);
  const s = await api("/api/wf/state"); if (!alive()) return;
  if (!s.available) { m.innerHTML = `<h2>WorldForge</h2><div class="notice warn">${esc(s.why)}</div>`; return; }
  if (arg && arg !== WFS.name) { WFS.name = arg; WFS.recipe = null; WFS.dirty = false; }
  if (!WFS.name && s.recipes.length) WFS.name = s.recipes[0].name;
  if (WFS.name && !WFS.recipe) { try { WFS.recipe = (await api("/api/wf/recipe?name=" + enc(WFS.name))).recipe; } catch (e) { WFS.name = null; } if (!alive()) return; }
  const live = s.job && s.job.status === "running";
  const res = WFS.name && WFS.tab === "results" ? await api("/api/wf/result?name=" + enc(WFS.name)).catch(() => null) : null; if (!alive()) return;
  m.innerHTML = `<h2>WorldForge</h2><p class="lede">Environments made from a recipe: mountains, cliffs, paths, ruins, tunnels, caves, chambers, rivers and craters as real
    geometry with collision, then lights, water and effects. Change the values (or the JSON), save, and <b>Build &amp; test</b> runs the playable preview in the sandbox
    (${esc(s.sandbox)}): the hunter walks the route, navigation, collision, the mesh, the camera, the feet, lights and frame times are measured, and a report is written.
    <b>Play</b> opens the preview on the recipe: walk it with WASD, F1 quality, F2 time of day, F3 weather; save here and press F5 there to rebuild.</p>
    ${!s.unity ? '<div class="notice warn">Unity 6000.6.7f1 was not found: building needs it (playing an existing preview does not).</div>' : ""}
    <div id="wfJob">${wfJobHtml(s.job)}</div>
    <div class="panel"><h3>Recipes <span class="muted small">(${esc(s.recipes.length)}, in the project's WorldForge/Recipes)</span></h3>
      ${s.recipes.length ? `<table class="wf-recipes"><tr><th>Recipe</th><th>Saved</th><th>Last test</th><th>Findings</th></tr>${s.recipes.map(r => `<tr data-wfr="${esc(r.name)}" tabindex="0" class="${r.name === WFS.name ? "on" : ""}">
        <td><b>${esc(r.name)}</b></td><td class="small muted">${esc(r.saved || "")}</td><td class="small muted">${esc(r.tested || "not tested")}</td><td>${r.totals ? totalsHtml(r.totals) : ""}</td></tr>`).join("")}</table>` : '<p class="muted">No recipes yet.</p>'}
      <div class="wf-bar"><span class="muted small">Preview build: ${esc(s.preview || "none yet")}</span>${WFS.name ? `<button id="wfCopy">Copy ${esc(WFS.name)} as a new recipe…</button>` : ""}</div></div>
    ${WFS.name ? `<div class="panel"><h3>${esc(WFS.name)}${WFS.dirty ? ' <span class="pill">not saved</span>' : ""}</h3>
      <div class="tabs" role="tablist">${[["values", "Values"], ["json", "JSON"], ["results", "Last test"]].map(([k, l]) => `<button role="tab" data-wft="${k}" class="${WFS.tab === k ? "on" : ""}" aria-selected="${WFS.tab === k}">${l}</button>`).join("")}</div>
      <div id="wfBody"></div>
      <div class="wf-bar">${WFS.tab !== "results" ? '<button id="wfSave">Save</button>' : ""}
        <button class="primary" id="wfRun" ${live || !RUN ? "disabled" : ""}>${WFS.tab !== "results" ? "Save, " : ""}Build &amp; test</button>
        <label class="small"><input type="checkbox" id="wfBuild"> build the preview again first</label>
        <button id="wfPlay" ${live || !s.preview || !RUN ? "disabled" : ""}>Play the preview</button>
        ${!RUN ? '<span class="muted small">Running is switched off in Privacy &amp; access.</span>' : ""}</div></div>` : ""}`;
  const body = $("#wfBody", m);
  if (body && WFS.tab === "values") body.innerHTML = WF_GROUPS.map(([g, keys]) => { const ks = keys.filter(k => k in WFS.recipe); if (!ks.length) return "";
    return `<details class="wf-item" ${g === "General" || g === "Features (shapes)" ? "open" : ""}><summary>${esc(g)}</summary>${ks.map(k => Array.isArray(WFS.recipe[k]) ? wfField(k, WFS.recipe[k], k) + `<button class="small" data-wfnew="${esc(k)}">+ add to ${esc(wfLabel(k).toLowerCase())}</button>` : wfField(k, WFS.recipe[k], k)).join("")}</details>`; }).join("");
  if (body && WFS.tab === "json") body.innerHTML = `<textarea class="wf-json" id="wfJson" spellcheck="false">${esc(JSON.stringify(WFS.recipe, null, 1))}</textarea>`;
  if (body && WFS.tab === "results") body.innerHTML = !res || !res.report ? '<p class="muted">This recipe has not been tested yet: Build &amp; test makes the report.</p>' : `
    <p>${totalsHtml(res.totals)} <span class="muted small">${esc(res.created || "")}</span> <button class="small" id="wfReport">Open the full report</button></p>
    <div class="wf-pre">${esc(res.summary)}</div>
    ${res.video && SETTINGS.showArt ? `<h3>The walk (video)</h3><video controls preload="metadata" style="width:100%;max-width:960px;border-radius:8px" src="/wf/${enc(WFS.name)}/walkthrough.mp4"></video>` : ""}
    ${SETTINGS.showArt && res.shots.length ? `<h3>Viewpoints</h3><div class="wf-shots">${res.shots.map(f => `<figure style="margin:0"><img src="/wf/${enc(WFS.name)}/shots/${enc(f)}" alt="${esc(f)}" loading="lazy" data-wfimg><figcaption class="small muted">${esc(f.replace(/\.jpg$/, "").replace("_", " · "))}</figcaption></figure>`).join("")}</div>` : ""}
    ${SETTINGS.showArt && (res.map || res.sheet) ? `<h3>Map and walk</h3><div class="wf-shots">${[res.map, res.sheet, ...res.charts].filter(Boolean).map(p => `<img src="${fileUrl(p)}" alt="" loading="lazy" data-wfimg>`).join("")}</div>` : artOff()}
    <h3>Findings (${esc(res.findings.length)})</h3>${res.findings.length ? `<table><tr><th>Kind</th><th>Check</th><th>Where</th><th>What was measured</th></tr>${res.findings.map(f => `<tr><td>${kindPill(f.kind)}</td><td>${esc(f.check)}</td><td class="small">${esc(f.subject)}</td>
      <td class="small">${esc(f.detail)}${f.suggestedFix ? `<div class="muted">fix: ${esc(f.suggestedFix)}</div>` : ""}</td></tr>`).join("")}</table>` : '<p class="muted">Nothing found.</p>'}`;
  // events
  $$("tr[data-wfr]", m).forEach(tr => { tr.onclick = () => { if (WFS.dirty && !confirm("Leave without saving the changes to " + WFS.name + "?")) return; WFS.name = tr.dataset.wfr; WFS.recipe = null; WFS.dirty = false; go("worldforge", WFS.name); };
    tr.onkeydown = e => { if (e.key === "Enter") tr.click(); }; });
  $$("[data-wft]", m).forEach(b => b.onclick = () => { try { if (WFS.tab === "values") WFS.recipe = wfCollect(m); else if (WFS.tab === "json") WFS.recipe = JSON.parse($("#wfJson", m).value); } catch (e) { return toast("Not switched: " + e.message); } WFS.tab = b.dataset.wft; route(); });
  if (body) body.addEventListener("input", () => { if (!WFS.dirty) { WFS.dirty = true; const h = $(".panel h3", body.parentElement); if (h && !h.querySelector(".pill")) h.insertAdjacentHTML("beforeend", ' <span class="pill">not saved</span>'); } });
  const edit = fn => { try { WFS.recipe = wfCollect(m); } catch (e) { return toast(e.message); } fn(); WFS.dirty = true; route(); };
  $$("[data-wfdel]", m).forEach(b => b.onclick = e => { e.preventDefault(); edit(() => { const [o, k] = wfWalk(WFS.recipe, b.dataset.wfdel); o.splice(k, 1); }); });
  $$("[data-wfdup]", m).forEach(b => b.onclick = e => { e.preventDefault(); edit(() => { const [o, k] = wfWalk(WFS.recipe, b.dataset.wfdup); const c = JSON.parse(JSON.stringify(o[k])); if (c.name) c.name += " copy"; o.splice(k + 1, 0, c); }); });
  $$("[data-wfadd]", m).forEach(b => b.onclick = e => { e.preventDefault(); edit(() => { const [o, k] = wfWalk(WFS.recipe, b.dataset.wfadd); const a = o[k], last = a[a.length - 1] || {x: 0, y: 0, z: 0}; a.push({x: last.x, y: last.y, z: last.z + 5}); }); });
  $$("[data-wfnew]", m).forEach(b => b.onclick = e => { e.preventDefault(); edit(() => { const a = WFS.recipe[b.dataset.wfnew]; const c = a.length ? JSON.parse(JSON.stringify(a[a.length - 1])) : {name: "new", type: ""}; if ("name" in c) c.name = "new " + (c.type || "item"); a.push(c); }); });
  $$("[data-wfimg]", m).forEach(i => i.onclick = () => lightbox(i.src));
  const current = () => WFS.tab === "values" ? wfCollect(m) : WFS.tab === "json" ? JSON.parse($("#wfJson", m).value) : WFS.recipe;
  const save = async () => { const r = current(); await api("/api/wf/save", {name: WFS.name, recipe: r}); WFS.recipe = r; WFS.dirty = false; };
  const sv = $("#wfSave", m); if (sv) sv.onclick = async () => { try { await save(); toast("Saved " + WFS.name + " (the old version is kept in Recipes/.history)"); route(); } catch (e) { toast("Not saved: " + e.message); } };
  const rn = $("#wfRun", m); if (rn) rn.onclick = async () => { rn.disabled = true; try { if (WFS.tab !== "results") await save(); await api("/api/wf/run", {name: WFS.name, build: $("#wfBuild", m).checked}); toast("Started: " + WFS.name); WFS.tab = "results"; route(); } catch (e) { toast("Not started: " + e.message); rn.disabled = false; } };
  const pl = $("#wfPlay", m); if (pl) pl.onclick = async () => { try { if (WFS.dirty) await save(); await api("/api/wf/play", {name: WFS.name}); toast("The preview is opening in its own window (Esc quits it)"); } catch (e) { toast("Not opened: " + e.message); } };
  const cp = $("#wfCopy", m); if (cp) cp.onclick = async () => { const to = (prompt("Name of the new recipe (letters, digits, _ and -):", WFS.name + "_2") || "").trim(); if (!to) return;
    try { await api("/api/wf/copy", {name: WFS.name, to}); WFS.name = to; WFS.recipe = null; WFS.dirty = false; go("worldforge", to); } catch (e) { toast("Not copied: " + e.message); } };
  const rp = $("#wfReport", m); if (rp) rp.onclick = () => openPage(res.report);
  const cn = $("#wfCancel", m); if (cn) cn.onclick = async () => { if (!confirm("Stop the running WorldForge job?")) return; await api("/api/wf/cancel", {}); route(); };
  // while a job runs only its panel refreshes (the editor keeps what is being typed)
  if (live) { const tick = async () => { if (!alive() || VIEW !== "worldforge") return; let st; try { st = await api("/api/wf/state"); } catch (e) { return; } if (!alive()) return;
      if (st.job && st.job.status === "running") { $("#wfJob", m).innerHTML = wfJobHtml(st.job); const c = $("#wfCancel", m); if (c) c.onclick = cn.onclick; WFS.timer = setTimeout(tick, 3000); }
      else if (!WFS.dirty) route(); else { $("#wfJob", m).innerHTML = wfJobHtml(st.job); toast("The WorldForge job finished: open Last test to see it"); } };
    WFS.timer = setTimeout(tick, 3000); }
};
