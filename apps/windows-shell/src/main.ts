import { invoke } from "@tauri-apps/api/core";
import "./styles.css";

type ShellState = {
  connected: boolean;
  serverName: string;
  serverHost: string;
  status: string;
};

type ExplorerOpenTarget = {
  host: string;
  share?: string;
  openPath: string;
};

const defaultServerHost = "192.168.102.136";
const defaultCopyLinkTestPath = "\\\\192.168.102.136\\临时文件夹\\123";

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
            <span>服务器或共享路径</span>
            <input name="host" autocomplete="url" value="${escapeHtml(state.serverHost || defaultServerHost)}" />
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
          <button id="copy-link-demo" type="button" class="secondary">复制测试分享链接</button>
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

    if (!host) {
      setDiagnostic("请输入服务器地址");
      return;
    }

    setDiagnostic("正在解析资源管理器路径...");

    try {
      const target = await invoke<ExplorerOpenTarget>("preview_explorer_path", { host, share: null });
      setDiagnostic(`正在打开资源管理器：${target.openPath}`);

      const opened = await invoke<string>("open_explorer", { host, share: null });
      let credentialWarning: string | null = null;

      try {
        await invoke("connect_profile", { host: target.openPath, username, password });
      } catch (error) {
        credentialWarning = formatError(error);
      }

      render({
        connected: credentialWarning === null,
        serverName: target.share ? `${target.host}\\${target.share}` : target.host,
        serverHost: target.openPath,
        status: credentialWarning ? "需确认凭据" : "已连接",
      });

      if (credentialWarning) {
        setDiagnostic(`已请求打开：${opened}。SMB 凭据接入失败：${credentialWarning}`);
      } else {
        setDiagnostic(`已请求打开：${opened}`);
      }
    } catch (error) {
      setDiagnostic(`打开资源管理器失败：${formatError(error)}`);
    }
  });

  document.querySelector<HTMLButtonElement>("#open-explorer")?.addEventListener("click", async () => {
    const host = state.serverHost;
    if (!host) {
      setDiagnostic("请先登录服务器");
      return;
    }

    try {
      const opened = await invoke<string>("open_explorer", { host, share: null });
      setDiagnostic(`已请求打开：${opened}`);
    } catch (error) {
      setDiagnostic(`打开资源管理器失败：${formatError(error)}`);
    }
  });

  document.querySelector<HTMLButtonElement>("#copy-link-demo")?.addEventListener("click", async () => {
    try {
      const path = copyLinkTestPath();
      const link = await invoke<string>("copy_link_for_unc_path", {
        path,
        kind: "dir",
      });
      const copied = await copyTextToClipboard(link);
      setDiagnostic(
        copied
          ? `已复制测试分享链接：${link}\n测试路径：${path}`
          : `已生成测试分享链接，剪贴板写入失败，可手动复制：${link}\n测试路径：${path}`,
      );
    } catch (error) {
      setDiagnostic(`生成链接失败：${formatError(error)}`);
    }
  });
}

function setDiagnostic(message: string) {
  const output = document.querySelector<HTMLOutputElement>("#diagnostic-output");
  if (output) {
    output.value = message;
    output.textContent = message;
  }
}

function formatError(error: unknown) {
  if (error instanceof Error) {
    return error.message;
  }
  return String(error || "未知错误");
}

function copyLinkTestPath() {
  return defaultCopyLinkTestPath;
}

async function copyTextToClipboard(text: string) {
  try {
    await navigator.clipboard?.writeText(text);
    return true;
  } catch {
    return copyTextWithTextarea(text);
  }
}

function copyTextWithTextarea(text: string) {
  const textarea = document.createElement("textarea");
  textarea.value = text;
  textarea.style.position = "fixed";
  textarea.style.left = "-9999px";
  document.body.appendChild(textarea);
  textarea.focus();
  textarea.select();

  try {
    return document.execCommand("copy");
  } catch {
    return false;
  } finally {
    textarea.remove();
  }
}

function escapeHtml(value: string) {
  return value.replace(/[&<>"']/g, (char) => {
    switch (char) {
      case "&":
        return "&amp;";
      case "<":
        return "&lt;";
      case ">":
        return "&gt;";
      case '"':
        return "&quot;";
      case "'":
        return "&#39;";
      default:
        return char;
    }
  });
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
