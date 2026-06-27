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

type ShellTab = "server" | "links" | "activation" | "settings" | "shortcuts" | "about";

type AppState = ShellState & {
  activeTab: ShellTab;
  username: string;
  diagnostic: string;
  lastLink: string;
  autoOpenExplorer: boolean;
  launchAtLogin: boolean;
};

const defaultServerHost = "192.168.102.136";
const defaultCopyLinkTestPath = "\\\\192.168.102.136\\临时文件夹\\123";
const localRedirectUrl = "http://127.0.0.1:19527";
const contextIpcUrl = "127.0.0.1:19528";

const initialState: AppState = {
  connected: false,
  serverName: "RYNAT 文件共享",
  serverHost: "",
  status: "未连接",
  activeTab: "server",
  username: "",
  diagnostic: "客户端已启动，等待登录。",
  lastLink: "",
  autoOpenExplorer: true,
  launchAtLogin: false,
};

let currentState = { ...initialState };

const app = document.querySelector<HTMLDivElement>("#app");

if (!app) {
  throw new Error("missing #app root");
}

function render(nextState: AppState) {
  currentState = nextState;
  app.innerHTML = nextState.connected ? renderShellView(nextState) : renderLoginView(nextState);
  bindViewEvents();
}

function renderLoginView(state: AppState) {
  return `
    <main class="login-screen">
      <section class="login-card" aria-label="登录 RYNAT">
        <div class="login-brand">
          <div class="brand-mark">R</div>
          <div>
            <p class="eyebrow">Explorer-first Windows Client</p>
            <h1>RYNAT</h1>
          </div>
        </div>

        <div class="login-copy">
          <h2>登录共享服务器</h2>
          <p>登录后客户端会保持运行，负责 Explorer 会话、右键分享链接和链接唤醒服务。</p>
        </div>

        <form id="login-form" class="form-stack">
          <label>
            <span>服务器或共享路径</span>
            <input name="host" autocomplete="url" value="${escapeHtml(state.serverHost || defaultServerHost)}" />
          </label>
          <label>
            <span>用户名</span>
            <input name="username" autocomplete="username" value="${escapeHtml(state.username)}" placeholder="用户名" />
          </label>
          <label>
            <span>密码</span>
            <input name="password" type="password" autocomplete="current-password" placeholder="密码" />
          </label>
          <button type="submit" class="primary-action">登录并打开资源管理器</button>
        </form>

        <output id="diagnostic-output" class="diagnostic" aria-live="polite">${escapeHtml(state.diagnostic)}</output>
      </section>
    </main>
  `;
}

function renderShellView(state: AppState) {
  return `
    <main class="app-shell">
      <aside class="sidebar" aria-label="RYNAT 导航">
        <div class="sidebar-brand">
          <div class="brand-mark small">R</div>
          <div>
            <strong>RYNAT</strong>
            <span>${escapeHtml(state.status)}</span>
          </div>
        </div>

        <nav class="nav-list">
          ${renderNavButton("server", "服务器", "连接与 Explorer")}
          ${renderNavButton("links", "分享链接", "右键复制与测试")}
          ${renderNavButton("activation", "唤醒服务", "短链监听状态")}
          ${renderNavButton("settings", "设置", "启动和默认行为")}
          ${renderNavButton("shortcuts", "快捷键", "常用动作")}
          ${renderNavButton("about", "关于", "版本与诊断")}
        </nav>

        <div class="sidebar-footer">
          <span class="service-dot"></span>
          <span>监听服务运行中</span>
        </div>
      </aside>

      <section class="content">
        <header class="content-header">
          <div>
            <p class="eyebrow">Windows Explorer-first</p>
            <h1>${escapeHtml(tabTitle(state.activeTab))}</h1>
          </div>
          <span class="status is-connected">${escapeHtml(state.status)}</span>
        </header>

        ${renderPanel(state)}

        <output id="diagnostic-output" class="diagnostic docked" aria-live="polite">${escapeHtml(state.diagnostic)}</output>
      </section>
    </main>
  `;
}

function renderNavButton(tab: ShellTab, label: string, detail: string) {
  const active = currentState.activeTab === tab ? "is-active" : "";
  return `
    <button class="nav-item ${active}" type="button" data-tab="${tab}">
      <span>${label}</span>
      <small>${detail}</small>
    </button>
  `;
}

