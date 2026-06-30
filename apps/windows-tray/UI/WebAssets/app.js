const storedPasswordPlaceholder = "********";

const state = {
  connected: false,
  serverHost: "192.168.102.136",
  serverName: "默认服务器",
  defaultServerId: "",
  servers: [],
  username: "",
  rememberPassword: true,
  autoLogin: false,
  hasStoredPassword: false,
  general: {
    startWithWindows: true,
    copyLinkHotkeyEnabled: true,
    copyLinkHotkey: "Ctrl + Shift + L"
  },
  status: "加载中",
  smbSessionStatus: "尚未连接共享网盘会话",
  localRedirectRunning: false,
  localRedirectStatus: "本地短链服务尚未启动",
  contextIpcRunning: false,
  contextIpcStatus: "右键 IPC 服务尚未启动",
  lastActivation: "暂无"
};

let activePage = "server";
let editingServerId = "";
let addingServer = false;
let nextId = 1;
const pending = new Map();

function send(command, payload = {}) {
  const id = String(nextId++);
  chrome.webview.postMessage({ id, command, ...payload });
  return new Promise((resolve, reject) => {
    pending.set(id, { resolve, reject });
  });
}

window.chrome.webview.addEventListener("message", (event) => {
  const { id, payload } = event.data || {};
  if (id === "state") {
    applyState(payload);
    return;
  }

  if (!id || !pending.has(id)) {
    return;
  }

  const request = pending.get(id);
  pending.delete(id);
  if (payload && payload.error) {
    request.reject(new Error(payload.error));
    return;
  }

  request.resolve(payload);
});

function applyState(nextState) {
  const source = nextState && nextState.state ? nextState.state : nextState;
  if (!source) {
    return;
  }

  Object.assign(state, source);
  state.servers = Array.isArray(state.servers) ? state.servers : [];
  state.general = state.general || {};
  editingServerId = editingServerId || state.defaultServerId || state.servers[0]?.id || "";
  render();
}

function render() {
  document.querySelector("#login-screen").classList.toggle("hidden", state.connected);
  document.querySelector("#app-screen").classList.toggle("hidden", !state.connected);

  renderLogin();
  renderShell();
  renderServerPage();
  renderGeneralPage();
  renderServiceStatus();
}

function renderLogin() {
  document.querySelector("#username").value = state.username || "";
  document.querySelector("#password").value = state.hasStoredPassword ? storedPasswordPlaceholder : "";
  document.querySelector("#remember-password").checked = Boolean(state.rememberPassword || state.autoLogin);
  document.querySelector("#auto-login").checked = Boolean(state.autoLogin);
  document.querySelector("#login-hint").textContent = state.status || "";
}

function renderShell() {
  document.querySelector("#page-title").textContent = activePage === "server" ? "服务器" : "通用";
  document.querySelectorAll(".nav-item").forEach((item) => {
    item.classList.toggle("active", item.dataset.page === activePage);
  });
  document.querySelector("#server-page").classList.toggle("hidden", activePage !== "server");
  document.querySelector("#general-page").classList.toggle("hidden", activePage !== "general");
  document.querySelector("#connected-summary").textContent = state.status || "服务器状态正常。";
  document.querySelector("#state-host").textContent = state.serverHost || "-";
  document.querySelector("#state-user").textContent = state.username || "-";
  document.querySelector("#state-smb-session").textContent = state.smbSessionStatus || "-";
  document.querySelector("#state-redirect").textContent = state.localRedirectStatus || "-";
  document.querySelector("#state-context").textContent = state.contextIpcStatus || "-";
}

function renderServerPage() {
  const list = document.querySelector("#server-list");
  list.innerHTML = "";
  state.servers.forEach((server) => {
    const item = document.createElement("button");
    item.type = "button";
    item.className = "server-item";
    item.classList.toggle("active", server.id === editingServerId);
    item.innerHTML = `
      <span>
        <strong>${escapeHtml(server.name || "共享网盘")}</strong>
        <small>${escapeHtml(server.host || "-")}</small>
      </span>
      ${server.id === state.defaultServerId ? '<em>默认</em>' : ""}
    `;
    item.addEventListener("click", () => {
      editingServerId = server.id;
      renderServerPage();
    });
    list.appendChild(item);
  });

  const selected = selectedServer();
  document.querySelector("#server-form-title").textContent = "编辑服务器";
  document.querySelector("#server-id").value = selected?.id || "";
  document.querySelector("#server-name").value = selected?.name || "";
  document.querySelector("#server-host").value = selected?.host || "";
  document.querySelector("#server-set-default").checked = selected?.id === state.defaultServerId;
  document.querySelector("#delete-server").disabled = !selected?.id || state.servers.length <= 1;
  document.querySelector("#server-form").classList.toggle("inactive", !selected?.id);
  document.querySelector("#new-server-form").classList.toggle("hidden", !addingServer);
}

