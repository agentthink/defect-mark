const MaskFlowAuth = (() => {
  const TOKEN_KEY = "maskflow.token";
  const USER_KEY = "maskflow.user";

  function token() {
    return localStorage.getItem(TOKEN_KEY) || "";
  }

  function user() {
    const saved = localStorage.getItem(USER_KEY);
    if (!saved) return null;
    try {
      return JSON.parse(saved);
    } catch (error) {
      return null;
    }
  }

  function saveSession(data) {
    localStorage.setItem(TOKEN_KEY, data.token);
    localStorage.setItem(USER_KEY, JSON.stringify(data.user));
  }

  function logout() {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(USER_KEY);
  }

  function authHeaders(extra = {}) {
    const currentToken = token();
    return currentToken ? { ...extra, Authorization: `Bearer ${currentToken}` } : { ...extra };
  }

  function formatBytes(bytes) {
    if (bytes >= 1024 ** 3) return `${(bytes / 1024 ** 3).toFixed(2)} GB`;
    if (bytes >= 1024 ** 2) return `${(bytes / 1024 ** 2).toFixed(1)} MB`;
    return `${Math.max(0, bytes)} B`;
  }

  async function refresh() {
    const currentToken = token();
    if (!currentToken) return null;
    const response = await fetch("/api/me", {
      headers: { Authorization: `Bearer ${currentToken}` },
    });
    if (!response.ok) {
      logout();
      return null;
    }
    const data = await response.json();
    localStorage.setItem(USER_KEY, JSON.stringify(data.user));
    return data.user;
  }

  async function apiFetch(url, options = {}) {
    const headers = authHeaders(options.headers || {});
    const response = await fetch(url, { ...options, headers });
    const data = await response.json().catch(() => ({}));
    if (!response.ok) {
      throw new Error(data.detail || "请求失败");
    }
    return data;
  }

  async function renderAccountStrip(titleId, metaId) {
    const title = document.querySelector(`#${titleId}`);
    const meta = document.querySelector(`#${metaId}`);
    if (!title || !meta) return;

    let current = user();
    try {
      current = (await refresh()) || current;
    } catch (error) {
      current = user();
    }

    if (!current) {
      title.textContent = "未登录";
      meta.textContent = "登录后可使用 1GB 免费云端空间";
      return;
    }

    title.textContent = current.email;
    meta.textContent = `${current.plan} · 已用 ${formatBytes(current.usedBytes)} / ${formatBytes(current.quotaBytes)}`;
  }

  async function requireLogin() {
    const current = await refresh();
    if (!current) {
      location.href = "/index.html";
      return null;
    }
    return current;
  }

  async function redirectIfLoggedIn() {
    const current = await refresh();
    if (current) location.href = "/dashboard.html";
  }

  const navItems = [
    { href: "/dashboard.html", key: "dashboard", icon: "⌂", label: "控制台" },
    { href: "#", key: "projects", icon: "▱", label: "项目管理" },
    { href: "/files.html", key: "upload", icon: "☁", label: "上传图片" },
    { href: "/segment.html", key: "segment", icon: "⌘", label: "SAM 分割" },
    { href: "/annotate.html", key: "annotate", icon: "□", label: "YOLO 标注" },
    { href: "/export.html", key: "export", icon: "▤", label: "数据集导出" },
    { href: "/files.html", key: "files", icon: "▱", label: "文件管理" },
    { href: "/records.html", key: "records", icon: "▦", label: "处理记录" },
    { href: "/billing.html", key: "billing", icon: "▧", label: "账单套餐" },
    { href: "/settings.html", key: "settings", icon: "⚙", label: "账户设置" },
  ];

  function activeKeyFromPath(pathname) {
    if (pathname.includes("dashboard")) return "dashboard";
    if (pathname.includes("segment")) return "segment";
    if (pathname.includes("annotate")) return "annotate";
    if (pathname.includes("export")) return "export";
    if (pathname.includes("files")) return "files";
    if (pathname.includes("records")) return "records";
    if (pathname.includes("billing")) return "billing";
    if (pathname.includes("settings")) return "settings";
    return "";
  }

  function buildSidebar(activeKey) {
    const aside = document.createElement("aside");
    aside.className = "mf-sidebar";
    aside.innerHTML = `<nav>${navItems
      .map((item) => {
        const active = item.key === activeKey ? " active" : "";
        return `<a class="${active.trim()}" href="${item.href}"><span class="nav-icon">${item.icon}</span>${item.label}</a>`;
      })
      .join("")}</nav>`;
    return aside;
  }

  function ensureAppSidebar() {
    const body = document.body;
    if (!body || !body.classList.contains("mf-app")) return;
    if (document.querySelector(".side-shell")) return;

    const main = document.querySelector("main");
    if (!main) return;

    const shell = document.createElement("div");
    shell.className = "side-shell";
    const content = document.createElement("div");
    content.className = "side-content";
    const activeKey = activeKeyFromPath(location.pathname);

    main.parentNode.insertBefore(shell, main);
    shell.appendChild(buildSidebar(activeKey));
    shell.appendChild(content);
    content.appendChild(main);
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", ensureAppSidebar);
  } else {
    ensureAppSidebar();
  }

  return {
    token,
    user,
    authHeaders,
    apiFetch,
    saveSession,
    logout,
    formatBytes,
    refresh,
    renderAccountStrip,
    requireLogin,
    redirectIfLoggedIn,
    ensureAppSidebar,
  };
})();
