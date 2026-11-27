import * as App from "../bindings/gitee.com/wangbenchi66/dbsync/app.js";

type Endpoint = { kind: string; dsn?: string; path?: string; schema?: string };
type Column = {
  name: string; status: string; sourceType: string; targetType: string;
  sourceNull: boolean; targetNull: boolean; sourceDefault: string; targetDefault: string; reasons: string[];
};
type Table = {
  name: string; status: string; columns: Column[];
  indexes: { name: string; status: string }[];
  constraints: { name: string; kind: string; status: string }[];
  data?: { sourceCount: number; targetCount: number; inserts: number; updates: number; deletes: number; unsupportedKey: boolean; rows: { kind: string; key: string; cells: { name: string; source: string; target: string }[] }[]; truncated: boolean };
};
type CompareView = {
  runId: string; sourceName: string; targetName: string;
  tables: Table[]; views: { name: string; status: string }[];
  routines?: { name: string; status: string }[];
  triggers?: { name: string; status: string }[];
  dependencies?: { nodes: { name: string; schema?: string }[]; edges: { from: string; to: string; constraint: string; schema?: string }[]; cycles: string[][] };
  added: number; removed: number; changed: number; same: number; hasData: boolean;
};
type PlanView = {
  runId: string; blocked: boolean; fingerprint: string;
  diagnostics: { severity: string; code: string; object: string; message: string }[];
  phases: { phase: number; statements: { sql: string; object: string; destructive: boolean }[] }[];
};

type LibraryEntry = {
  path: string; name: string; schema: string; createdAt: string;
  tables: number; rows: number; hasData: boolean; size: number; error?: string;
  tags?: string[]; note?: string;
};

const $ = (id: string) => document.getElementById(id) as HTMLElement;
const input = (id: string) => document.getElementById(id) as HTMLInputElement;
const select = (id: string) => document.getElementById(id) as HTMLSelectElement;

let view: CompareView | null = null;
let plan: PlanView | null = null;
let activeTable: Table | null = null;

const phaseNames: Record<number, string> = {
  1: "1 删除依赖", 2: "2 数据清理", 3: "3 结构变更",
  4: "4 数据装载", 5: "5 结构收尾", 6: "6 对象", 7: "7 自检",
};
const statusText: Record<string, string> = { added: "仅源", removed: "仅目标", changed: "变更", same: "相同" };

function setStatus(text: string) { $("status").textContent = text; }
function setMsg(text: string, error = false) {
  const el = $("msg");
  el.textContent = text;
  el.style.color = error ? "var(--danger)" : "var(--ok)";
}
function esc(s: unknown): string {
  return String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c] as string));
}
function badge(status: string): string {
  return `<span class="badge ${esc(status)}">${esc(statusText[status] ?? status)}</span>`;
}
function endpoint(prefix: "source" | "target"): Endpoint {
  const kind = select(`${prefix}Kind`).value;
  const ep: Endpoint = {
    kind,
    schema: input(`${prefix}Schema`).value.trim(),
    dsn: input(`${prefix}DSN`).value.trim(),
    path: input(`${prefix}Path`).value.trim(),
  };
  if (kind === "snapshot") ep.dsn = "";
  else ep.path = "";
  return ep;
}
function syncKind(prefix: "source" | "target") {
  const kind = select(`${prefix}Kind`).value;
  const file = kind === "snapshot" || kind === "sqlite" || kind === "dir";
  input(`${prefix}DSN`).classList.toggle("hidden", file);
  input(`${prefix}Path`).classList.toggle("hidden", !file);
  input(`${prefix}Schema`).classList.toggle("hidden", kind === "sqlite" || kind === "snapshot" || kind === "dir");
  input(`${prefix}Path`).placeholder = kind === "sqlite" ? "SQLite 文件路径" : kind === "dir" ? "DDL 目录路径" : "baseline.dbsync 路径";
  if (kind === "dir") input("compareData").checked = false;
  input(`${prefix}Schema`).placeholder = "库名（可空）";
}

