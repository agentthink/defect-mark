<script setup>
import { useMaskFlowApp } from "./composables/useMaskFlowApp";
import HomePage from "./components/pages/HomePage.vue";
import AuthPage from "./components/pages/AuthPage.vue";
import AnnotatePage from "./components/pages/AnnotatePage.vue";
import DashboardPage from "./components/pages/DashboardPage.vue";
import SegmentPage from "./components/pages/SegmentPage.vue";
import FilesPage from "./components/pages/FilesPage.vue";
import RecordsPage from "./components/pages/RecordsPage.vue";
import ExportPage from "./components/pages/ExportPage.vue";
import BillingPage from "./components/pages/BillingPage.vue";
import SettingsPage from "./components/pages/SettingsPage.vue";

const {
  uploadDuplicatePrompt,
  resolveDuplicateUpload,
  page,
  auth,
  go,
  account,
  logout
} = useMaskFlowApp();
</script>

<template>
  <div v-if="uploadDuplicatePrompt.visible" class="upload-duplicate-mask" @click.self="resolveDuplicateUpload('cancel')">
    <section class="upload-duplicate-dialog" role="dialog" aria-modal="true" aria-labelledby="upload-duplicate-title">
      <h3 id="upload-duplicate-title">发现重复文件</h3>
      <p>当前项目已有 {{ uploadDuplicatePrompt.duplicateNames.length }} 个同名文件。你可以选择跳过重复项，或继续上传全部文件。</p>
      <ul class="upload-duplicate-list">
        <li v-for="name in uploadDuplicatePrompt.duplicateNames" :key="name">{{ name }}</li>
      </ul>
      <div class="upload-duplicate-actions">
        <button class="btn" type="button" :disabled="uploadDuplicatePrompt.newCount === 0" @click="resolveDuplicateUpload('skip')">跳过重复，上传其余 {{ uploadDuplicatePrompt.newCount }} 个</button>
        <button class="btn secondary" type="button" @click="resolveDuplicateUpload('all')">仍然上传全部</button>
        <button class="btn ghost" type="button" @click="resolveDuplicateUpload('cancel')">取消上传</button>
      </div>
    </section>
  </div>

  <header v-if="page === 'home' || page === 'auth'" class="marketing-nav">
    <a class="marketing-brand" href="#" @click.prevent="go('/index.html')"><span class="logo-mark">M</span><strong>MaskFlow</strong></a>
    <nav class="marketing-links">
      <a href="#features">功能</a>
      <a href="#" @click.prevent="go('/billing.html')">价格</a>
      <a href="#" @click.prevent="go('/records.html')">帮助文档</a>
      <a href="#features">博客</a>
    </nav>
    <div class="marketing-actions">
      <a class="btn secondary" href="#" @click.prevent="go('/auth.html')">登录</a>
      <a class="btn" href="#" @click.prevent="auth.mode = 'register'; go('/auth.html')">注册</a>
    </div>
  </header>

  <template v-else>
    <header class="mf-topbar">
      <a class="marketing-brand" href="#" @click.prevent="go('/dashboard.html')"><span class="logo-mark">M</span><strong>MaskFlow</strong></a>
      <nav class="mf-nav">
        <a :class="{ active: page === 'dashboard' }" href="#" @click.prevent="go('/dashboard.html')">控制台</a>
        <a :class="{ active: page === 'annotate' }" href="#" @click.prevent="go('/annotate.html')">AI 标注工具</a>
        <a :class="{ active: page === 'export' }" href="#" @click.prevent="go('/export.html')">数据集</a>
        <a :class="{ active: page === 'billing' }" href="#" @click.prevent="go('/billing.html')">账单套餐</a>
        <a :class="{ active: page === 'files' }" href="#" @click.prevent="go('/files.html')">图片管理</a>
      </nav>
      <div class="mf-userbar">
        <button class="icon-btn">?</button>
        <span class="user-mini"><span class="avatar">M</span>{{ account?.username || "MaskFlow User" }}</span>
        <button class="btn secondary" @click="logout">退出</button>
      </div>
    </header>
  </template>

  <aside v-if="!['home', 'auth'].includes(page)" class="app-sidebar">
    <a class="app-sidebar-brand" href="#" @click.prevent="go('/dashboard.html')"><span class="logo-mark">M</span><strong>MaskFlow</strong></a>
    <nav>
      <a :class="{ active: page === 'dashboard' }" href="#" @click.prevent="go('/dashboard.html')"><span>D</span>控制台</a>
      <a :class="{ active: page === 'files' }" href="#" @click.prevent="go('/files.html')"><span>U</span>上传图片</a>
      <a :class="{ active: page === 'segment' }" href="#" @click.prevent="go('/segment.html')"><span>S</span>SAM 分割</a>
      <a :class="{ active: page === 'annotate' }" href="#" @click.prevent="go('/annotate.html')"><span>A</span>YOLO 标注</a>
      <a :class="{ active: page === 'export' }" href="#" @click.prevent="go('/export.html')"><span>E</span>数据集导出</a>
      <a :class="{ active: page === 'records' }" href="#" @click.prevent="go('/records.html')"><span>R</span>处理记录</a>
      <a :class="{ active: page === 'billing' }" href="#" @click.prevent="go('/billing.html')"><span>B</span>账单套餐</a>
      <a :class="{ active: page === 'settings' }" href="#" @click.prevent="go('/settings.html')"><span>C</span>账户设置</a>
    </nav>
    <div class="app-sidebar-account">
      <span class="avatar">M</span>
      <div><strong>{{ account?.username || "MaskFlow User" }}</strong><small>{{ account?.plan || "Free" }}</small></div>
      <button @click="logout">退出</button>
    </div>
  </aside>

  <HomePage />
  <AuthPage />

  <DashboardPage v-if="page === 'dashboard'" />
  <SegmentPage v-if="page === 'segment'" />
  <AnnotatePage v-if="page === 'annotate'" />
  <FilesPage v-if="page === 'files'" />
  <RecordsPage v-if="page === 'records'" />
  <ExportPage v-if="page === 'export'" />
  <BillingPage v-if="page === 'billing'" />
  <SettingsPage v-if="page === 'settings'" />
</template>
