<script setup>
import { inject } from "vue";

const segment = inject("segment");
const loading = inject("loading");
const runSegment = inject("runSegment");
const selectSegmentFile = inject("selectSegmentFile");
const showSegmentOverlay = inject("showSegmentOverlay");
const setSegmentPromptMode = inject("setSegmentPromptMode");
const setSegmentPointPolarity = inject("setSegmentPointPolarity");
const handleSegmentPointClick = inject("handleSegmentPointClick");
const onSegmentImageLoad = inject("onSegmentImageLoad");
const clearSegmentPointDraft = inject("clearSegmentPointDraft");
const confirmSegmentPointTarget = inject("confirmSegmentPointTarget");
const startNewSegmentPointTarget = inject("startNewSegmentPointTarget");
const previewConfirmedSegmentTarget = inject("previewConfirmedSegmentTarget");
const segmentCanvasSrc = inject("segmentCanvasSrc");
const promptPointStyle = inject("promptPointStyle");
</script>

<template>
  <main class="mf-main page-pad work-page">
    <section class="work-wrap">
      <header class="work-title">
        <div class="work-title-icon">S</div>
        <div><h1>SAM 分割</h1><p>支持自动识别、文本提示，以及点击正负点交互抠图。</p></div>
      </header>
      <nav class="billing-proto-tabs work-tabs"><a class="active">单图分割</a><a>结果列表</a></nav>
      <section class="work-tool-grid segment-tool-grid">
        <aside class="work-card">
          <h2>分割配置</h2>
          <input type="file" accept="image/*" @change="selectSegmentFile($event.target.files[0])" />

          <div class="prompt-mode-switch" role="tablist" aria-label="分割模式">
            <button type="button" :class="{ active: segment.promptMode === 'auto' }" @click="setSegmentPromptMode('auto')">自动</button>
            <button type="button" :class="{ active: segment.promptMode === 'text' }" @click="setSegmentPromptMode('text')">文本</button>
            <button type="button" :class="{ active: segment.promptMode === 'points' }" @click="setSegmentPromptMode('points')">点提示</button>
          </div>

          <template v-if="segment.promptMode === 'text'">
            <label>提示词<input v-model="segment.prompt" placeholder="输入 person, car, box" /></label>
          </template>

          <template v-if="segment.promptMode === 'points'">
            <div class="point-polarity-switch">
              <button type="button" :class="{ active: segment.pointPolarity === 1 }" @click="setSegmentPointPolarity(1)">正向点</button>
              <button type="button" :class="{ active: segment.pointPolarity === 0 }" @click="setSegmentPointPolarity(0)">负向点</button>
            </div>
            <p class="point-hint">
              当前只编辑<strong>一个目标</strong>：所有正/负点共同精修同一块 Mask。
              满意后点「确认目标」；再抠下一个物体前请先「新建目标」。
              右侧已确认目标可点击预览。
            </p>
            <div class="point-actions">
              <button class="btn" type="button" :disabled="!segment.pointDraft.candidates.length" @click="confirmSegmentPointTarget">确认目标</button>
              <button class="btn secondary" type="button" @click="startNewSegmentPointTarget">新建目标</button>
              <button class="btn secondary" type="button" :disabled="!segment.pointDraft.points.length" @click="clearSegmentPointDraft">清空提示点</button>
            </div>
          </template>

          <label>置信度 {{ segment.conf }}<input v-model="segment.conf" type="range" min="0.01" max="0.95" step="0.01" /></label>
          <button
            v-if="segment.promptMode !== 'points'"
            class="btn"
            :disabled="loading || !segment.file"
            @click="runSegment"
          >
            {{ loading ? '分割中...' : '开始分割' }}
          </button>
          <p>{{ segment.status }}</p>
          <p v-if="segment.pointDraft.loading" class="point-loading">正在更新 Mask...</p>
        </aside>

        <section class="work-stage segment-stage">
          <div
            v-if="segmentCanvasSrc()"
            :class="['segment-canvas', { pointing: segment.promptMode === 'points' }]"
            @pointerdown="handleSegmentPointClick"
            @contextmenu.prevent
          >
            <img
              :src="segmentCanvasSrc()"
              alt="分割画布"
              @load="onSegmentImageLoad"
            />
            <span
              v-for="point in segment.pointDraft.points"
              :key="point.id"
              :class="['prompt-point', point.label === 1 ? 'positive' : 'negative']"
              :style="promptPointStyle(point, segment.width, segment.height)"
              :title="point.label === 1 ? '正向点' : '负向点'"
            />
          </div>
          <div v-else>选择图片后开始 AI 分割</div>
        </section>

        <aside class="work-card segment-result-card">
          <h2>分割结果</h2>
          <template v-if="segment.promptMode === 'points'">
            <p>当前目标提示点：{{ segment.pointDraft.points.length }}（正 {{ segment.pointDraft.points.filter((p) => p.label === 1).length }} / 负 {{ segment.pointDraft.points.filter((p) => p.label === 0).length }}）</p>
            <p v-if="segment.pointDraft.candidates.length" class="point-current-mask">当前目标 Mask 已更新（正负点共同作用的一个结果）</p>
            <p v-else class="point-hint">在图上点击后，这里会显示当前目标的分割结果。</p>
            <p>已确认目标：{{ segment.confirmed.length }}（点击可预览）</p>
            <div class="overlay-list">
              <button
                v-for="(item, index) in segment.confirmed"
                :key="item.id"
                :class="['overlay-row', { active: segment.activeConfirmedId === item.id }]"
                type="button"
                @click="previewConfirmedSegmentTarget(item.id)"
              >
                <span>目标 {{ index + 1 }}</span>
                <b>{{ Number(item.score || 0).toFixed(2) }}</b>
              </button>
            </div>
          </template>
          <template v-else>
            <p v-if="segment.mode">模式：{{ segment.mode === 'text' ? '文本提示' : '自动识别' }}</p>
            <p v-if="segment.count">目标数量：{{ segment.count }}</p>
            <p v-if="segment.warning" class="segment-warning">自动分类暂不可用，已先返回分割结果。可输入 person、car 等提示词获得类别结果。</p>
            <div class="overlay-list">
              <button v-if="segment.overlays.all" :class="['overlay-row', { active: segment.activeOverlay === 'all' }]" type="button" @click="showSegmentOverlay('all')"><span>全部目标</span><b>{{ segment.count }}</b></button>
              <button v-for="cat in segment.categories" :key="cat.id" :class="['overlay-row', { active: segment.activeOverlay === cat.id }]" type="button" @click="showSegmentOverlay(cat.id)"><span>{{ cat.label }}</span><b>{{ cat.count }}</b></button>
            </div>
            <p v-if="!segment.categories.length">运行后将在这里显示识别类别。</p>
          </template>
        </aside>
      </section>
    </section>
  </main>
</template>