async function test(prefix: "source" | "target") {
  try {
    setStatus(`测试 ${prefix}...`);
    const info = await App.TestConnection(endpoint(prefix));
    setStatus(`连接成功：${info.version} / ${info.database || "(默认库)"}`);
  } catch (e) {
    setStatus("连接失败");
    setMsg(String(e), true);
  }
}

function renderTables() {
  const el = $("tableList");
  if (!view) return;
  $("summary").textContent = `${view.added} 增 / ${view.removed} 删 / ${view.changed} 变`;
  if (view.tables.length === 0) {
    el.className = "list empty";
    el.textContent = "没有表差异";
    return;
  }
  el.className = "list";
  el.innerHTML = "";
  for (const t of view.tables) {
    const b = document.createElement("button");
    b.className = "obj" + (activeTable?.name === t.name ? " active" : "");
    b.innerHTML = `<span class="name">${esc(t.name)}</span>${badge(t.status)}`;
    b.addEventListener("click", () => { activeTable = t; renderTables(); renderDetail(); });
    el.appendChild(b);
  }
}
function renderDetail() {
  const el = $("detail");
  if (!activeTable) {
    el.className = "detail empty";
    el.textContent = "选择左侧对象查看列/索引/约束";
    return;
  }
  const t = activeTable;
  el.className = "detail";
  $("detailTitle").textContent = `结构差异 · ${t.name}`;
  let html = `<h3>${esc(t.name)} ${badge(t.status)}</h3>`;
  const deps = view?.dependencies;
  if (deps) {
    const out = deps.edges.filter((e) => e.from === t.name);
    const inb = deps.edges.filter((e) => e.to === t.name);
    if (out.length || inb.length) {
      html += `<h3>依赖影响</h3><table><thead><tr><th>方向</th><th>约束</th><th>表</th></tr></thead><tbody>`;
      for (const e of out) html += `<tr><td>引用</td><td class="mono">${esc(e.constraint)}</td><td>${esc(e.to)}</td></tr>`;
      for (const e of inb) html += `<tr><td>被引用</td><td class="mono">${esc(e.constraint)}</td><td>${esc(e.from)}</td></tr>`;
      html += `</tbody></table>`;
    }
    const cyc = deps.cycles.find((c) => c.includes(t.name));
    if (cyc) html += `<p class="reason">存在外键环：${esc(cyc.join(" → "))}</p>`;
  }

  if (t.data) {
    const d = t.data;
    html += `<p class="muted">数据：源 ${d.sourceCount} 行，目标 ${d.targetCount} 行；INSERT ${d.inserts} / UPDATE ${d.updates} / DELETE ${d.deletes}${d.unsupportedKey ? ' <span class="reason">（无主键，不能生成逐行 DML）</span>' : ""}</p>`;
    if (d.rows?.length) {
      html += `<h3>数据差异 ${d.truncated ? "（仅前 50 行）" : ""}</h3><table><thead><tr><th>类型</th><th>主键</th><th>变化</th></tr></thead><tbody>`;
      for (const r of d.rows) {
        const kind = r.kind === "insert" ? "新增" : r.kind === "delete" ? "删除" : "变更";
        const cells = r.cells.map((c) => {
          if (r.kind === "insert") return `${esc(c.name)}=${esc(c.source)}`;
          if (r.kind === "delete") return `${esc(c.name)}=${esc(c.target)}`;
          return `${esc(c.name)}: ${esc(c.target)} → ${esc(c.source)}`;
        }).join("；");
        html += `<tr><td><span class="badge ${r.kind}">${kind}</span></td><td class="mono">${esc(r.key)}</td><td class="mono">${cells}</td></tr>`;
      }
      html += `</tbody></table>`;
    }
  }
  html += `<table><thead><tr><th>列</th><th>状态</th><th>源类型</th><th>目标类型</th><th>可空</th><th>默认值</th></tr></thead><tbody>`;
  for (const c of t.columns) {
    const reasons = c.reasons?.length ? `<div class="reason">${esc(c.reasons.join("；"))}</div>` : "";
    html += `<tr><td class="mono">${esc(c.name)}${reasons}</td><td>${badge(c.status)}</td><td class="mono">${esc(c.sourceType)}</td><td class="mono">${esc(c.targetType)}</td><td>${c.sourceNull ? "是" : "否"} / ${c.targetNull ? "是" : "否"}</td><td class="mono">${esc(c.sourceDefault || c.targetDefault)}</td></tr>`;
  }
  html += `</tbody></table>`;
  if (t.indexes?.length) {
    html += `<h3>索引</h3><table><thead><tr><th>名称</th><th>状态</th></tr></thead><tbody>`;
    for (const x of t.indexes) html += `<tr><td class="mono">${esc(x.name)}</td><td>${badge(x.status)}</td></tr>`;
    html += `</tbody></table>`;
  }
  if (t.constraints?.length) {
    html += `<h3>约束</h3><table><thead><tr><th>名称</th><th>类型</th><th>状态</th></tr></thead><tbody>`;
    for (const x of t.constraints) html += `<tr><td class="mono">${esc(x.name)}</td><td>${esc(x.kind)}</td><td>${badge(x.status)}</td></tr>`;
    html += `</tbody></table>`;
  }
  if (view?.views?.length) {
    html += `<h3>视图</h3><table><thead><tr><th>名称</th><th>状态</th></tr></thead><tbody>`;
    for (const x of view.views) html += `<tr><td class="mono">${esc(x.name)}</td><td>${badge(x.status)}</td></tr>`;
    html += `</tbody></table>`;
  }
  el.innerHTML = html;
}

