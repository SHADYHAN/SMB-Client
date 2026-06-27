import { invoke } from "@tauri-apps/api/core";
import "./styles.css";

type ShellState = {
  connected: boolean;
  serverName: string;
  serverHost: string;
  status: string;
};

const initialState: ShellState = {
  connected: false,
  serverName: "RYNAT 文件共享",
  serverHost: "",
  status: "未连接",
};

const app = document.querySelector<HTMLDivElement>("#app");

if (!app) {
  throw new Error("missing #app root");
}

function render(state: ShellState) {
  app.innerHTML = `
    <section class="shell">
      <header class="chrome">
        <div>
          <p class="eyebrow">Windows Explorer-first</p>
          <h1>RYNAT</h1>
        </div>
        <span class="status ${state.connected ? "is-connected" : ""}">${state.status}</span>
      </header>

      <section class="panel">
        <div class="server-block">
          <span class="label">当前服务器</span>
          <strong>${state.serverName}</strong>
          <span class="host">${state.serverHost || "登录后打开 Windows 资源管理器"}</span>
        </div>

        <form id="login-form" class="login-form">
          <label>
            <span>服务器地址</span>
            <input name="host" autocomplete="url" placeholder="192.168.102.136" />
          </label>
          <label>
            <span>用户名</span>
            <input name="username" autocomplete="username" placeholder="用户名" />
          </label>
          <label>
            <span>密码</span>
            <input name="password" type="password" autocomplete="current-password" placeholder="密码" />
          </label>
          <button type="submit">连接并打开资源管理器</button>
        </form>

        <div class="actions">
          <button id="open-explorer" type="button">打开资源管理器</button>
          <button id="copy-link-demo" type="button" class="secondary">测试 UNC 复制链接</button>
        </div>

        <output id="diagnostic-output" class="diagnostic" aria-live="polite"></output>
      </section>
    </section>
  `;

  document.querySelector<HTMLFormElement>("#login-form")?.addEventListener("submit", async (event) => {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const host = String(data.get("host") || "").trim();
    const username = String(data.get("username") || "").trim();
    const password = String(data.get("password") || "");
    await invoke("connect_profile", { host, username, password });
    setDiagnostic(`已建立连接请求：\\\\${host}`);
    render({
      connected: true,
      serverName: host,
      serverHost: `\\\\${host}`,
      status: "已连接",
    });
  });

  document.querySelector<HTMLButtonElement>("#open-explorer")?.addEventListener("click", async () => {
    const opened = await invoke<string>("open_explorer", { host: state.serverHost.replace(/^\\\\/, ""), share: null });
    setDiagnostic(`打开：${opened}`);
  });

  document.querySelector<HTMLButtonElement>("#copy-link-demo")?.addEventListener("click", async () => {
    const link = await invoke<string>("copy_link_for_unc_path", {
      path: "\\\\192.168.102.136\\共享资料\\123",
      kind: "dir",
    });
    setDiagnostic(`生成链接：${link}`);
  });
}

function setDiagnostic(message: string) {
  const output = document.querySelector<HTMLOutputElement>("#diagnostic-output");
  if (output) {
    output.value = message;
    output.textContent = message;
  }
}

async function boot() {
  try {
    const state = await invoke<ShellState>("get_bootstrap_state");
    render(state);
  } catch {
    render(initialState);
  }
}

boot();
