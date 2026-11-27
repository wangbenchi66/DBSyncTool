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
  data?: { sourceCount: number; targetCount: number; inserts: number; updates: number; deletes: number; unsupportedKey: boolean };
};
type CompareView = {
  runId: string; sourceName: string; targetName: string;
  tables: Table[]; views: { name: string; status: string }[];
  added: number; removed: number; changed: number; same: number; hasData: boolean;
};
type PlanView = {
  runId: string; blocked: boolean; fingerprint: string;
  diagnostics: { severity: string; code: string; object: string; message: string }[];
  phases: { phase: number; statements: { sql: string; object: string; destructive: boolean }[] }[];
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
  const snap = select(`${prefix}Kind`).value === "snapshot";
  input(`${prefix}DSN`).classList.toggle("hidden", snap);
  input(`${prefix}Path`).classList.toggle("hidden", !snap);
  input(`${prefix}Schema`).placeholder = snap ? "库名（可空）" : "库名（可空）";
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
  if (t.data) {
    const d = t.data;
    html += `<p class="muted">数据：源 ${d.sourceCount} 行，目标 ${d.targetCount} 行；INSERT ${d.inserts} / UPDATE ${d.updates} / DELETE ${d.deletes}${d.unsupportedKey ? ' <span class="reason">（无主键，不能生成逐行 DML）</span>' : ""}</p>`;
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
setStatus("只读模式 · 不执行任何 SQL");
$("viewport").textContent = "只读比对";
