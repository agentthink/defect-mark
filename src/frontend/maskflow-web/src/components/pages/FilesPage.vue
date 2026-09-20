<script setup>
import { inject } from "vue";

const projects = inject("projects");
const selectedProject = inject("selectedProject");
const files = inject("files");
const account = inject("account");
const loading = inject("loading");
const uploadQueue = inject("uploadQueue");
const uploadQueueStatusLabel = inject("uploadQueueStatusLabel");
const formatBytes = inject("formatBytes");
const selectProject = inject("selectProject");
const createProject = inject("createProject");
const deleteCurrentProject = inject("deleteCurrentProject");
const uploadFiles = inject("uploadFiles");
const deleteFile = inject("deleteFile");
</script>

<template>
  <main class="mf-main page-pad work-page">
    <section class="work-wrap">
      <header class="work-title">
        <div class="work-title-icon">U</div>
        <div><h1>上传图片</h1><p>按项目管理用于 SAM 分割、YOLO 标注和数据集导出的图片素材。</p></div>
      </header>
      <nav class="billing-proto-tabs work-tabs"><a class="active">图片文件</a><a>上传记录</a></nav>
      <section class="project-bar">
        <div><strong>当前项目</strong><p>{{ selectedProject?.name || '请先选择或创建项目' }}</p></div>
        <select v-model="projects.selectedId" @change="selectProject(projects.selectedId)">
          <option value="">选择项目</option>
          <option v-for="project in projects.rows" :key="project.id" :value="project.id">{{ project.name }} · {{ project.imageCount || 0 }} 张</option>
        </select>
        <input v-model="projects.newName" placeholder="新项目名称，例如 药瓶分类" @keyup.enter="createProject" />
        <select v-model="projects.newDataType" class="project-type-select" title="新建项目的任务类型">
          <option value="detection">目标检测</option>
          <option value="segmentation">实例分割</option>
        </select>
        <button class="btn compact-btn" type="button" @click="createProject">新增项目</button>
        <button class="btn secondary compact-btn" type="button" :disabled="!projects.selectedId" @click="deleteCurrentProject">删除项目</button>
      </section>
      <section class="work-bottom">
        <article class="work-card">
          <h2>上传图片</h2>
          <input type="file" multiple accept="image/*" @change="files.selected = $event.target.files" />
          <p>{{ files.selected?.length ? '已选择 ' + files.selected.length + ' 个文件' : '支持批量选择图片文件。' }}</p>
          <button class="btn" :disabled="loading || !files.selected?.length || !projects.selectedId" @click="uploadFiles">上传到当前项目</button>
          <div v-if="uploadQueue.items.length" class="upload-queue-panel compact">
            <div class="upload-queue-head">
              <strong>上传进度</strong>
              <span>{{ uploadQueue.done + uploadQueue.failed + uploadQueue.skipped }} / {{ uploadQueue.total }}</span>
            </div>
            <div class="progress upload-progress">
              <i :style="{ width: uploadQueue.percent + '%' }"></i>
            </div>
            <p v-if="uploadQueue.currentName" class="upload-current">正在上传：{{ uploadQueue.currentName }}</p>
            <ul class="upload-queue-list">
              <li v-for="item in uploadQueue.items" :key="item.id" :class="['upload-queue-item', item.status]">
                <span class="upload-queue-name">{{ item.name }}</span>
                <em>{{ uploadQueueStatusLabel(item) }}</em>
              </li>
            </ul>
          </div>
          <p v-if="projects.status">{{ projects.status }}</p>
          <p v-if="account">空间：{{ formatBytes(account.usedBytes) }} / {{ formatBytes(account.quotaBytes) }}</p>
        </article>
        <article class="work-card wide">
          <h2>文件列表</h2>
          <table>
            <tbody>
              <tr v-for="file in files.rows" :key="file.id">
                <td>{{ file.name }}</td>
                <td>{{ formatBytes(file.size) }}</td>
                <td>{{ file.annotationCount || 0 }} 条标注</td>
                <td>{{ file.createdAt }}</td>
                <td><a :href="file.downloadUrl">下载</a> <button class="text-danger" type="button" @click="deleteFile(file.id)">删除</button></td>
              </tr>
              <tr v-if="!files.rows.length"><td colspan="5">当前项目暂无图片文件。</td></tr>
            </tbody>
          </table>
        </article>
      </section>
    </section>
  </main>
</template>
