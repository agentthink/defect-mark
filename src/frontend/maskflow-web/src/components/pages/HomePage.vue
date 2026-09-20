<template>
  <main v-if="page === 'home'" class="home-prototype">
    <section class="home-hero">
      <div class="home-copy">
        <p class="home-badge">AI 自动标注平台</p>
        <h1>把图片快速变成可训练的数据集</h1>
        <p>上传图片，调用 SAM 自动分割，人工复核标签与轮廓，最后一键导出 YOLO 数据集。MaskFlow 让数据准备从零散操作变成连续工作流。</p>
        <div class="home-actions">
          <a class="home-primary" href="#" @click.prevent="auth.mode = 'register'; go('/auth.html')">免费开始</a>
          <a class="home-secondary" href="#" @click.prevent="go('/segment.html')">查看演示</a>
        </div>
        <div class="home-checks"><span>流程清晰</span><span>批量处理</span><span>YOLO 导出</span></div>
      </div>
      <div class="home-preview">
        <div class="preview-head"><strong><span class="tiny-logo">M</span> MaskFlow</strong><span>自动保存 10:30:25</span></div>
        <div class="preview-body">
          <aside class="preview-rail"><span class="active">图片</span><span>标注</span><span>模型</span><span>任务</span><span>导出</span><span>设置</span></aside>
          <section class="preview-work">
            <p>road_scene_001.jpg</p>
            <img :src="heroPreviewRoad" alt="MaskFlow 标注预览" />
            <footer><span>1920 x 1080</span><span>4 / 128</span><span>已标注 6</span><span>待确认 122</span><span>缩放 100%</span></footer>
          </section>
          <aside class="preview-side">
            <h3>标注工具</h3>
            <div><span>person 3</span><span>car 1</span><span>road 1</span></div>
            <button type="button">运行 AI</button>
          </aside>
        </div>
      </div>
    </section>
    <section id="features" class="home-features">
      <article v-for="feature in homeFeatures" :key="feature[1]" class="home-feature-card">
        <span>{{ feature[0] }}</span>
        <h3>{{ feature[1] }}</h3>
        <p>{{ feature[2] }}</p>
      </article>
    </section>
  </main>
</template>

<script setup>
import { inject } from "vue";

const page = inject("page");
const auth = inject("auth");
const go = inject("go");
const homeFeatures = inject("homeFeatures");
const heroPreviewRoad = inject("heroPreviewRoad");
</script>
