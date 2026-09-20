const loginForm = document.querySelector("#loginForm");
const registerForm = document.querySelector("#registerForm");
const loginMessage = document.querySelector("#loginMessage");
const registerMessage = document.querySelector("#registerMessage");
const showRegisterButton = document.querySelector("#showRegisterButton");
const showLoginButton = document.querySelector("#showLoginButton");
const authFormPanel = document.querySelector(".auth-form-panel");

function formData(form) {
  return new FormData(form);
}

function setAuthMode(mode) {
  const isRegister = mode === "register";
  loginForm.hidden = isRegister;
  registerForm.hidden = !isRegister;
  authFormPanel.dataset.authMode = isRegister ? "register" : "login";
  loginMessage.textContent = "";
  registerMessage.textContent = "";
  const nextUrl = new URL(location.href);
  nextUrl.searchParams.set("mode", isRegister ? "register" : "login");
  history.replaceState(null, "", nextUrl);
}

async function submitAuth(url, form, message) {
  message.textContent = "处理中...";
  try {
    const response = await fetch(url, {
      method: "POST",
      body: formData(form),
    });
    const data = await response.json();
    if (!response.ok) {
      throw new Error(data.detail || "请求失败");
    }
    MaskFlowAuth.saveSession(data);
    message.textContent = "成功";
    location.href = "/dashboard.html";
  } catch (error) {
    message.textContent = error.message;
  }
}

loginForm.addEventListener("submit", (event) => {
  event.preventDefault();
  submitAuth("/api/auth/login", loginForm, loginMessage);
});

registerForm.addEventListener("submit", (event) => {
  event.preventDefault();
  submitAuth("/api/auth/register", registerForm, registerMessage);
});

showRegisterButton.addEventListener("click", () => setAuthMode("register"));
showLoginButton.addEventListener("click", () => setAuthMode("login"));

const initialMode = new URLSearchParams(location.search).get("mode");
setAuthMode(initialMode === "register" ? "register" : "login");
MaskFlowAuth.redirectIfLoggedIn();