function renderPanel(state: AppState) {
  switch (state.activeTab) {
    case "links":
      return renderLinksPanel(state);
    case "activation":
      return renderActivationPanel();
    case "settings":
      return renderSettingsPanel(state);
    case "shortcuts":
      return renderShortcutsPanel();
    case "about":
      return renderAboutPanel(state);
    case "server":
    default:
      return renderServerPanel(state);
  }
}

function renderServerPanel(state: AppState) {
  return `
    <section class="panel-grid">
      <article class="panel-card span-2">
        <div class="panel-heading">
          <div>
            <span class="label">当前服务器</span>
            <h2>${escapeHtml(state.serverName)}</h2>
          </div>
          <span class="pill success">已连接</span>
        </div>
        <dl class="details">
          <div>
            <dt>Explorer 路径</dt>
            <dd>${escapeHtml(state.serverHost || "未解析")}</dd>
          </div>
          <div>
            <dt>用户名</dt>
            <dd>${escapeHtml(state.username || "未填写")}</dd>
          </div>
          <div>
            <dt>登录后动作</dt>
            <dd>${state.autoOpenExplorer ? "自动打开资源管理器" : "仅保持会话"}</dd>
          </div>
        </dl>
        <div class="button-row">
          <button id="open-explorer" type="button">打开资源管理器</button>
          <button id="reconnect" type="button" class="secondary">重新连接</button>
          <button id="logout" type="button" class="ghost">退出登录</button>
        </div>
      </article>

      <article class="panel-card">
        <span class="label">SMB 会话</span>
        <strong class="metric">Explorer 接管</strong>
        <p>文件浏览、复制、移动、删除、缩略图和系统右键菜单都由 Windows 资源管理器处理。</p>
      </article>

      <article class="panel-card">
        <span class="label">客户端职责</span>
        <strong class="metric">常驻监听</strong>
        <p>RYNAT 保持运行，用于复制分享链接、接收短链唤醒和打开对应目录。</p>
      </article>
    </section>
  `;
}

function renderLinksPanel(state: AppState) {
  return `
    <section class="panel-grid">
      <article class="panel-card span-2">
        <div class="panel-heading">
          <div>
            <span class="label">右键分享链接</span>
            <h2>复制 Explorer 选中项链接</h2>
          </div>
          <span class="pill">测试中</span>
        </div>
        <dl class="details">
          <div>
            <dt>测试路径</dt>
            <dd>${escapeHtml(defaultCopyLinkTestPath)}</dd>
          </div>
          <div>
            <dt>最近链接</dt>
            <dd>${escapeHtml(state.lastLink || "尚未生成")}</dd>
          </div>
        </dl>
        <div class="button-row">
          <button id="copy-link-demo" type="button">复制测试分享链接</button>
          <button id="open-last-link" type="button" class="secondary">打开最近链接</button>
        </div>
      </article>

      <article class="panel-card">
        <span class="label">上下文菜单</span>
        <strong class="metric">复制 RYNAT 分享链接</strong>
        <p>正式版会出现在文件和目录右键菜单中，当前构建先通过测试按钮验证链路。</p>
      </article>

      <article class="panel-card">
        <span class="label">链接类型</span>
        <strong class="metric">目录</strong>
        <p>当前测试路径按目录处理，点击后应打开对应 Explorer 目录。</p>
      </article>
    </section>
  `;
}

function renderActivationPanel() {
  return `
    <section class="panel-grid">
      <article class="panel-card span-2">
        <div class="panel-heading">
          <div>
            <span class="label">唤醒服务</span>
            <h2>客户端保持运行并监听短链</h2>
          </div>
          <span class="pill success">运行中</span>
        </div>
        <dl class="details">
          <div>
            <dt>短链服务</dt>
            <dd>${localRedirectUrl}/s/...</dd>
          </div>
          <div>
            <dt>右键 IPC</dt>
            <dd>${contextIpcUrl}</dd>
          </div>
          <div>
            <dt>协议</dt>
            <dd>rynat://s/... 解析后打开 Explorer</dd>
          </div>
        </dl>
      </article>

      <article class="panel-card">
        <span class="label">当前限制</span>
        <strong class="metric">需要客户端运行</strong>
        <p>本地短链依赖 127.0.0.1 服务；客户端未运行时，需要后续接入协议注册或公网中转页。</p>
      </article>

      <article class="panel-card">
        <span class="label">目标行为</span>
        <strong class="metric">打开目录</strong>
        <p>链接点击后解析目标 UNC，目录直接打开，文件则打开父目录并选中文件。</p>
      </article>
    </section>
  `;
}

