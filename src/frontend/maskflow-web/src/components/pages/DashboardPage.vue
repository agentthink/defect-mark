<script setup>
import { inject } from "vue";

const go = inject("go");
const dashboard = inject("dashboard");
const files = inject("files");
</script>

<template>
  <main class="mf-main page-pad work-page">
    <section class="work-wrap">
      <header class="work-title">
        <div class="work-title-icon">D</div>
        <div><h1>控制台</h1><p>查看项目、图片、任务和 AI 配额的整体运行状态。</p></div>
      </header>
      <nav class="billing-proto-tabs work-tabs"><a class="active">概览</a><a>最近任务</a></nav>
      <section class="work-metrics">
        <article><span>当前项目</span><strong>{{ dashboard.projects.length }}</strong><p>正在维护的数据标注项目</p></article>
        <article><span>上传图片</span><strong>{{ files.rows.length }}</strong><p>当前工作区图片资源</p></article>
        <article><span>处理任务</span><strong>{{ dashboard.tasks.length }}</strong><p>SAM、YOLO 与导出任务</p></article>
        <article><span>今日 AI 次数</span><strong>{{ dashboard.quota ? (dashboard.quota.dailyLimit - dashboard.quota.dailyUsed) + '/' + dashboard.quota.dailyLimit : '50/50' }}</strong><p>每日自动重置</p></article>
      </section>
      <section class="work-bottom">
        <article class="work-card wide">
          <h2>最近处理记录</h2>
          <table><tbody><tr v-for="task in dashboard.tasks.slice(0,5)" :key="task.id"><td>{{ task.title }}</td><td>{{ task.type }}</td><td>{{ task.status }}</td><td>{{ task.createdAt }}</td></tr><tr v-if="!dashboard.tasks.length"><td colspan="4">暂无处理记录。</td></tr></tbody></table>
        </article>
        <article class="work-card"><h2>快捷入口</h2><button class="btn" @click="go('/files.html')">上传图片</button><button class="btn secondary" @click="go('/segment.html')">SAM 分割</button><button class="btn secondary" @click="go('/annotate.html')">YOLO 标注</button></article>
      </section>
    </section>
  </main>
</template>
