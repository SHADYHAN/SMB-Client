const state = {
  connected: false,
  serverHost: "192.168.102.136",
  username: "",
  rememberPassword: true,
  status: "加载中",
  smbSessionStatus: "尚未连接 Windows SMB 会话",
  localRedirectRunning: false,
  localRedirectStatus: "本地短链服务尚未启动",
  contextIpcRunning: false,
  contextIpcStatus: "右键 IPC 服务尚未启动",
  lastActivation: "暂无"
};

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
  render();
}

function render() {
  const loginView = document.querySelector("#login-view");
  const dashboardView = document.querySelector("#dashboard-view");
  const title = document.querySelector("#page-title");
  loginView.classList.toggle("hidden", state.connected);
  dashboardView.classList.toggle("hidden", !state.connected);
  title.textContent = state.connected ? "服务器状态" : "连接服务器";

  document.querySelector("#server-host").value = state.serverHost || "192.168.102.136";
  document.querySelector("#username").value = state.username || "";
  document.querySelector("#remember-password").checked = Boolean(state.rememberPassword);

  document.querySelector("#connected-summary").textContent = state.status || "服务器状态正常。";
  document.querySelector("#state-host").textContent = state.serverHost || "-";
  document.querySelector("#state-user").textContent = state.username || "-";
  document.querySelector("#state-smb-session").textContent = state.smbSessionStatus || "-";
  document.querySelector("#state-redirect").textContent = state.localRedirectStatus || "-";
  document.querySelector("#state-context").textContent = state.contextIpcStatus || "-";
  document.querySelector("#status-text").textContent = state.status || "-";
  document.querySelector("#last-activation").textContent = state.lastActivation || "暂无";

  const servicesOk = Boolean(state.localRedirectRunning && state.contextIpcRunning);
  document.querySelector("#service-dot").classList.toggle("ok", servicesOk);
  document.querySelector("#service-label").textContent = servicesOk ? "监听服务运行中" : "监听服务需检查";
}

function showError(error) {
  state.status = error.message || String(error);
  render();
}

document.querySelector("#login-form").addEventListener("submit", async (event) => {
  event.preventDefault();
  try {
    const result = await send("connect", {
      serverHost: document.querySelector("#server-host").value,
      username: document.querySelector("#username").value,
      password: document.querySelector("#password").value,
      rememberPassword: document.querySelector("#remember-password").checked
    });
    document.querySelector("#password").value = "";
    applyState(result);
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

document.querySelector("#copy-test-link").addEventListener("click", async () => {
  try {
    const result = await send("copyTestLink");
    applyState(result);
  } catch (error) {
    showError(error);
  }
});

document.querySelector("#copy-material-test-link").addEventListener("click", async () => {
  try {
    const result = await send("copyMaterialTestLink");
    applyState(result);
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

document.querySelector("#hide-button").addEventListener("click", async () => {
  try {
    await send("hideWindow");
  } catch (error) {
    showError(error);
  }
});

send("getState").then(applyState).catch(showError);