function renderSettingsPanel(state: AppState) {
  return `
    <section class="panel-grid">
      <article class="panel-card span-2">
        <div class="panel-heading">
          <div>
            <span class="label">常规设置</span>
            <h2>默认行为</h2>
          </div>
        </div>
        <div class="setting-list">
          <label class="setting-row">
            <input id="auto-open-toggle" type="checkbox" ${state.autoOpenExplorer ? "checked" : ""} />
            <span>
              <strong>登录后自动打开资源管理器</strong>
              <small>连接成功后立即打开当前服务器 UNC 路径。</small>
            </span>
          </label>
          <label class="setting-row">
            <input id="launch-login-toggle" type="checkbox" ${state.launchAtLogin ? "checked" : ""} />
            <span>
              <strong>开机启动</strong>
              <small>后续接入系统启动项，目前仅保留设置入口。</small>
            </span>
          </label>
        </div>
      </article>

      <article class="panel-card">
        <span class="label">默认服务器</span>
        <strong class="metric">${defaultServerHost}</strong>
        <p>登录页默认填入这个地址，后续可以持久化为用户配置。</p>
      </article>

      <article class="panel-card">
        <span class="label">窗口模式</span>
        <strong class="metric">常驻</strong>
        <p>关闭窗口和托盘行为后续再接入，主进程需要持续监听链接服务。</p>
      </article>
    </section>
  `;
}

function renderShortcutsPanel() {
  return `
    <section class="panel-grid">
      <article class="panel-card span-2">
        <div class="panel-heading">
          <div>
            <span class="label">快捷键</span>
            <h2>预留常用动作</h2>
          </div>
          <span class="pill">规划中</span>
        </div>
        <div class="shortcut-list">
          <div><kbd>Ctrl</kbd><kbd>O</kbd><span>打开资源管理器</span></div>
          <div><kbd>Ctrl</kbd><kbd>L</kbd><span>复制当前测试分享链接</span></div>
          <div><kbd>Ctrl</kbd><kbd>,</kbd><span>打开设置</span></div>
        </div>
      </article>

      <article class="panel-card span-2">
        <span class="label">说明</span>
        <p>第一版先展示入口，不抢系统快捷键；等托盘和右键菜单稳定后再启用全局快捷键。</p>
      </article>
    </section>
  `;
}

function renderAboutPanel(state: AppState) {
  return `
    <section class="panel-grid">
      <article class="panel-card span-2">
        <div class="panel-heading">
          <div>
            <span class="label">关于</span>
            <h2>RYNAT Windows Shell</h2>
          </div>
          <span class="pill">Explorer-first</span>
        </div>
        <dl class="details">
          <div>
            <dt>产品形态</dt>
            <dd>轻量客户端 + Windows 资源管理器</dd>
          </div>
          <div>
            <dt>当前连接</dt>
            <dd>${escapeHtml(state.serverHost || "未连接")}</dd>
          </div>
          <div>
            <dt>测试路径</dt>
            <dd>${escapeHtml(defaultCopyLinkTestPath)}</dd>
          </div>
        </dl>
      </article>

      <article class="panel-card">
        <span class="label">构建线</span>
        <strong class="metric">Tauri</strong>
        <p>负责轻量 UI、SMB 会话、右键 IPC 和短链监听。</p>
      </article>

      <article class="panel-card">
        <span class="label">文件能力</span>
        <strong class="metric">Explorer</strong>
        <p>本机资源管理器负责完整文件体验，客户端不复刻文件管理器。</p>
      </article>
    </section>
  `;
}

function bindViewEvents() {
  document.querySelector<HTMLFormElement>("#login-form")?.addEventListener("submit", handleLoginSubmit);

  document.querySelectorAll<HTMLButtonElement>("[data-tab]").forEach((button) => {
    button.addEventListener("click", () => {
      const tab = button.dataset.tab as ShellTab;
      render({ ...currentState, activeTab: tab });
    });
  });

  document.querySelector<HTMLButtonElement>("#open-explorer")?.addEventListener("click", handleOpenExplorer);
  document.querySelector<HTMLButtonElement>("#reconnect")?.addEventListener("click", handleReconnect);
  document.querySelector<HTMLButtonElement>("#logout")?.addEventListener("click", handleLogout);
  document.querySelector<HTMLButtonElement>("#copy-link-demo")?.addEventListener("click", handleCopyLinkDemo);
  document.querySelector<HTMLButtonElement>("#open-last-link")?.addEventListener("click", handleOpenLastLink);
  document.querySelector<HTMLInputElement>("#auto-open-toggle")?.addEventListener("change", (event) => {
    const checked = (event.currentTarget as HTMLInputElement).checked;
    render({ ...currentState, autoOpenExplorer: checked, diagnostic: checked ? "已设置登录后自动打开资源管理器。" : "已设置登录后仅保持客户端运行。" });
  });
  document.querySelector<HTMLInputElement>("#launch-login-toggle")?.addEventListener("change", (event) => {
    const checked = (event.currentTarget as HTMLInputElement).checked;
    render({ ...currentState, launchAtLogin: checked, diagnostic: checked ? "开机启动入口已打开，系统注册后生效。" : "开机启动入口已关闭。" });
  });
}