function renderGeneralPage() {
  document.querySelector("#start-with-windows").checked = Boolean(state.general.startWithWindows);
  document.querySelector("#copy-link-hotkey-enabled").checked = state.general.copyLinkHotkeyEnabled !== false;
  document.querySelector("#copy-link-hotkey").textContent = state.general.copyLinkHotkey || "Ctrl + Shift + L";
}

function renderServiceStatus() {
  const servicesOk = Boolean(state.localRedirectRunning && state.contextIpcRunning);
  document.querySelector("#service-dot").classList.toggle("ok", servicesOk);
  document.querySelector("#service-label").textContent = servicesOk ? "监听服务运行中" : "监听服务需检查";
}

function selectedServer() {
  return state.servers.find((server) => server.id === editingServerId)
    || state.servers.find((server) => server.id === state.defaultServerId)
    || state.servers[0]
    || null;
}

function showError(error) {
  state.status = error.message || String(error);
  render();
}

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

document.querySelectorAll(".nav-item").forEach((item) => {
  item.addEventListener("click", () => {
    activePage = item.dataset.page || "server";
    render();
  });
});

document.querySelector("#auto-login").addEventListener("change", (event) => {
  if (event.target.checked) {
    document.querySelector("#remember-password").checked = true;
  }
});

document.querySelector("#remember-password").addEventListener("change", (event) => {
  if (!event.target.checked) {
    document.querySelector("#auto-login").checked = false;
  }
});

document.querySelector("#login-form").addEventListener("submit", async (event) => {
  event.preventDefault();
  try {
    const passwordField = document.querySelector("#password");
    const password = passwordField.value === storedPasswordPlaceholder ? "" : passwordField.value;
    const result = await send("connect", {
      username: document.querySelector("#username").value,
      password,
      rememberPassword: document.querySelector("#remember-password").checked,
      autoLogin: document.querySelector("#auto-login").checked
    });
    passwordField.value = "";
    applyState(result);
  } catch (error) {
    showError(error);
  }
});

document.querySelector("#add-server").addEventListener("click", () => {
  addingServer = true;
  renderServerPage();
  document.querySelector("#new-server-host").focus();
});

document.querySelector("#cancel-new-server").addEventListener("click", () => {
  addingServer = false;
  document.querySelector("#new-server-host").value = "";
  renderServerPage();
});

document.querySelector("#new-server-form").addEventListener("submit", async (event) => {
  event.preventDefault();
  try {
    const host = document.querySelector("#new-server-host").value.trim();
    if (!host) {
      document.querySelector("#new-server-host").focus();
      return;
    }

    const result = await send("saveServer", {
      setDefault: false,
      server: {
        id: "",
        name: host,
        host
      }
    });
    document.querySelector("#new-server-host").value = "";
    addingServer = false;
    applyState(result);
    editingServerId = result.savedServerId || state.defaultServerId || state.servers[0]?.id || "";
    render();
  } catch (error) {
    showError(error);
  }
});

document.querySelector("#server-form").addEventListener("submit", async (event) => {
  event.preventDefault();
  try {
    const result = await send("saveServer", {
      setDefault: document.querySelector("#server-set-default").checked,
      server: {
        id: document.querySelector("#server-id").value,
        name: document.querySelector("#server-name").value,
        host: document.querySelector("#server-host").value
      }
    });
    applyState(result);
    editingServerId = result.savedServerId || state.defaultServerId || state.servers[0]?.id || "";
    render();
  } catch (error) {
    showError(error);
  }
});

document.querySelector("#delete-server").addEventListener("click", async () => {
  const serverId = document.querySelector("#server-id").value;
  if (!serverId) {
    return;
  }

  try {
    applyState(await send("deleteServer", { serverId }));
    editingServerId = state.defaultServerId || state.servers[0]?.id || "";
    render();
  } catch (error) {
    showError(error);
  }
});

document.querySelector("#general-form").addEventListener("submit", async (event) => {
  event.preventDefault();
  try {
    applyState(await send("saveGeneralSettings", {
      general: {
        startWithWindows: document.querySelector("#start-with-windows").checked,
        copyLinkHotkeyEnabled: document.querySelector("#copy-link-hotkey-enabled").checked,
        copyLinkHotkey: "Ctrl + Shift + L"
      }
    }));
  } catch (error) {
    showError(error);
  }
});

document.querySelector("#open-explorer").addEventListener("click", async () => {
  try {
    applyState(await send("openExplorer"));
  } catch (error) {
    showError(error);
  }
});

document.querySelector("#disconnect").addEventListener("click", async () => {
  try {
    applyState(await send("disconnect"));
  } catch (error) {
    showError(error);
  }
});

send("getState").then(applyState).catch(showError);
