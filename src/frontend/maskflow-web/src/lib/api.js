const sessionKey = "maskflow.session";

export function session() {
  try {
    return JSON.parse(localStorage.getItem(sessionKey) || "null");
  } catch {
    return null;
  }
}

export function saveSession(value) {
  localStorage.setItem(sessionKey, JSON.stringify(value));
}

export function clearSession() {
  localStorage.removeItem(sessionKey);
}

export function user() {
  return session()?.user || null;
}

export function authHeaders() {
  const token = session()?.token;
  return token ? { Authorization: `Bearer ${token}` } : {};
}

export async function apiFetch(path, options = {}) {
  const headers = { ...(options.headers || {}), ...authHeaders() };
  if (options.body && !(options.body instanceof FormData)) {
    headers["Content-Type"] = "application/json";
    options.body = JSON.stringify(options.body);
  }
  const response = await fetch(path, { ...options, headers });
  const text = await response.text();
  let data = {};
  if (text) {
    try {
      data = JSON.parse(text);
    } catch {
      if (!response.ok) {
        throw new Error(text.split("\n")[0].slice(0, 200) || "请求失败");
      }
    }
  }
  if (!response.ok) {
    throw new Error(data.detail || data.title || text.split("\n")[0] || "请求失败");
  }
  return data;
}

export async function downloadAuthenticated(path, filename = "download") {
  const response = await fetch(path, { headers: authHeaders() });
  if (!response.ok) {
    const text = await response.text();
    let message = "下载失败";
    try {
      const data = JSON.parse(text);
      message = data.detail || data.title || message;
    } catch {
      if (text) message = text;
    }
    throw new Error(message);
  }
  const blob = await response.blob();
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = filename;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

export function formatBytes(bytes = 0) {
  if (bytes < 1024) return `${bytes} B`;
  const units = ["KB", "MB", "GB", "TB"];
  let value = bytes / 1024;
  let index = 0;
  while (value >= 1024 && index < units.length - 1) {
    value /= 1024;
    index += 1;
  }
  return `${value.toFixed(value >= 10 ? 1 : 2)} ${units[index]}`;
}