async function handleLoginSubmit(event: SubmitEvent) {
  event.preventDefault();
  const form = event.currentTarget as HTMLFormElement;
  const data = new FormData(form);
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
    let opened = "";
    if (currentState.autoOpenExplorer) {
      setDiagnostic(`正在打开资源管理器：${target.openPath}`);
      opened = await invoke<string>("open_explorer", { host, share: null });
    }

    let credentialWarning: string | null = null;
    try {
      await invoke("connect_profile", { host: target.openPath, username, password });
    } catch (error) {
      credentialWarning = formatError(error);
    }

    const connectedState: AppState = {
      ...currentState,
      connected: true,
      serverName: target.share ? `${target.host}\\${target.share}` : target.host,
      serverHost: target.openPath,
      username,
      status: credentialWarning ? "需确认凭据" : "已连接",
      activeTab: "server",
      diagnostic: credentialWarning
        ? `${opened ? `已请求打开：${opened}。` : ""}SMB 凭据接入失败：${credentialWarning}`
        : opened
          ? `已请求打开：${opened}`
          : `已连接：${target.openPath}`,
    };
    render(connectedState);
  } catch (error) {
    setDiagnostic(`登录失败：${formatError(error)}`);
  }
}

async function handleOpenExplorer() {
  const host = currentState.serverHost;
  if (!host) {
    setDiagnostic("请先登录服务器");
    return;
  }

  try {
    const opened = await invoke<string>("open_explorer", { host, share: null });
    render({ ...currentState, diagnostic: `已请求打开：${opened}` });
  } catch (error) {
    setDiagnostic(`打开资源管理器失败：${formatError(error)}`);
  }
}

async function handleReconnect() {
  if (!currentState.serverHost) {
    render({ ...currentState, connected: false, diagnostic: "请重新登录服务器。" });
    return;
  }

  try {
    const opened = await invoke<string>("open_explorer", { host: currentState.serverHost, share: null });
    render({ ...currentState, diagnostic: `已重新请求打开：${opened}` });
  } catch (error) {
    setDiagnostic(`重新连接失败：${formatError(error)}`);
  }
}

function handleLogout() {
  render({
    ...initialState,
    username: currentState.username,
    diagnostic: "已退出当前会话。",
  });
}

async function handleCopyLinkDemo() {
  try {
    const path = copyLinkTestPath();
    const link = await invoke<string>("copy_link_for_unc_path", {
      path,
      kind: "dir",
    });
    const copied = await copyTextToClipboard(link);
    render({
      ...currentState,
      activeTab: "links",
      lastLink: link,
      diagnostic: copied
        ? `已复制测试分享链接：${link}\n测试路径：${path}`
        : `已生成测试分享链接，剪贴板写入失败，可手动复制：${link}\n测试路径：${path}`,
    });
  } catch (error) {
    setDiagnostic(`生成链接失败：${formatError(error)}`);
  }
}

function handleOpenLastLink() {
  if (!currentState.lastLink) {
    render({ ...currentState, diagnostic: "尚未生成分享链接。" });
    return;
  }
  window.open(currentState.lastLink, "_blank", "noopener,noreferrer");
  render({ ...currentState, diagnostic: `已在浏览器打开最近链接：${currentState.lastLink}` });
}

function setDiagnostic(message: string) {
  const output = document.querySelector<HTMLOutputElement>("#diagnostic-output");
  if (output) {
    output.value = message;
    output.textContent = message;
  }
  currentState = { ...currentState, diagnostic: message };
}

function tabTitle(tab: ShellTab) {
  switch (tab) {
    case "links":
      return "分享链接";
    case "activation":
      return "唤醒服务";
    case "settings":
      return "设置";
    case "shortcuts":
      return "快捷键";
    case "about":
      return "关于";
    case "server":
    default:
      return "服务器";
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
    render({ ...initialState, ...state });
  } catch {
    render(initialState);
  }
}

boot();
