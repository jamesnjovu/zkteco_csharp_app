const $ = (id) => document.getElementById(id);

// ---- Config / connection ----

async function loadConfig() {
  const r = await fetch("/api/config");
  const j = await r.json();
  const d = j.device;
  $("cfg-ip").value = d.ip;
  $("cfg-port").value = d.port;
  $("cfg-password").value = d.password ?? 0;
  $("cfg-timeout").value = d.timeout ?? 10;
  $("cfg-machine").value = d.machineNumber ?? 1;
  $("device-addr").textContent = `${d.ip}:${d.port}`;
}

function deviceOverrides() {
  return {
    ip: $("cfg-ip").value.trim(),
    port: parseInt($("cfg-port").value, 10),
    password: parseInt($("cfg-password").value, 10) || 0,
    timeout: parseInt($("cfg-timeout").value, 10) || 10,
    machineNumber: parseInt($("cfg-machine").value, 10) || 1,
  };
}

// ---- Tab switching ----

document.querySelectorAll(".tab").forEach((tab) => {
  tab.addEventListener("click", () => {
    document.querySelectorAll(".tab").forEach((t) => t.classList.remove("active"));
    document.querySelectorAll(".tab-pane").forEach((p) => p.classList.remove("active"));
    tab.classList.add("active");
    $("tab-" + tab.dataset.tab).classList.add("active");
  });
});

// ---- Output helpers ----

function show(elId, ok, payload) {
  const el = $(elId);
  el.classList.remove("ok", "err");
  el.classList.add(ok ? "ok" : "err");
  el.textContent = typeof payload === "string" ? payload : JSON.stringify(payload, null, 2);
}

function showHtml(elId, ok, html) {
  const el = $(elId);
  el.classList.remove("ok", "err");
  el.classList.add(ok ? "ok" : "err");
  el.innerHTML = html;
}

async function callJson(path, body) {
  const res = await fetch(path, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body ?? {}),
  });
  const j = await res.json().catch(() => ({ ok: false, error: `HTTP ${res.status}` }));
  return { ok: res.ok && j.ok !== false, body: j };
}

// ---- Actions ----

