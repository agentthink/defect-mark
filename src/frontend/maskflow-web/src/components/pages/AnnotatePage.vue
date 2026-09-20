<template>
  <main class="mf-main page-pad work-page annotate-workbench-page">
    <section class="work-wrap annotate-shell">
      <header class="work-title annotate-title">
        <div class="work-title-icon">Y</div>
        <div>
          <h1>YOLO 标注工作台</h1>
          <p>批量上传图片，运行自动分割，在同一屏完成标签修正、确认和导出。</p>
        </div>
        <div class="save-state" :class="{ dirty: annotate.dirty }">{{ saveStateText }}</div>
      </header>

      <section class="project-bar annotate-project-bar">
        <div>
          <strong>当前项目</strong>
          <p>
            {{ selectedProject?.name || '请先选择或创建项目' }}
            <span v-if="selectedProject" class="project-task-badge">{{ selectedProjectDataTypeLabel }}</span>
          </p>
        </div>
        <select :value="projects.selectedId" @change="selectProject($event.target.value)">
          <option value="">选择项目</option>
          <option v-for="project in projects.rows" :key="project.id" :value="project.id">{{ project.name }} · {{ project.imageCount || 0 }} 张</option>
        </select>
        <input v-model="projects.newName" placeholder="新项目名称，例如 道路场景" @keyup.enter="createProject" />
        <select v-model="projects.newDataType" class="project-type-select" title="新建项目的任务类型">
          <option value="detection">目标检测</option>
          <option value="segmentation">实例分割</option>
        </select>
        <button class="btn compact-btn" type="button" @click="createProject">新建项目</button>
        <button class="btn secondary compact-btn" type="button" :disabled="loading || !projects.selectedId" @click="copyCurrentProject">复制项目</button>
      </section>

      <section class="annotate-topbar">
        <label class="upload-inline">
          <input type="file" multiple accept="image/*" :disabled="loading" @change="changeAnnotateFiles($event.target.files)" />
          <span>上传图片</span>
        </label>
        <label class="inline-control">置信度 {{ annotate.conf }}<input v-model="annotate.conf" type="range" min="0.01" max="0.95" step="0.01" /></label>
        <label class="inline-control annotate-default-label">
          本次默认类别
          <select v-model="annotate.defaultRunLabel" :disabled="!annotate.labels.length">
            <option value="">未分配</option>
            <option v-for="label in annotate.labels" :key="label" :value="label">{{ label }}</option>
          </select>
        </label>
        <div v-if="annotate.labels.length" class="annotate-label-legend" aria-label="标签颜色图例">
          <span v-for="label in annotate.labels" :key="label" class="annotate-legend-item">
            <i class="label-color-swatch" :style="labelSwatchStyle(label)" />
            {{ label }}
          </span>
        </div>
        <button class="btn" :disabled="loading || !annotate.ready || !canRunAnnotateAi" @click="runCurrentMask">运行当前图片</button>
        <button class="btn secondary" :disabled="loading || !files.rows.length || !canRunAnnotateAi" @click="runMasks">批量运行 AI</button>
        <button class="btn secondary" :disabled="loading || !annotate.ready || !annotate.dirty" @click="saveAnnotation">保存</button>
        <button class="btn ghost" :disabled="!projects.selectedId || !annotate.labels.length" @click="createExport">导出 ZIP</button>
      </section>

      <p v-if="!annotate.labels.length" class="annotate-label-hint">请先添加至少一个项目标签，再运行 AI 自动标注。</p>

      <section class="annotate-workbench">
        <aside class="work-card image-queue-panel">
          <div class="panel-head">
            <div><h2>图片队列</h2><p>{{ files.rows.length }} 张图片</p></div>
          </div>
          <div class="queue-filters" role="tablist" aria-label="图片筛选">
            <button type="button" :class="{ active: annotate.filter === 'all' }" @click="annotate.filter = 'all'">全部</button>
            <button type="button" :class="{ active: annotate.filter === 'unannotated' }" @click="annotate.filter = 'unannotated'">未标注</button>
            <button type="button" :class="{ active: annotate.filter === 'annotated' }" @click="annotate.filter = 'annotated'">已标注</button>
          </div>
          <div class="queue-list">
            <button v-for="file in filteredFiles" :key="file.id" :class="['file-row-btn', { active: annotate.current?.id === file.id }]" type="button" @click="selectAnnotateFile(file)">
              <span>{{ file.name }}</span>
              <small>{{ formatBytes(file.size) }} · {{ file.annotated ? file.annotationCount + ' 条标注' : '未标注' }}</small>
              <i class="queue-delete-btn" title="删除图片" @click.stop="deleteAnnotateFile(file)">删除</i>
              <b :class="['file-status-dot', { done: file.annotated }]" title="已执行自动标注"></b>
            </button>
            <div v-if="!filteredFiles.length" class="empty-note">当前筛选下没有图片。</div>
          </div>
          <p class="queue-status">{{ annotate.status }}</p>
        </aside>

        <section class="work-stage yolo-stage annotate-canvas-panel" :inert="!annotate.ready">
          <div class="canvas-topbar">
            <button :class="['btn compact-btn', annotate.drawMode ? 'active-tool' : 'secondary']" type="button" :disabled="!annotate.current" @click="toggleManualDrawMode">
              {{ annotate.drawMode ? '退出画框' : '手动画框' }}
            </button>
            <button :class="['btn compact-btn', annotate.pointMode ? 'active-tool' : 'secondary']" type="button" :disabled="!annotate.current" @click="togglePointPromptMode">
              {{ annotate.pointMode ? '退出点提示' : '点提示抠图' }}
            </button>
            <template v-if="annotate.pointMode">
              <button :class="['btn compact-btn', annotate.pointPolarity === 1 ? 'active-tool' : 'secondary']" type="button" @click="setAnnotatePointPolarity(1)">正向点</button>
              <button :class="['btn compact-btn', annotate.pointPolarity === 0 ? 'active-tool' : 'secondary']" type="button" @click="setAnnotatePointPolarity(0)">负向点</button>
              <button class="btn compact-btn" type="button" :disabled="!annotate.pointDraft.candidates.length" @click="confirmAnnotatePointTarget">确认目标</button>
              <button class="btn secondary compact-btn" type="button" @click="startNewAnnotatePointTarget">新建目标</button>
              <button class="btn secondary compact-btn" type="button" :disabled="!annotate.pointDraft.points.length" @click="clearAnnotatePointDraft">清空点</button>
            </template>
            <button class="btn secondary compact-btn" type="button" :disabled="currentFileIndex <= 0" @click="selectAdjacentFile(-1)">上一张</button>
            <span>{{ currentFileIndex >= 0 ? currentFileIndex + 1 : 0 }} / {{ files.rows.length }} · {{ annotate.current?.name || '未选择图片' }}</span>
            <button class="btn secondary compact-btn" type="button" :disabled="currentFileIndex < 0 || currentFileIndex >= files.rows.length - 1" @click="selectAdjacentFile(1)">下一张</button>
            <button class="btn danger compact-btn" type="button" :disabled="loading || !annotate.current" @click="deleteCurrentAnnotateFile">删除图片</button>
          </div>
          <div v-if="annotate.current" class="yolo-canvas">
            <div
              v-if="previewUrl(annotate.current)"
              :class="['yolo-image-frame', { drawing: annotate.drawMode, pointing: annotate.pointMode }]"
              :style="yoloFrameStyle"
              @pointerdown="annotate.pointMode ? handleAnnotatePointClick($event) : beginManualBox($event)"
              @pointermove="updateManualBox"
              @pointerup="finishManualBox"
              @pointercancel="cancelManualBox"
              @pointerleave="updateManualBox"
              @contextmenu.prevent
            >
              <img :src="previewUrl(annotate.current)" alt="标注图片" @load="updateYoloFrame" />
              <img
                v-if="annotate.pointMode && annotate.pointDraft.overlay"
                class="point-mask-overlay"
                :src="annotate.pointDraft.overlay"
                alt="点提示 Mask"
              />
              <div v-if="annotate.drawingBox" class="manual-draw-box" :style="drawingBoxStyle()"></div>
              <svg class="yolo-mask-layer" viewBox="0 0 100 100" preserveAspectRatio="none">
                <polygon
                  v-for="item in annotate.annotations"
                  :key="'poly-' + item.id"
                  :points="segmentPoints(item)"
                  :class="{ active: annotate.activeId === item.id, unassigned: !item.label }"
                  :style="annotationPolygonStyle(item)"
                />
                <polygon
                  v-if="annotate.pointMode && activeAnnotatePointCandidate()"
                  class="point-draft-polygon"
                  :points="candidatePolygonPoints(activeAnnotatePointCandidate())"
                />
              </svg>
              <span
                v-for="point in annotate.pointDraft.points"
                :key="point.id"
                :class="['prompt-point', point.label === 1 ? 'positive' : 'negative']"
                :style="promptPointStyle(point, annotate.width, annotate.height)"
                :title="point.label === 1 ? '正向点' : '负向点'"
              />
              <button
                v-for="item in annotate.annotations"
                :key="item.id"
                :class="['yolo-box', { active: annotate.activeId === item.id, unassigned: !item.label, matched: reviewFilterActive && reviewFilterMatchedAnnotations.some((match) => match.id === item.id) }]"
                :style="annotationBoxStyle(item)"
                type="button"
                @click="annotate.activeId = item.id"
              >
                {{ formatAnnotationLabel(item.label) }}
              </button>
            </div>
            <div v-else class="empty-note">正在加载图片预览...</div>
            <div v-if="annotate.pointMode && annotate.pointDraft.candidates.length" class="point-candidate-bar">
              <span>当前目标 Mask 已更新（多个正负点共同形成一个结果）</span>
              <span v-if="annotate.pointDraft.loading">更新中...</span>
            </div>
          </div>
          <div v-else class="canvas-empty">
            <strong>选择图片开始标注</strong>
            <span>左侧上传或选择图片后，可运行 AI 自动生成候选标注。</span>
          </div>
          <div class="canvas-bottombar" role="toolbar" aria-label="画布缩放">
            <button class="btn secondary compact-btn" type="button" @click="setYoloZoom(annotate.zoom - 0.1)">缩小</button>
            <button class="btn secondary compact-btn" type="button" @click="setYoloZoom(annotate.zoom + 0.1)">放大</button>
            <button class="btn ghost compact-btn" type="button" @click="resetYoloZoom">适配</button>
            <input :value="Math.round(annotate.zoom * 100)" type="range" min="25" max="300" step="5" aria-label="缩放比例" @input="setYoloZoom($event.target.value / 100)" />
            <span>{{ Math.round(annotate.zoom * 100) }}%</span>
          </div>
        </section>

        <aside class="work-card annotation-inspector">
          <div class="panel-head">
            <div>
              <h2>标注结果</h2>
              <p>{{ annotationStats.confirmed }} 人工已确认 · {{ annotationStats.pending }} 待人确认 · {{ annotationStats.unassigned }} 未分配</p>
            </div>
            <div class="annotation-actions">
              <button :class="['btn compact-btn', annotate.reviewFilterOpen ? 'active-tool' : 'secondary']" type="button" :disabled="!annotate.annotations.length" @click="toggleReviewFilterPanel">
                {{ annotate.reviewFilterOpen ? '收起筛选' : '筛选' }}
              </button>
              <button class="btn compact-btn" type="button" :disabled="!annotate.annotations.length" @click="saveAnnotation">保存</button>
            </div>
          </div>
          <section v-if="annotate.reviewFilterOpen" class="review-filter-panel">
            <div class="review-filter-head">
              <div>
                <h3>标注筛选</h3>
                <p>
                  {{ reviewFilterActive ? `当前图命中 ${reviewFilterMatchedAnnotations.length} 个框` : '按面积、长宽、位置筛选全项目' }}
                  <span v-if="annotate.reviewFilterGlobalMatches">
                    · 全项目 {{ annotate.reviewFilterGlobalMatches.annotations }} 个 / {{ annotate.reviewFilterGlobalMatches.files }} 张图
                  </span>
                </p>
              </div>
              <button class="btn ghost compact-btn" type="button" @click="resetReviewFilters">清空</button>
            </div>
            <div class="review-filter-grid" @input="annotate.reviewFilterGlobalMatches = null" @change="annotate.reviewFilterGlobalMatches = null">
              <label>标签<select v-model="annotate.reviewFilters.label"><option value="">全部</option><option v-for="label in annotate.labels" :key="label" :value="label">{{ label }}</option></select></label>
              <label>最小面积%<input v-model="annotate.reviewFilters.minArea" type="number" min="0" max="100" step="0.1" /></label>
              <label>最大面积%<input v-model="annotate.reviewFilters.maxArea" type="number" min="0" max="100" step="0.1" /></label>
              <label>最小宽%<input v-model="annotate.reviewFilters.minWidth" type="number" min="0" max="100" step="0.1" /></label>
              <label>最大宽%<input v-model="annotate.reviewFilters.maxWidth" type="number" min="0" max="100" step="0.1" /></label>
              <label>最小高%<input v-model="annotate.reviewFilters.minHeight" type="number" min="0" max="100" step="0.1" /></label>
              <label>最大高%<input v-model="annotate.reviewFilters.maxHeight" type="number" min="0" max="100" step="0.1" /></label>
              <label>最小宽高比<input v-model="annotate.reviewFilters.minAspect" type="number" min="0" step="0.1" /></label>
              <label>最大宽高比<input v-model="annotate.reviewFilters.maxAspect" type="number" min="0" step="0.1" /></label>
              <label>中心X最小%<input v-model="annotate.reviewFilters.minCenterX" type="number" min="0" max="100" step="1" /></label>
              <label>中心X最大%<input v-model="annotate.reviewFilters.maxCenterX" type="number" min="0" max="100" step="1" /></label>
              <label>中心Y最小%<input v-model="annotate.reviewFilters.minCenterY" type="number" min="0" max="100" step="1" /></label>
              <label>中心Y最大%<input v-model="annotate.reviewFilters.maxCenterY" type="number" min="0" max="100" step="1" /></label>
              <label>最小置信度<input v-model="annotate.reviewFilters.minConfidence" type="number" min="0" max="1" step="0.01" /></label>
            </div>
            <div class="review-filter-actions">
              <button class="btn ghost compact-btn" type="button" :disabled="!reviewFilterActive || loading" @click="collectReviewFilterMatches">
                统计全项目命中
              </button>
              <button class="btn secondary compact-btn review-delete-btn" type="button" :disabled="!reviewFilterActive || loading" @click="deleteReviewFilterMatches">
                删除全项目命中项
              </button>
            </div>
          </section>

          <div class="annotation-list">
            <section
              v-for="item in annotate.annotations"
              :key="item.id"
              :class="['annotation-row', { active: annotate.activeId === item.id, unassigned: !item.label }]"
              :style="annotationRowAccentStyle(item)"
              @click="annotate.activeId = item.id"
            >
              <span class="label-color-swatch annotation-row-swatch" :style="labelSwatchStyle(item.label)" />
              <span
                class="confirm-dot"
                :class="{ unconfirmed: !item.confirmed }"
                @click.stop="toggleAnnotationConfirmed(item.id)"
                :title="item.confirmed ? '人工已确认' : '待人确认：点击确认标签与 mask'"
              ></span>
              <select :value="item.label ?? ''" @click.stop @change="onAnnotationLabelChange(item, $event.target.value)">
                <option value="">未分配</option>
                <option v-for="label in annotate.labels" :key="label" :value="label">{{ label }}</option>
              </select>
              <small>{{ item.label ? `class ${item.classId}` : '未分配' }} · conf {{ Number(item.confidence || 1).toFixed(2) }}</small>
              <button class="btn secondary compact-btn" type="button" @click.stop="removeAnnotation(item.id)">删除</button>
            </section>
            <div v-if="!annotate.annotations.length" class="empty-note">运行 AI 后会在这里显示标注结果。</div>
          </div>

          <div class="label-manager-inline">
            <h2>标签</h2>
            <div class="settings-inline-form yolo-label-form">
              <input v-model="annotate.newLabel" placeholder="新增标签，例如 person" @keyup.enter="addAnnotateLabel" />
              <button class="btn compact-btn" type="button" @click="addAnnotateLabel">新增</button>
            </div>
            <div v-if="!annotate.labels.length" class="empty-note">添加标签后即可运行 AI 并分配类别。</div>
            <div class="label-chip-list">
              <div v-for="label in annotate.labels" :key="label" class="label-chip-row">
                <button type="button" class="label-chip" :style="labelChipStyle(label)" @click="applyLabelToActive(label)">
                  <i class="label-color-swatch" :style="labelSwatchStyle(label)" />
                  {{ label }}
                </button>
                <template v-if="annotate.pendingDeleteLabel === label">
                  <select v-model="annotate.labelDeleteReplace" class="label-delete-replace">
                    <option value="">未分配</option>
                    <option v-for="other in annotate.labels.filter((item) => item !== label)" :key="other" :value="other">{{ other }}</option>
                  </select>
                  <button class="btn compact-btn" type="button" :disabled="loading" @click="confirmDeleteAnnotateLabel">确认</button>
                  <button class="btn secondary compact-btn" type="button" @click="cancelDeleteAnnotateLabel">取消</button>
                </template>
                <button
                  v-else
                  class="label-delete-btn"
                  type="button"
                  :disabled="loading"
                  @click="beginDeleteAnnotateLabel(label)"
                >
                  删除
                </button>
              </div>
            </div>
          </div>

          <div class="active-object-card">
            <h2>当前目标</h2>
            <p v-if="activeAnnotation">
              {{ formatAnnotationLabel(activeAnnotation.label) }}
              · {{ activeAnnotation.label ? `class ${activeAnnotation.classId}` : '未分配' }}
              · {{ activeAnnotation.confirmed ? '人工已确认' : '待人确认' }}
            </p>
            <p v-else>选择一个标注目标后显示属性。</p>
            <p v-if="selectedProject" class="export-format-hint">保存/导出格式：{{ selectedProjectExportHint }}</p>
            <button class="btn secondary" type="button" :disabled="!annotate.annotations.some((item) => item.label)" @click="downloadCurrentTxt">导出当前 TXT</button>
          </div>
        </aside>
      </section>
    </section>
  </main>
