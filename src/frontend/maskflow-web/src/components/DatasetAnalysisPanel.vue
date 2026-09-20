<script setup>
import { inject } from "vue";

const exportPage = inject("exportPage");
const projects = inject("projects");
const refresh = inject("refreshExportAnalysis");
const apply = inject("applyRecommendedSplit");
const validConfig = inject("validExportConfig");
const ratio = split => `${split.train} / ${split.val} / ${split.test}`;
const imageCounts = splits => ["train", "val", "test"].map(key => splits[key].images).join(" / ");
</script>

<template>
  <section class="dataset-analysis" aria-labelledby="dataset-analysis-title" :aria-busy="exportPage.analysisLoading">
    <div class="dataset-analysis-heading">
      <h2 id="dataset-analysis-title">数据集分析</h2>
      <button class="btn secondary compact-btn" type="button" :disabled="!projects.selectedId || !validConfig() || exportPage.analysisLoading" @click="refresh">重新分析</button>
    </div>
    <p v-if="!projects.selectedId" class="dataset-empty">尚未选择项目</p>
    <p v-else-if="!validConfig()" class="dataset-alert">比例需为 0–100 的整数且合计 100%，随机种子需为有效整数。</p>
    <p v-else-if="exportPage.analysisLoading" role="status" class="dataset-empty">正在统计标签与划分分布…</p>
    <p v-else-if="exportPage.analysisError" role="alert" class="dataset-alert">分析失败：{{ exportPage.analysisError }}</p>
    <template v-else-if="exportPage.analysis">
      <dl class="dataset-metrics">
        <div><dt>项目原图</dt><dd>{{ exportPage.analysis.totalImages }}</dd></div>
        <div><dt>参与划分原图</dt><dd>{{ exportPage.analysis.exportImages }}</dd></div>
        <div><dt>有效标签目标</dt><dd>{{ exportPage.analysis.totalAnnotations }}</dd></div>
        <div><dt>待人工确认目标</dt><dd>{{ exportPage.analysis.unconfirmedAnnotations }}</dd></div>
      </dl>
      <div v-if="exportPage.analysis.recommendationAvailable" class="dataset-recommendation">
        <div>
          <h3>建议比例 <strong>{{ ratio(exportPage.analysis.recommendedSplit) }}</strong></h3>
          <p>训练 / 验证 / 测试（%）</p>
          <p>{{ exportPage.analysis.recommendationReason }}</p>
        </div>
        <button class="btn secondary" type="button" :disabled="ratio(exportPage.split) === ratio(exportPage.analysis.recommendedSplit)" @click="apply">应用建议比例</button>
      </div>
      <div class="dataset-distribution">
        <h3>当前划分预估</h3>
        <p>训练 {{ exportPage.analysis.preview.images.train }} 张 · 验证 {{ exportPage.analysis.preview.images.val }} 张 · 测试 {{ exportPage.analysis.preview.images.test }} 张</p>
        <div class="dataset-split-bar" aria-hidden="true">
          <span v-for="name in ['train', 'val', 'test']" :key="name" :class="name" :style="{ flexGrow: exportPage.analysis.preview.images[name] }"></span>
        </div>
      </div>
      <div class="dataset-table-scroll" tabindex="0" role="region" aria-label="类别分布与补样建议">
        <table class="table dataset-class-table">
          <thead><tr><th>类别</th><th>目标数</th><th>目标占比</th><th>原图数</th><th>原图占比</th><th>当前划分<br />训练 / 验证 / 测试原图</th><th>建议划分<br />训练 / 验证 / 测试原图</th><th>建议补充原图</th></tr></thead>
          <tbody>
            <tr v-for="item in exportPage.analysis.classes" :key="item.label">
              <th scope="row">{{ item.label }}</th><td>{{ item.annotations }}</td><td>{{ item.objectPercent }}%</td><td>{{ item.images }}</td><td>{{ item.imagePercent }}%</td>
              <td>{{ imageCounts(item.splits) }}</td><td>{{ imageCounts(item.recommendedSplits) }}</td>
              <td :class="{ 'dataset-needs-samples': item.suggestedAdditionalImages > 0 }">{{ item.suggestedAdditionalImages ? `+${item.suggestedAdditionalImages} 张（参考目标 ${item.targetImages}）` : '已达参考目标' }}</td>
            </tr>
            <tr v-if="!exportPage.analysis.classes.length"><td colspan="8">尚无类别</td></tr>
          </tbody>
        </table>
      </div>
      <p class="dataset-basis">{{ exportPage.analysis.supplementBasis }}</p>
      <ul v-if="exportPage.analysis.warnings.length" class="dataset-warnings">
        <li v-for="warning in exportPage.analysis.warnings" :key="warning">{{ warning }}</li>
      </ul>
      <p class="dataset-basis">{{ exportPage.analysis.limitation }}</p>
    </template>
  </section>
</template>