const actions = {
  // Connection
  async connect(btn) {
    $("conn-msg").textContent = "Connecting...";
    const { ok, body } = await callJson("/api/connect", deviceOverrides());
    const status = $("conn-status");
    if (ok) {
      status.className = "status-dot online";
      status.textContent = "connected";
      $("conn-msg").textContent = body.message;
      $("device-addr").textContent = `${$("cfg-ip").value}:${$("cfg-port").value}`;
    } else {
      status.className = "status-dot offline";
      status.textContent = "disconnected";
      $("conn-msg").textContent = body.error || "Connection failed";
    }
  },

  // Users
  async "list-users"(btn) {
    show("out-list-users", true, "Fetching users...");
    const { ok, body } = await callJson("/api/users", deviceOverrides());
    if (!ok) return show("out-list-users", false, body.error || body);

    const rows = body.users.map((u) =>
      `<tr><td>${u.enrollNumber}</td><td>${u.name || ""}</td><td>${u.privilege}</td><td>${u.enabled ? "yes" : "no"}</td></tr>`
    ).join("");

    showHtml("out-list-users", true, `<table>
      <thead><tr><th>Enroll #</th><th>Name</th><th>Privilege</th><th>Enabled</th></tr></thead>
      <tbody>${rows || '<tr><td colspan="4">No users</td></tr>'}</tbody>
    </table><p style="margin:8px 0 0;color:var(--muted)">Total: ${body.count} users</p>`);
  },

  async "get-user"(btn) {
    const enroll = $("get-user-enroll").value.trim();
    if (!enroll) return show("out-get-user", false, "Enter enroll number");
    show("out-get-user", true, "Fetching...");
    const { ok, body } = await callJson("/api/user", { ...deviceOverrides(), enrollNumber: enroll });
    if (!ok) return show("out-get-user", false, body.error || body);
    show("out-get-user", true, body);
  },

  async "create-user"(btn) {
    const enroll = $("create-enroll").value.trim();
    const name = $("create-name").value.trim();
    const privilege = parseInt($("create-privilege").value, 10);
    if (!enroll) return show("out-create-user", false, "Enter enroll number");
    show("out-create-user", true, "Creating...");
    const { ok, body } = await callJson("/api/user/create", { ...deviceOverrides(), enrollNumber: enroll, name, privilege });
    show("out-create-user", ok, ok ? body.message : (body.error || body));
  },

  async "update-user"(btn) {
    const enroll = $("update-enroll").value.trim();
    const name = $("update-name").value.trim();
    const privilege = parseInt($("update-privilege").value, 10);
    if (!enroll) return show("out-update-user", false, "Enter enroll number");
    show("out-update-user", true, "Updating...");
    const { ok, body } = await callJson("/api/user/update", { ...deviceOverrides(), enrollNumber: enroll, name: name || undefined, privilege });
    show("out-update-user", ok, ok ? body.message : (body.error || body));
  },

  async "enable-user"(btn) {
    const enroll = $("toggle-enroll").value.trim();
    if (!enroll) return show("out-toggle-user", false, "Enter enroll number");
    show("out-toggle-user", true, "Enabling...");
    const { ok, body } = await callJson("/api/user/enable", { ...deviceOverrides(), enrollNumber: enroll, enable: true });
    show("out-toggle-user", ok, ok ? body.message : (body.error || body));
  },

  async "disable-user"(btn) {
    const enroll = $("toggle-enroll").value.trim();
    if (!enroll) return show("out-toggle-user", false, "Enter enroll number");
    show("out-toggle-user", true, "Disabling...");
    const { ok, body } = await callJson("/api/user/enable", { ...deviceOverrides(), enrollNumber: enroll, enable: false });
    show("out-toggle-user", ok, ok ? body.message : (body.error || body));
  },

  async "delete-user"(btn) {
    const enroll = $("delete-enroll").value.trim();
    if (!enroll) return show("out-delete-user", false, "Enter enroll number");
    if (!confirm(`Delete user ${enroll}? This removes all enrolled data.`)) return;
    show("out-delete-user", true, "Deleting...");
    const { ok, body } = await callJson("/api/user/delete", { ...deviceOverrides(), enrollNumber: enroll });
    show("out-delete-user", ok, ok ? body.message : (body.error || body));
  },

  // Enrollment
  async "enroll-finger"(btn) {
    const enroll = $("enroll-fp-id").value.trim();
    const finger = parseInt($("enroll-fp-finger").value, 10);
    if (!enroll) return show("out-enroll-finger", false, "Enter enroll number");
    show("out-enroll-finger", true, "Starting enrollment...");
    const { ok, body } = await callJson("/api/enroll/finger", { ...deviceOverrides(), enrollNumber: enroll, fingerIndex: finger });
    show("out-enroll-finger", ok, ok ? body.message : (body.error || body));
  },

  async "enroll-face"(btn) {
    const enroll = $("enroll-face-id").value.trim();
    if (!enroll) return show("out-enroll-face", false, "Enter enroll number");
    show("out-enroll-face", true, "Starting enrollment...");
    const { ok, body } = await callJson("/api/enroll/face", { ...deviceOverrides(), enrollNumber: enroll });
    show("out-enroll-face", ok, ok ? body.message : (body.error || body));
  },

  // Templates
  async "get-finger-tpl"(btn) {
    const enroll = $("tpl-fp-get-enroll").value.trim();
    const finger = parseInt($("tpl-fp-get-finger").value, 10);
    if (!enroll) return show("out-get-finger-tpl", false, "Enter enroll number");
    show("out-get-finger-tpl", true, "Fetching...");
    const { ok, body } = await callJson("/api/template/finger", { ...deviceOverrides(), enrollNumber: enroll, fingerIndex: finger });
    if (!ok) return show("out-get-finger-tpl", false, body.error || body);
    if (!body.found) return show("out-get-finger-tpl", true, `No fingerprint template in slot ${finger} for user ${enroll}`);
    show("out-get-finger-tpl", true, body);
  },

  async "get-face-tpl"(btn) {
    const enroll = $("tpl-face-get-enroll").value.trim();
    const faceIndex = parseInt($("tpl-face-get-index").value, 10) || 50;
    if (!enroll) return show("out-get-face-tpl", false, "Enter enroll number");
    show("out-get-face-tpl", true, "Fetching...");
    const { ok, body } = await callJson("/api/template/face", { ...deviceOverrides(), enrollNumber: enroll, faceIndex });
    if (!ok) return show("out-get-face-tpl", false, body.error || body);
    if (!body.found) return show("out-get-face-tpl", true, `No face template in slot ${faceIndex} for user ${enroll}`);
    show("out-get-face-tpl", true, body);
  },

  async "upload-finger-tpl"(btn) {
    const enroll = $("tpl-fp-up-enroll").value.trim();
    const finger = parseInt($("tpl-fp-up-finger").value, 10);
    const template = $("tpl-fp-up-data").value.trim();
    if (!enroll) return show("out-upload-finger-tpl", false, "Enter enroll number");
    if (!template) return show("out-upload-finger-tpl", false, "Paste template data");
    show("out-upload-finger-tpl", true, "Uploading...");
    const { ok, body } = await callJson("/api/template/finger/upload", { ...deviceOverrides(), enrollNumber: enroll, fingerIndex: finger, template });
    show("out-upload-finger-tpl", ok, ok ? body.message : (body.error || body));
  },

  async "upload-face-tpl"(btn) {
    const enroll = $("tpl-face-up-enroll").value.trim();
    const faceIndex = parseInt($("tpl-face-up-index").value, 10) || 50;
    const template = $("tpl-face-up-data").value.trim();
    if (!enroll) return show("out-upload-face-tpl", false, "Enter enroll number");
    if (!template) return show("out-upload-face-tpl", false, "Paste template data");
    show("out-upload-face-tpl", true, "Uploading...");
    const { ok, body } = await callJson("/api/template/face/upload", { ...deviceOverrides(), enrollNumber: enroll, faceIndex, template });
    show("out-upload-face-tpl", ok, ok ? body.message : (body.error || body));
  },

  // Attendance
  async "att-all"(btn) {
    show("out-att-all", true, "Fetching all logs...");
    const { ok, body } = await callJson("/api/attendance/all", deviceOverrides());
    if (!ok) return show("out-att-all", false, body.error || body);
    renderAttLogs("out-att-all", body);
  },

  async "att-new"(btn) {
    show("out-att-new", true, "Fetching new logs...");
    const { ok, body } = await callJson("/api/attendance/new", deviceOverrides());
    if (!ok) return show("out-att-new", false, body.error || body);
    renderAttLogs("out-att-new", body);
  },

  async "att-range"(btn) {
    const startDate = fmtDt($("att-range-start").value);
    const endDate = fmtDt($("att-range-end").value);
    if (!startDate || !endDate) return show("out-att-range", false, "Select start and end dates");
    show("out-att-range", true, "Fetching...");
    const { ok, body } = await callJson("/api/attendance/range", { ...deviceOverrides(), startDate, endDate });
    if (!ok) return show("out-att-range", false, body.error || body);
    renderAttLogs("out-att-range", body);
  },

  async "att-admin"(btn) {
    show("out-att-admin", true, "Fetching admin logs...");
    const { ok, body } = await callJson("/api/attendance/admin", deviceOverrides());
    if (!ok) return show("out-att-admin", false, body.error || body);
    if (!body.logs.length) return show("out-att-admin", true, "No admin logs");
    const rows = body.logs.map(l =>
      `<tr><td>${l.admin}</td><td>${l.target}</td><td>${l.manipulation}</td><td>${l.timestamp}</td></tr>`
    ).join("");
    showHtml("out-att-admin", true, `<table>
      <thead><tr><th>Admin</th><th>Target</th><th>Action</th><th>Time</th></tr></thead>
      <tbody>${rows}</tbody>
    </table><p style="margin:8px 0 0;color:var(--muted)">Total: ${body.count} entries</p>`);
  },

  async "att-delete-range"(btn) {
    const startDate = fmtDt($("att-del-start").value);
    const endDate = fmtDt($("att-del-end").value);
    if (!startDate || !endDate) return show("out-att-delete-range", false, "Select start and end dates");
    if (!confirm(`Delete attendance logs from ${startDate} to ${endDate}?`)) return;
    show("out-att-delete-range", true, "Deleting...");
    const { ok, body } = await callJson("/api/attendance/delete-range", { ...deviceOverrides(), startDate, endDate });
    show("out-att-delete-range", ok, ok ? body.message : (body.error || body));
  },

  async "att-delete-before"(btn) {
    const before = fmtDt($("att-del-before").value);
    if (!before) return show("out-att-delete-before", false, "Select a date");
    if (!confirm(`Delete all attendance logs before ${before}?`)) return;
    show("out-att-delete-before", true, "Deleting...");
    const { ok, body } = await callJson("/api/attendance/delete-before", { ...deviceOverrides(), before });
    show("out-att-delete-before", ok, ok ? body.message : (body.error || body));
  },

  async "att-clear"(btn) {
    if (!confirm("Clear ALL attendance logs? This cannot be undone.")) return;
    show("out-att-clear", true, "Clearing...");
    const { ok, body } = await callJson("/api/attendance/clear", deviceOverrides());
    show("out-att-clear", ok, ok ? body.message : (body.error || body));
  },

  async "att-clear-admin"(btn) {
    if (!confirm("Clear ALL admin logs? This cannot be undone.")) return;
    show("out-att-clear", true, "Clearing...");
    const { ok, body } = await callJson("/api/attendance/clear-admin", deviceOverrides());
    show("out-att-clear", ok, ok ? body.message : (body.error || body));
  },

  // Device
  async "device-info"(btn) {
    show("out-device-info", true, "Fetching...");
    const { ok, body } = await callJson("/api/device/info", deviceOverrides());
    if (!ok) return show("out-device-info", false, body.error || body);
    const i = body.info;
    showHtml("out-device-info", true, `<dl class="info-grid">
      <dt>Serial</dt><dd>${i.serial}</dd>
      <dt>Product</dt><dd>${i.product}</dd>
      <dt>Firmware</dt><dd>${i.firmware}</dd>
      <dt>Platform</dt><dd>${i.platform}</dd>
      <dt>Vendor</dt><dd>${i.vendor}</dd>
      <dt>SDK Version</dt><dd>${i.sdkVersion}</dd>
      <dt>MAC</dt><dd>${i.mac}</dd>
      <dt>Users</dt><dd>${i.userCount} / ${i.userCapacity}</dd>
      <dt>Fingerprints</dt><dd>${i.fingerprintCount} / ${i.fingerprintCapacity}</dd>
      <dt>Faces</dt><dd>${i.faceCount} / ${i.faceCapacity}</dd>
      <dt>Att. Logs</dt><dd>${i.attLogCount} / ${i.attLogCapacity}</dd>
    </dl>`);
  },

  async "device-time"(btn) {
    show("out-device-time", true, "Fetching...");
    const { ok, body } = await callJson("/api/device/time", deviceOverrides());
    show("out-device-time", ok, ok ? `Device time: ${body.time}` : (body.error || body));
  },

  async "device-time-sync"(btn) {
    show("out-device-time", true, "Syncing...");
    const { ok, body } = await callJson("/api/device/time/set", deviceOverrides());
    show("out-device-time", ok, ok ? body.message : (body.error || body));
  },

  async "voice-test"(btn) {
    const index = parseInt($("voice-index").value, 10) || 0;
    show("out-voice-test", true, "Playing...");
    const { ok, body } = await callJson("/api/device/voice", { ...deviceOverrides(), index });
    show("out-voice-test", ok, ok ? body.message : (body.error || body));
  },

  async "door-lock"(btn) {
    show("out-door", true, "Locking...");
    const { ok, body } = await callJson("/api/device/door/lock", deviceOverrides());
    show("out-door", ok, ok ? body.message : (body.error || body));
  },

  async "door-unlock"(btn) {
    const seconds = parseInt($("door-seconds").value, 10) || 5;
    show("out-door", true, "Unlocking...");
    const { ok, body } = await callJson("/api/device/door/unlock", { ...deviceOverrides(), seconds });
    show("out-door", ok, ok ? body.message : (body.error || body));
  },
};