</template>

<script setup>
import { inject } from "vue";

const annotate = inject("annotate");
const projects = inject("projects");
const selectedProject = inject("selectedProject");
const selectedProjectDataTypeLabel = inject("selectedProjectDataTypeLabel");
const selectedProjectExportHint = inject("selectedProjectExportHint");
const files = inject("files");
const filteredFiles = inject("filteredFiles");
const loading = inject("loading");
const formatBytes = inject("formatBytes");
const selectAnnotateFile = inject("selectAnnotateFile");
const runCurrentMask = inject("runCurrentMask");
const runMasks = inject("runMasks");
const saveAnnotation = inject("saveAnnotation");
const removeAnnotation = inject("removeAnnotation");
const annotationBoxStyle = inject("annotationBoxStyle");
const segmentPoints = inject("segmentPoints");
const yoloFrameStyle = inject("yoloFrameStyle");
const updateYoloFrame = inject("updateYoloFrame");
const drawingBoxStyle = inject("drawingBoxStyle");
const beginManualBox = inject("beginManualBox");
const updateManualBox = inject("updateManualBox");
const finishManualBox = inject("finishManualBox");
const cancelManualBox = inject("cancelManualBox");
const toggleManualDrawMode = inject("toggleManualDrawMode");
const togglePointPromptMode = inject("togglePointPromptMode");
const setAnnotatePointPolarity = inject("setAnnotatePointPolarity");
const handleAnnotatePointClick = inject("handleAnnotatePointClick");
const selectAnnotatePointCandidate = inject("selectAnnotatePointCandidate");
const clearAnnotatePointDraft = inject("clearAnnotatePointDraft");
const confirmAnnotatePointTarget = inject("confirmAnnotatePointTarget");
const startNewAnnotatePointTarget = inject("startNewAnnotatePointTarget");
const promptPointStyle = inject("promptPointStyle");
const activeAnnotatePointCandidate = inject("activeAnnotatePointCandidate");
const candidatePolygonPoints = inject("candidatePolygonPoints");
const reviewFilterActive = inject("reviewFilterActive");
const reviewFilterMatchedAnnotations = inject("reviewFilterMatchedAnnotations");
const collectReviewFilterMatches = inject("collectReviewFilterMatches");
const resetReviewFilters = inject("resetReviewFilters");
const toggleReviewFilterPanel = inject("toggleReviewFilterPanel");
const deleteReviewFilterMatches = inject("deleteReviewFilterMatches");
const addAnnotateLabel = inject("addAnnotateLabel");
const beginDeleteAnnotateLabel = inject("beginDeleteAnnotateLabel");
const confirmDeleteAnnotateLabel = inject("confirmDeleteAnnotateLabel");
const cancelDeleteAnnotateLabel = inject("cancelDeleteAnnotateLabel");
const applyLabelToActive = inject("applyLabelToActive");
const applyAnnotationLabel = inject("applyAnnotationLabel");
const toggleAnnotationConfirmed = inject("toggleAnnotationConfirmed");
const changeAnnotateFiles = inject("changeAnnotateFiles");
const deleteFile = inject("deleteFile");
const previewUrl = inject("previewUrl");
const downloadCurrentTxt = inject("downloadCurrentTxt");
const selectProject = inject("selectProject");
const createProject = inject("createProject");
const copyCurrentProject = inject("copyCurrentProject");
const createExport = inject("createExport");
const currentFileIndex = inject("currentFileIndex");
const activeAnnotation = inject("activeAnnotation");
const annotationStats = inject("annotationStats");
const saveStateText = inject("saveStateText");
const setYoloZoom = inject("setYoloZoom");
const resetYoloZoom = inject("resetYoloZoom");
const selectAdjacentFile = inject("selectAdjacentFile");
const canRunAnnotateAi = inject("canRunAnnotateAi");
const formatAnnotationLabel = inject("formatAnnotationLabel");
const annotationPolygonStyle = inject("annotationPolygonStyle");
const labelChipStyle = inject("labelChipStyle");
const labelSwatchStyle = inject("labelSwatchStyle");
const annotationRowAccentStyle = inject("annotationRowAccentStyle");

function onAnnotationLabelChange(item, value) {
  applyAnnotationLabel(item, value);
}

async function deleteAnnotateFile(file) {
  if (!file?.id) return;
  const message = file.annotated
    ? `确定删除 ${file.name} 吗？这张图的 ${file.annotationCount || 0} 条标注也会一起删除。`
    : `确定删除 ${file.name} 吗？`;
  if (!window.confirm(message)) return;
  await deleteFile(file.id);
}

async function deleteCurrentAnnotateFile() {
  await deleteAnnotateFile(annotate.current);
}
</script>