async function saveReport() {
  if (!view) { setMsg("请先比较", true); return; }
  const path = input("reportPath").value.trim();
  if (!path) { setMsg("请填写报告保存路径", true); return; }
  try {
    setMsg("");
    await App.SaveReport(view.runId, select("reportFormat").value, path);
    setStatus(`报告已保存：${path}`);
  } catch (e) {
    setStatus("保存报告失败");
    setMsg(String(e), true);
  }
}

async function runMulti() {
  const kind = select("multiKind").value;
  const schema = input("multiSchema").value.trim();
  const lines = (document.getElementById("multiTargets") as HTMLTextAreaElement).value
    .split("\n").map((s) => s.trim()).filter(Boolean);
  if (!lines.length) { setMsg("请填写至少一个目标", true); return; }
  const targets = lines.map((line) => ({
    kind, schema,
    dsn: kind === "sqlite" || kind === "snapshot" || kind === "dir" ? "" : line,
    path: kind === "sqlite" || kind === "snapshot" || kind === "dir" ? line : "",
  }));
  try {
    setMsg("");
    setStatus("多目标比较中...");
    const res = await App.CompareMulti({
      source: endpoint("source"),
      targets,
      compareData: input("compareData").checked,
      allowDestructive: input("allowDestructive").checked,
      applyRules: input("applyRules").checked,
    });
    const list = $("multiList");
    list.innerHTML = (res?.targets ?? []).map((t: any) => {
      if (t.error) return `<div class="lib-item"><span class="name">${esc(t.name)}</span><span class="reason">${esc(t.error)}</span></div>`;
      const v = t.view;
      return `<div class="lib-item"><span class="name">${esc(t.name)}</span><span class="meta">${v.added} 增 / ${v.removed} 删 / ${v.changed} 变 / ${v.same} 同</span></div>`;
    }).join("");
    setStatus(`多目标完成：${res?.targets?.length ?? 0} 个目标`);
  } catch (e) {
    setStatus("多目标比较失败");
    setMsg(String(e), true);
  }
}

async function openRules() {
  try {
    const rs: { kind: string; pattern: string; note?: string }[] = await App.Rules();
    (document.getElementById("rulesText") as HTMLTextAreaElement).value =
      rs.map((r) => `${r.kind}:${r.pattern}${r.note ? " " + r.note : ""}`).join("\n");
    $("rulesModal").classList.remove("hidden");
  } catch (e) {
    setMsg(String(e), true);
  }
}