// ---- Event delegation ----

document.addEventListener("click", async (e) => {
  const btn = e.target.closest("button[data-action]");
  if (!btn) return;
  const action = btn.dataset.action;
  if (!actions[action]) return;
  btn.disabled = true;
  try { await actions[action](btn); }
  finally { btn.disabled = false; }
});

// Format datetime-local value to "yyyy-MM-dd HH:mm:ss"
function fmtDt(val) {
  if (!val) return null;
  return val.replace("T", " ") + (val.length === 16 ? ":00" : "");
}

const verifyNames = { 0: "Password", 1: "Fingerprint", 2: "Card", 3: "Face", 4: "Multi" };
const inOutNames = { 0: "Check-In", 1: "Check-Out", 2: "Break-Out", 3: "Break-In", 4: "OT-In", 5: "OT-Out" };

function renderAttLogs(elId, body) {
  if (!body.logs.length) return show(elId, true, "No logs found");
  const rows = body.logs.map(l =>
    `<tr><td>${l.userId}</td><td>${l.timestamp}</td><td>${verifyNames[l.verifyMethod] ?? l.verifyMethod}</td><td>${inOutNames[l.inOutState] ?? l.inOutState}</td><td>${l.workCode || ""}</td></tr>`
  ).join("");
  showHtml(elId, true, `<table>
    <thead><tr><th>User ID</th><th>Time</th><th>Verify</th><th>State</th><th>Work Code</th></tr></thead>
    <tbody>${rows}</tbody>
  </table><p style="margin:8px 0 0;color:var(--muted)">Total: ${body.count} records</p>`);
}

loadConfig();
