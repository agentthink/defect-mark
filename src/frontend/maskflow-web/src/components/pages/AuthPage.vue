<template>
  <main v-if="page === 'auth'" class="auth-redesign">
    <section class="auth-hero-panel">
      <a class="auth-brand" href="#" @click.prevent="go('/index.html')"><span class="logo-mark">M</span><strong>MaskFlow</strong></a>
      <div>
        <p class="home-badge">AI 自动标注平台</p>
        <h1>让标注、复核和导出都在一个工作台完成</h1>
        <p>登录后即可管理项目、上传图片、运行自动分割、复核 YOLO 标注，并把结果导出成训练可用的数据集。</p>
      </div>
      <div class="auth-preview-mini">
        <div><span></span><b>AI 自动分割</b><em>38 / 50 次</em></div>
        <div><span></span><b>项目图片</b><em>128 张</em></div>
        <div><span></span><b>导出格式</b><em>YOLO txt</em></div>
      </div>
    </section>
    <section class="auth-card">
      <div class="auth-card-head">
        <p>{{ authCardEyebrow }}</p>
        <h2>{{ authCardTitle }}</h2>
      </div>

      <div v-if="auth.mode === 'login' || auth.mode === 'register'" class="auth-switch" role="tablist" aria-label="登录注册切换">
        <button :class="{ active: auth.mode === 'login' }" type="button" @click="goLogin">登录</button>
        <button :class="{ active: auth.mode === 'register' }" type="button" @click="goRegister">注册</button>
      </div>

      <form v-if="auth.mode === 'login' || auth.mode === 'register'" class="auth-form" @submit.prevent="submitAuth">
        <div class="form-row"><label>邮箱</label><input v-model="auth.email" type="email" autocomplete="email" placeholder="name@example.com" required /></div>
        <div v-if="auth.mode === 'register'" class="form-row"><label>用户名</label><input v-model="auth.username" autocomplete="name" placeholder="MaskFlow User" /></div>
        <div class="form-row"><label>密码</label><input v-model="auth.password" type="password" :autocomplete="auth.mode === 'register' ? 'new-password' : 'current-password'" placeholder="请输入密码" required /></div>
        <div v-if="auth.mode === 'login'" class="auth-options">
          <label><input type="checkbox" /> 记住我</label>
          <a href="#" @click.prevent="openForgotPassword">忘记密码？</a>
        </div>
        <p v-if="message" class="form-error">{{ message }}</p>
        <button class="auth-submit" type="submit" :disabled="loading">{{ loading ? '处理中...' : (auth.mode === 'register' ? '注册并进入控制台' : '登录') }}</button>
      </form>

      <form v-else-if="auth.mode === 'forgot'" class="auth-form" @submit.prevent="submitForgotPassword">
        <p class="auth-help">输入注册邮箱，系统会生成重置码（当前部署为页面内回显，无需邮件）。</p>
        <div class="form-row"><label>邮箱</label><input v-model="auth.email" type="email" autocomplete="email" placeholder="name@example.com" required /></div>
        <p v-if="message" class="form-error">{{ message }}</p>
        <button class="auth-submit" type="submit" :disabled="loading">{{ loading ? '处理中...' : '获取重置码' }}</button>
        <p class="auth-footnote"><a href="#" @click.prevent="goLogin">返回登录</a></p>
      </form>

      <form v-else class="auth-form" @submit.prevent="submitResetPassword">
        <p class="auth-help">输入重置码和新密码，完成后即可用新密码登录。</p>
        <div class="form-row"><label>邮箱</label><input v-model="auth.email" type="email" autocomplete="email" placeholder="name@example.com" required /></div>
        <div class="form-row"><label>重置码</label><input v-model="auth.resetToken" autocomplete="one-time-code" placeholder="粘贴或输入重置码" required /></div>
        <div class="form-row"><label>新密码</label><input v-model="auth.password" type="password" autocomplete="new-password" placeholder="至少 8 位" required minlength="8" /></div>
        <div class="form-row"><label>确认新密码</label><input v-model="auth.confirmPassword" type="password" autocomplete="new-password" placeholder="再次输入新密码" required minlength="8" /></div>
        <p v-if="message" class="form-error">{{ message }}</p>
        <button class="auth-submit" type="submit" :disabled="loading">{{ loading ? '处理中...' : '重置密码' }}</button>
        <p class="auth-footnote"><a href="#" @click.prevent="openForgotPassword">重新获取重置码</a> · <a href="#" @click.prevent="goLogin">返回登录</a></p>
      </form>

      <p v-if="auth.mode === 'login' || auth.mode === 'register'" class="auth-footnote">
        {{ auth.mode === 'register' ? '已有账户？' : '还没有账户？' }}
        <a href="#" @click.prevent="auth.mode === 'register' ? goLogin() : goRegister()">
          {{ auth.mode === 'register' ? '去登录' : '免费注册' }}
        </a>
      </p>
    </section>
  </main>
</template>

<script setup>
import { computed, inject } from "vue";

const page = inject("page");
const auth = inject("auth");
const go = inject("go");
const message = inject("message");
const loading = inject("loading");
const submitAuth = inject("submitAuth");
const openForgotPassword = inject("openForgotPassword");
const submitForgotPassword = inject("submitForgotPassword");
const submitResetPassword = inject("submitResetPassword");

function goLogin() {
  auth.mode = "login";
  message.value = "";
}

function goRegister() {
  auth.mode = "register";
  message.value = "";
}

const authCardEyebrow = computed(() => {
  if (auth.mode === "register") return "创建账户";
  if (auth.mode === "forgot") return "找回密码";
  if (auth.mode === "reset") return "设置新密码";
  return "账户登录";
});

const authCardTitle = computed(() => {
  if (auth.mode === "register") return "开始使用 MaskFlow";
  if (auth.mode === "forgot") return "忘记密码";
  if (auth.mode === "reset") return "重置你的密码";
  return "欢迎回来";
});
</script>