async function saveRules() {
  const lines = (document.getElementById("rulesText") as HTMLTextAreaElement).value.split("\n");
  const rs: { kind: string; pattern: string; note?: string }[] = [];
  for (const raw of lines) {
    const line = raw.trim();
    if (!line) continue;
    const i = line.indexOf(":");
    if (i <= 0) continue;
    const kind = line.slice(0, i).trim().toLowerCase();
    const rest = line.slice(i + 1).trim();
    const sp = rest.indexOf(" ");
    const pattern = sp < 0 ? rest : rest.slice(0, sp);
    const note = sp < 0 ? "" : rest.slice(sp + 1).trim();
    if (pattern) rs.push({ kind, pattern, note });
  }
  try {
    await App.SaveRules(rs);
    $("rulesModal").classList.add("hidden");
    setStatus(`已保存 ${rs.length} 条忽略规则`);
  } catch (e) {
    setMsg(String(e), true);
  }
}

async function openLibrary() {
  try {
    const dir = await App.SnapshotLibraryDir();
    $("libraryPath").textContent = dir;
    const entries = await App.SnapshotLibrary();
    const list = $("libraryList");
    list.innerHTML = (entries ?? []).length
      ? (entries as LibraryEntry[]).map((e) => `
        <div class="lib-item">
          <span class="name">${esc(e.name)}</span>
          <span class="meta">${esc(e.schema)} · ${e.tables} 表 · ${e.rows} 行 · ${e.hasData ? "含数据" : "仅结构"}${e.tags?.length ? ` · ${e.tags.map(esc).join(" / ")}` : ""}${e.note ? ` · ${esc(e.note)}` : ""}</span>
          <button data-path="${esc(e.path)}" class="ghost use">使用</button>
          <button data-path="${esc(e.path)}" class="ghost tag">标签</button>
          <button data-path="${esc(e.path)}" class="ghost del">删除</button>
        </div>`).join("")
      : '<div class="empty">快照库为空。先用“导出源端快照”生成，或把 .dbsync 文件放进库目录。</div>';
    list.querySelectorAll<HTMLButtonElement>("button.use").forEach((b) => {
      b.onclick = () => useSnapshot(b.dataset.path!);
    });
    list.querySelectorAll<HTMLButtonElement>("button.tag").forEach((b) => {
      b.onclick = async () => {
        const entry = (entries as LibraryEntry[]).find((x) => x.path === b.dataset.path);
        const tagsRaw = prompt("标签（逗号分隔）", (entry?.tags ?? []).join(", "));
        if (tagsRaw === null) return;
        const note = prompt("备注", entry?.note ?? "");
        if (note === null) return;
        const tags = tagsRaw.split(",").map((s) => s.trim()).filter(Boolean);
        await App.SnapshotSetMeta(b.dataset.path!, tags, note);
        await openLibrary();
      };
    });
    list.querySelectorAll<HTMLButtonElement>("button.del").forEach((b) => {
      b.onclick = async () => {
        if (!confirm("删除这个快照文件？")) return;
        await App.SnapshotDelete(b.dataset.path!);
        await openLibrary();
      };
    });
    $("libraryModal").classList.remove("hidden");
  } catch (e) {
    setMsg(String(e), true);
  }
}

function useSnapshot(path: string) {
  select("sourceKind").value = "snapshot";
  input("sourcePath").value = path;
  syncKind("source");
  $("libraryModal").classList.add("hidden");
  setStatus(`已选择快照：${path}`);
}

async function createSnapshot() {
  const out = input("snapshotOut").value.trim();
  if (!out) { setMsg("请填写快照输出路径", true); return; }
  try {
    setMsg("");
    setStatus("导出快照中...");
    const m = await App.CreateSnapshot(endpoint("source"), out, true);
    const rows = (m.tables ?? []).reduce((n: number, t: any) => n + (t.rowCount ?? 0), 0);
    setStatus(`快照已导出：${m.tableCount} 表 / ${rows} 行（默认不加密）`);
    setMsg("快照写入完成；不会修改数据库", false);
  } catch (e) {
    setStatus("导出快照失败");
    setMsg(String(e), true);
  }
}

async function compare() {
  try {
    setMsg("");
    setStatus("比较中...");
    plan = null;
    $("planList").className = "plan-list empty";
    $("planList").textContent = "比较后生成差异 SQL 预览（不会执行）";
    $("diagnostics").innerHTML = "";
    $("planBadge").textContent = "只读";
    view = await App.Compare({
      source: endpoint("source"),
      target: endpoint("target"),
      compareData: input("compareData").checked,
      allowDestructive: input("allowDestructive").checked,
      applyRules: input("applyRules").checked,
    });
    if (!view) throw new Error("没有返回比较结果");
    activeTable = view.tables.find((t) => t.status !== "same") ?? view.tables[0] ?? null;
    renderTables();
    renderDetail();
    setStatus(`比较完成：${view.sourceName} → ${view.targetName}`);
  } catch (e) {
    setStatus("比较失败");
    setMsg(String(e), true);
  }
}

async function buildPlan() {
  if (!view) { setMsg("请先比较", true); return; }
  try {
    setMsg("");
    setStatus("生成计划...");
    plan = await App.Plan(view.runId, input("allowDestructive").checked);
    if (!plan) throw new Error("没有返回计划");
    const d = $("diagnostics");
    d.innerHTML = plan.diagnostics.length
      ? plan.diagnostics.map((x) => `<div class="diag ${esc(x.severity)}">${esc(x.message)}${x.object ? ` <span class="muted">(${esc(x.object)})</span>` : ""}</div>`).join("")
      : `<div class="diag info">无阻断项</div>`;
    const list = $("planList");
    if (!plan.phases.length) {
      list.className = "plan-list empty";
      list.textContent = "没有需要执行的语句";
    } else {
      list.className = "plan-list";
      list.innerHTML = plan.phases.map((p) => `<div class="phase"><h4>${esc(phaseNames[p.phase] ?? `阶段 ${p.phase}`)} · ${p.statements.length} 条</h4>${p.statements.map((s) => `<div class="stmt">${esc(s.sql)}</div>`).join("")}</div>`).join("");
    }
    $("planBadge").textContent = plan.blocked ? "有阻断" : `${plan.phases.reduce((n, p) => n + p.statements.length, 0)} 条 · 只读`;
    setStatus(plan.blocked ? "预览有阻断；不会执行任何语句" : "SQL 预览已生成（只读，不会执行）");
  } catch (e) {
    setStatus("预览失败");
    setMsg(String(e), true);
  }
}

for (const p of ["source", "target"] as const) {
  select(`${p}Kind`).addEventListener("change", () => syncKind(p));
  document.getElementById(`test${p === "source" ? "Source" : "Target"}`)!.addEventListener("click", () => test(p));
  syncKind(p);
}
$("btnCompare").addEventListener("click", compare);
$("btnPlan").addEventListener("click", buildPlan);
$("btnSnapshot").addEventListener("click", createSnapshot);
$("btnLibrary").addEventListener("click", openLibrary);
$("btnReport").addEventListener("click", saveReport);
$("btnMulti").addEventListener("click", () => $("multiModal").classList.remove("hidden"));
$("closeMulti").addEventListener("click", () => $("multiModal").classList.add("hidden"));
$("runMulti").addEventListener("click", runMulti);
$("btnRules").addEventListener("click", openRules);
$("closeRules").addEventListener("click", () => $("rulesModal").classList.add("hidden"));
$("saveRules").addEventListener("click", saveRules);
$("closeLibrary").addEventListener("click", () => $("libraryModal").classList.add("hidden"));
$("libraryModal").addEventListener("click", (e) => {
  if (e.target === $("libraryModal")) $("libraryModal").classList.add("hidden");
});
setStatus("只读模式 · 不执行任何 SQL");
$("viewport").textContent = "只读比对";
