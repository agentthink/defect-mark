import { computed, nextTick, onMounted, provide, reactive, ref, watch } from "vue";
import { matchesAnnotationFilters } from "../lib/annotationReview";
import { apiFetch, authHeaders, clearSession, downloadAuthenticated, formatBytes, saveSession, session, user as currentUser } from "../lib/api";
import {
  buildAnnotationBoxStyle,
  buildAnnotationPolygonStyle,
  buildAnnotationRowAccentStyle,
  buildLabelChipStyle,
  buildLabelSwatchStyle,
  getLabelColor
} from "../lib/labelColors";
import {
  buildYoloTxt as formatYoloTxt,
  normalizeProjectDataType,
  projectDataTypeLabel,
  projectYoloExportHint
} from "../lib/yoloFormat";
import heroPreviewRoad from "../assets/hero-preview-road.png";

function createEmptyPointDraft() {
  return {
    points: [],
    candidates: [],
    activeCandidate: 0,
    overlay: "",
    requestId: 0,
    loading: false
  };
}

export function useMaskFlowApp() {
  const routes = {
    "/": "home",
    "/index.html": "home",
    "/auth.html": "auth",
    "/dashboard.html": "dashboard",
    "/segment.html": "segment",
    "/annotate.html": "annotate",
    "/export.html": "export",
    "/files.html": "files",
    "/records.html": "records",
    "/billing.html": "billing",
    "/settings.html": "settings"
  };

  const path = ref(window.location.pathname);
  const page = computed(() => routes[path.value] || "home");
  const account = ref(currentUser());
  const message = ref("");
  const loading = ref(false);
  let annotationSelection = 0;
  let annotationEditRevision = 0;
  let analysisRequestId = 0;

  const auth = reactive({
    mode: new URLSearchParams(location.search).get("mode") || "login",
    email: "",
    username: "",
    password: "",
    confirmPassword: "",
    resetToken: ""
  });
  const dashboard = reactive({ projects: [], tasks: [], quota: null });
  const files = reactive({ rows: [], selected: null });
  const records = reactive({ rows: [] });
  const projects = reactive({ rows: [], selectedId: "", newName: "", newDataType: "detection", status: "" });
  const exportPage = reactive({
    tab: "config",
    format: "yolo-detect",
    split: { train: 70, val: 20, test: 10 },
    seed: 42,
    analysis: null,
    analysisLoading: false,
    analysisError: "",
    status: "",
    rows: []
  });
  const selectedProject = computed(() => projects.rows.find((item) => item.id === projects.selectedId) || null);
  const selectedProjectDataType = computed(() => normalizeProjectDataType(selectedProject.value?.dataType));
  const selectedProjectDataTypeLabel = computed(() => projectDataTypeLabel(selectedProject.value?.dataType));
  const selectedProjectExportHint = computed(() => projectYoloExportHint(selectedProject.value?.dataType));
  const segment = reactive({
    file: null,
    preview: "",
    prompt: "",
    conf: 0.25,
    overlay: "",
    overlays: {},
    activeOverlay: "all",
    categories: [],
    status: "准备就绪",
    warning: "",
    mode: "",
    count: 0,
    promptMode: "auto",
    width: 0,
    height: 0,
    pointPolarity: 1,
    pointDraft: createEmptyPointDraft(),
    confirmed: [],
    activeConfirmedId: ""
  });
  const annotate = reactive({
    selected: null,
    fileId: null,
    current: null,
    ready: false,
    preview: "",
    frame: { width: 0, height: 0 },
    annotations: [],
    activeId: "",
    width: 0,
    height: 0,
    conf: 0.25,
    labels: [],
    defaultRunLabel: "",
    newLabel: "",
    pendingDeleteLabel: "",
    labelDeleteReplace: "",
    status: "请选择或上传图片",
    dirty: false,
    savedAt: null,
    zoom: 1,
    filter: "all",
    drawMode: false,
    drawingBox: null,
    pointMode: false,
    pointPolarity: 1,
    pointDraft: createEmptyPointDraft(),
    reviewFilterOpen: false,
    reviewFilterGlobalMatches: null,
    reviewFilters: {
      label: "",
      minArea: "",
      maxArea: "",
      minWidth: "",
      maxWidth: "",
      minHeight: "",
      maxHeight: "",
      minAspect: "",
      maxAspect: "",
      minCenterX: "",
      maxCenterX: "",
      minCenterY: "",
      maxCenterY: "",
      minConfidence: ""
    }
  });
  const settings = reactive({
    active: "profile",
    username: "",
    phone: "",
    currentPassword: "",
    newPassword: "",
    confirmPassword: "",
    notifications: { emailTask: true, emailBilling: true, browserNotice: true, weeklyReport: false },
    tokenName: "",
    tokenValue: "",
    tokens: [],
    teamEmail: "",
    teamRole: "member",
    members: [],
    devices: []
  });
  const settingsTabs = [
    ["profile", "个人信息"],
    ["password", "修改密码"],
    ["notifications", "通知设置"],
    ["tokens", "API Token"],
    ["team", "团队管理"],
    ["devices", "设备管理"]
  ];
  const billingPlans = [
    {
      id: "free",
      name: "Free",
      price: 0,
      storage: "10G",
      ai: "每日 50 次",
      audience: "个人体验",
      features: ["基础标注工具", "单图 AI 分割", "YOLO 数据集导出", "文件管理"]
    },
    {
      id: "pro",
      name: "Pro",
      price: 49,
      storage: "50G",
      ai: "每日 1000 次",
      audience: "专业用户",
      featured: true,
      features: ["批量自动标注", "高级标注工具", "API Token", "团队协作"]
    },
    {
      id: "team",
      name: "Team",
      price: 299,
      storage: "500G",
      ai: "高额额度",
      audience: "团队协作",
      features: ["高优先级处理", "团队成员管理", "专属技术支持", "自定义导出"]
    }
  ];
  const billingExplain = [
    ["存储", "存储空间", "用于存储上传图片、视频、标注数据和导出结果。"],
    ["AI", "AI 处理次数", "包括自动检测、智能分割、人工校正辅助等 AI 功能调用次数。"],
    ["标注", "标注工具", "不同套餐提供不同的标注工具和高级能力。"],
    ["团队", "团队协作", "支持团队成员管理、任务分配与协作标注。"]
  ];
  const billingFaqs = ["套餐可以随时升级或降级吗？", "AI 处理次数用完后怎么办？", "存储空间可以单独购买吗？", "如何开具发票？"];
  const homeFeatures = [
    ["AI", "AI 自动分割", "高精度分割，节省时间"],
    ["批量", "批量处理", "支持批量图片自动处理"],
    ["标注", "标注工具", "丰富的标注与编辑工具"],
    ["导出", "数据集导出", "支持 YOLO 格式导出"],
    ["云端", "云端加速", "按需算力，权限管理"],
    ["安全", "安全可靠", "隐私保护，权限管理"]
  ];

  function go(to) {
    history.pushState({}, "", to);
    path.value = window.location.pathname;
    message.value = "";
    if (needsLogin(page.value) && !account.value) {
      const redirect = encodeURIComponent(path.value + window.location.search);
      history.replaceState({}, "", `/auth.html?redirect=${redirect}`);
      path.value = "/auth.html";
      return;
    }
    refreshPage();
  }

  function needsLogin(name) {
    return !["home", "auth"].includes(name);
  }

  function logout() {
    clearSession();
    account.value = null;
    go("/index.html");
  }

  async function submitAuth() {
    if (auth.mode === "forgot" || auth.mode === "reset") return;
    loading.value = true;
    message.value = "";
    try {
      const endpoint = auth.mode === "register" ? "/api/auth/register" : "/api/auth/login";
      const data = await apiFetch(endpoint, {
        method: "POST",
        body: { email: auth.email, username: auth.username, password: auth.password }
      });
      saveSession(data);
      account.value = data.user;
      const redirect = new URLSearchParams(location.search).get("redirect");
      go(redirect && redirect.startsWith("/") ? redirect : "/dashboard.html");
    } catch (error) {
      message.value = error.message;
    } finally {
      loading.value = false;
    }
  }

  function openForgotPassword() {
    auth.mode = "forgot";
    auth.password = "";
    auth.confirmPassword = "";
    auth.resetToken = "";
    message.value = "";
  }

  async function submitForgotPassword() {
    loading.value = true;
    message.value = "";
    try {
      const data = await apiFetch("/api/auth/forgot-password", {
        method: "POST",
        body: { email: auth.email }
      });
      if (data.resetToken) {
        auth.resetToken = data.resetToken;
        auth.mode = "reset";
        auth.password = "";
        auth.confirmPassword = "";
        message.value = data.message || "请设置新密码完成重置。";
      } else {
        message.value = data.message || "如果邮箱存在，请按提示完成重置。";
      }
    } catch (error) {
      message.value = error.message;
    } finally {
      loading.value = false;
    }
  }

  async function submitResetPassword() {
    if (auth.password !== auth.confirmPassword) {
      message.value = "两次输入的新密码不一致";
      return;
    }
    if ((auth.password || "").length < 8) {
      message.value = "新密码至少 8 位";
      return;
    }
    loading.value = true;
    message.value = "";
    try {
      const data = await apiFetch("/api/auth/reset-password", {
        method: "POST",
        body: {
          email: auth.email,
          token: auth.resetToken,
          newPassword: auth.password
        }
      });
      auth.mode = "login";
      auth.password = "";
      auth.confirmPassword = "";
      auth.resetToken = "";
      message.value = data.message || "密码已重置，请登录。";
    } catch (error) {
      message.value = error.message;
    } finally {
      loading.value = false;
    }
  }

  async function refreshDashboard() {
    if (!account.value) return;
    const [projects, tasks, quota] = await Promise.all([
      apiFetch("/api/projects").catch(() => ({ projects: [] })),
      apiFetch("/api/tasks").catch(() => ({ tasks: [] })),
      apiFetch("/api/ai/quota").catch(() => ({ quota: null }))
    ]);
    dashboard.projects = projects.projects || [];
    dashboard.tasks = tasks.tasks || [];
    dashboard.quota = quota.quota;
  }

  async function refreshProjects() {
    if (!account.value) return;
    const data = await apiFetch("/api/projects").catch(() => ({ projects: [] }));
    const previousProjectId = projects.selectedId;
    projects.rows = data.projects || [];
    dashboard.projects = projects.rows;
    if (!projects.selectedId && projects.rows.length) projects.selectedId = projects.rows[0].id;
    if (projects.selectedId && !projects.rows.some((project) => project.id === projects.selectedId)) {
      projects.selectedId = projects.rows[0]?.id || "";
    }
    if (previousProjectId !== projects.selectedId) syncExportSplitFromProject();
    await loadProjectLabels();
  }

  async function createProject() {
    const name = projects.newName.trim();
    if (!name) {
      projects.status = "请输入项目名称";
      return;
    }
    const data = await apiFetch("/api/projects", {
      method: "POST",
      body: {
        name,
        description: "",
        dataType: normalizeProjectDataType(projects.newDataType),
        split: { train: 70, val: 20, test: 10 }
      }
    });
    projects.newName = "";
    projects.selectedId = data.project.id;
    projects.status = `项目 ${data.project.name} 已创建`;
    await refreshProjects();
    await refreshFiles();
  }

  async function copyCurrentProject() {
    if (!projects.selectedId || !selectedProject.value) {
      projects.status = "请先选择要复制的项目";
      return;
    }
    if (!canLeaveCurrentAnnotation()) return;
    const defaultName = `${selectedProject.value.name} 副本`;
    const name = window.prompt("请输入复制后的新项目名称", defaultName);
    if (name === null) return;
    const cleanName = name.trim();
    if (!cleanName) {
      projects.status = "复制项目名称不能为空";
      return;
    }
    loading.value = true;
    projects.status = `正在复制项目 ${selectedProject.value.name}...`;
    try {
      const data = await apiFetch(`/api/projects/${projects.selectedId}/copy`, {
        method: "POST",
        body: { name: cleanName }
      });
      if (data.user) {
        account.value = data.user;
        saveSession({ ...session(), user: data.user });
      }
      projects.selectedId = data.project.id;
      projects.status = `项目已复制为 ${data.project.name}`;
      clearAnnotation();
      annotate.current = null;
      await refreshProjects();
      await refreshFiles();
      if (page.value === "export") await refreshExports();
    } catch (error) {
      projects.status = error.message;
    } finally {
      loading.value = false;
    }
  }

  function syncExportSplitFromProject() {
    const split = selectedProject.value?.split;
    if (split) {
      exportPage.split.train = split.train;
      exportPage.split.val = split.val;
      exportPage.split.test = split.test;
    }
  }

  async function selectProject(projectId) {
    if (projectId !== projects.selectedId && !canLeaveCurrentAnnotation()) return;
    annotationSelection++;
    projects.selectedId = projectId;
    syncExportSplitFromProject();
    clearAnnotation();
    annotate.current = null;
    await loadProjectLabels();
    await refreshFiles();
    if (page.value === "export") await refreshExports();
  }

  async function deleteCurrentProject() {
    if (!projects.selectedId) return;
    const projectName = selectedProject.value?.name || "当前项目";
    await apiFetch(`/api/projects/${projects.selectedId}`, { method: "DELETE" });
    projects.status = `项目 ${projectName} 已删除`;
    if (annotate.preview) URL.revokeObjectURL(annotate.preview);
    annotate.preview = "";
    annotate.current = null;
    clearAnnotation();
    await refreshProjects();
    await refreshFiles();
  }

  async function loadProjectLabels() {
    if (!projects.selectedId) {
      annotate.labels = [];
      annotate.defaultRunLabel = "";
      return;
    }
    const projectId = projects.selectedId;
    const data = await apiFetch(`/api/projects/${projectId}/labels`).catch(() => ({ labels: [] }));
    if (projectId !== projects.selectedId) return;
    annotate.labels = data.labels ?? [];
    syncDefaultRunLabel();
  }

  function isUnassignedLabel(label) {
    return label === null || label === undefined || String(label).trim() === "";
  }

  function labelsEqual(a, b) {
    if (isUnassignedLabel(a) && isUnassignedLabel(b)) return true;
    if (isUnassignedLabel(a) || isUnassignedLabel(b)) return false;
    return String(a).trim().toLowerCase() === String(b).trim().toLowerCase();
  }

  function findLabelIndex(label) {
    if (isUnassignedLabel(label)) return -1;
    const normalized = String(label).trim().toLowerCase();
    return annotate.labels.findIndex((item) => item.toLowerCase() === normalized);
  }

  function syncDefaultRunLabel() {
    if (!annotate.labels.length) {
      annotate.defaultRunLabel = "";
      return;
    }
    const currentIndex = findLabelIndex(annotate.defaultRunLabel);
    if (currentIndex >= 0) return;
    annotate.defaultRunLabel = annotate.labels[0];
  }

  function isExportableAnnotation(item) {
    return !isUnassignedLabel(item?.label) && Number(item?.classId) >= 0;
  }

  function formatAnnotationLabel(label) {
    return isUnassignedLabel(label) ? "未分配" : label;
  }

  function isAnnotationConfirmed(item) {
    return Boolean(item?.confirmed ?? item?.Confirmed);
  }

  function normalizeAnnotationItem(item) {
    const normalized = { ...item };
    normalized.confirmed = isAnnotationConfirmed(item);
    return normalized;
  }

  async function refreshFiles() {
    if (!account.value) return;
    if (!projects.selectedId && ["files", "annotate", "export"].includes(page.value)) {
      files.rows = [];
      return;
    }
    const projectId = projects.selectedId;
    const query = projectId ? `?projectId=${encodeURIComponent(projectId)}` : "";
    const data = await apiFetch(`/api/files${query}`).catch(() => ({ files: [] }));
    if (projectId !== projects.selectedId) return;
    files.rows = data.files || [];
    if (data.user) {
      account.value = data.user;
      saveSession({ ...session(), user: data.user });
    }
  }

  async function refreshRecords() {
    if (!account.value) return;
    const data = await apiFetch("/api/tasks").catch(() => ({ tasks: [] }));
    records.rows = data.tasks || [];
  }

  const uploadQueue = reactive({
    active: false,
    total: 0,
    done: 0,
    failed: 0,
    skipped: 0,
    percent: 0,
    currentName: "",
    items: []
  });

  const uploadDuplicatePrompt = reactive({
    visible: false,
    duplicateNames: [],
    newCount: 0,
    resolve: null
  });

  function normalizeFileName(name) {
    return String(name || "").trim().toLowerCase();
  }

  function getDuplicateUploadNames(fileList) {
    const existing = new Set(files.rows.map((file) => normalizeFileName(file.name)));
    return [...new Set(Array.from(fileList).filter((file) => existing.has(normalizeFileName(file.name))).map((file) => file.name))];
  }

  function openDuplicateUploadPrompt(duplicateNames, newCount) {
    return new Promise((resolve) => {
      uploadDuplicatePrompt.visible = true;
      uploadDuplicatePrompt.duplicateNames = duplicateNames;
      uploadDuplicatePrompt.newCount = newCount;
      uploadDuplicatePrompt.resolve = resolve;
    });
  }

  function resolveDuplicateUpload(action) {
    const resolve = uploadDuplicatePrompt.resolve;
    uploadDuplicatePrompt.visible = false;
    uploadDuplicatePrompt.resolve = null;
    uploadDuplicatePrompt.duplicateNames = [];
    uploadDuplicatePrompt.newCount = 0;
    resolve?.(action);
  }

  async function beginSequentialUpload(fileList, projectId) {
    await refreshFiles();
    const items = Array.from(fileList);
    const duplicateNames = getDuplicateUploadNames(items);
    let skipNames = new Set();

    if (duplicateNames.length) {
      const duplicateSet = new Set(duplicateNames.map(normalizeFileName));
      const newCount = items.filter((file) => !duplicateSet.has(normalizeFileName(file.name))).length;
      const action = await openDuplicateUploadPrompt(duplicateNames, newCount);
      if (action === "cancel") return { cancelled: true, uploaded: [] };
      if (action === "skip") skipNames = duplicateSet;
    }

    const uploaded = await uploadFilesSequentially(items, projectId, { skipNames });
    return { cancelled: false, uploaded };
  }

  function resetUploadQueue(fileList) {
    const items = Array.from(fileList);
    uploadQueue.active = true;
    uploadQueue.total = items.length;
    uploadQueue.done = 0;
    uploadQueue.failed = 0;
    uploadQueue.skipped = 0;
    uploadQueue.percent = 0;
    uploadQueue.currentName = "";
    uploadQueue.items = items.map((file, index) => ({
      id: `${Date.now()}-${index}`,
      name: file.name,
      size: file.size,
      status: "pending",
      error: ""
    }));
  }

  function refreshUploadProgress() {
    uploadQueue.percent = uploadQueue.total
      ? Math.round(((uploadQueue.done + uploadQueue.failed + uploadQueue.skipped) / uploadQueue.total) * 100)
      : 0;
  }

  function uploadQueueStatusLabel(item) {
    if (item.status === "pending") return "等待";
    if (item.status === "uploading") return "上传中";
    if (item.status === "done") return "完成";
    if (item.status === "skipped") return "已跳过";
    return "失败";
  }

  function buildUploadSummary(uploaded, total) {
    const parts = [`成功 ${uploaded.length} 张`];
    if (uploadQueue.skipped) parts.push(`跳过 ${uploadQueue.skipped} 张`);
    if (uploadQueue.failed) parts.push(`失败 ${uploadQueue.failed} 张`);
    return `上传完成：${parts.join("，")}（共 ${total} 张）`;
  }

  async function uploadSingleFile(file, projectId) {
    const form = new FormData();
    form.append("projectId", projectId);
    form.append("files", file);
    return apiFetch("/api/files/upload", { method: "POST", body: form });
  }

  async function uploadFilesSequentially(fileList, projectId, options = {}) {
    const skipNames = options.skipNames || new Set();
    resetUploadQueue(fileList);
    const items = Array.from(fileList);
    const uploaded = [];
    for (let index = 0; index < items.length; index += 1) {
      const file = items[index];
      const queueItem = uploadQueue.items[index];
      if (skipNames.has(normalizeFileName(file.name))) {
        queueItem.status = "skipped";
        queueItem.error = "服务器已存在同名文件";
        uploadQueue.skipped += 1;
        refreshUploadProgress();
        continue;
      }
      queueItem.status = "uploading";
      uploadQueue.currentName = file.name;
      try {
        const data = await uploadSingleFile(file, projectId);
        if (data.user) {
          account.value = data.user;
          saveSession({ ...session(), user: data.user });
        }
        const saved = data.files?.[0];
        if (saved) uploaded.push(saved);
        queueItem.status = "done";
        uploadQueue.done += 1;
      } catch (error) {
        queueItem.status = "failed";
        queueItem.error = error.message;
        uploadQueue.failed += 1;
      }
      refreshUploadProgress();
    }
    uploadQueue.active = false;
    uploadQueue.currentName = "";
    return uploaded;
  }

  async function uploadFiles() {
    if (!files.selected?.length) return;
    if (!projects.selectedId) {
      projects.status = "请先选择或创建项目";
      return;
    }
    loading.value = true;
    projects.status = "正在上传图片";
    try {
      const total = files.selected.length;
      const result = await beginSequentialUpload(files.selected, projects.selectedId);
      if (result.cancelled) {
        projects.status = "已取消上传";
        return;
      }
      projects.status = uploadQueue.skipped || uploadQueue.failed
        ? buildUploadSummary(result.uploaded, total)
        : `已上传 ${result.uploaded.length} / ${total} 张图片`;
      await refreshFiles();
    } catch (error) {
      projects.status = error.message;
    } finally {
      loading.value = false;
    }
  }

  async function deleteFile(fileId) {
    const deletedIndex = files.rows.findIndex((file) => file.id === fileId);
    const wasCurrent = annotate.current?.id === fileId;
    const data = await apiFetch(`/api/files/${fileId}`, { method: "DELETE" });
    if (data.user) {
      account.value = data.user;
      saveSession({ ...session(), user: data.user });
    }
    await refreshFiles();
    if (wasCurrent) {
      const nextFile = files.rows[Math.min(deletedIndex, files.rows.length - 1)] || files.rows[deletedIndex - 1] || null;
      if (nextFile) {
        await selectAnnotateFile(nextFile);
      } else {
        annotate.current = null;
        clearAnnotation();
        annotate.status = "图片已删除，当前项目暂无图片";
      }
    }
  }

  async function runSegment() {
    if (!segment.file) {
      segment.status = "请先选择一张图片";
      return;
    }
    if (segment.promptMode === "points") {
      segment.status = "点提示模式下请直接在图片上点击，无需点「开始分割」";
      return;
    }
    loading.value = true;
    segment.status = "AI 正在分割";
    segment.overlay = "";
    segment.overlays = {};
    segment.activeOverlay = "all";
    segment.categories = [];
    segment.warning = "";
    segment.mode = "";
    segment.count = 0;
    try {
      const form = new FormData();
      form.append("image", segment.file);
      form.append("prompt", segment.promptMode === "text" ? segment.prompt : "");
      form.append("conf", String(segment.conf));
      form.append("half", "true");
      const data = await apiFetch("/api/segment", { method: "POST", body: form });
      segment.overlays = data.overlays || {};
      segment.activeOverlay = "all";
      segment.overlay = data.overlay || segment.overlays.all || "";
      segment.categories = data.categories || [];
      segment.mode = data.mode || (segment.prompt ? "text" : "auto");
      segment.count = data.count || 0;
      segment.warning = data.warning || "";
      segment.status = segment.warning
        ? `已完成，识别出 ${segment.count} 个目标；自动分类失败，已返回分割结果`
        : `已完成，识别出 ${segment.count} 个目标`;
    } catch (error) {
      segment.status = error.message;
    } finally {
      loading.value = false;
    }
  }

  function resetSegmentPointDraft() {
    Object.assign(segment.pointDraft, createEmptyPointDraft());
  }

  function resetAnnotatePointDraft() {
    Object.assign(annotate.pointDraft, createEmptyPointDraft());
  }

  function selectSegmentFile(file) {
    if (segment.preview) URL.revokeObjectURL(segment.preview);
    segment.file = file || null;
    segment.preview = file ? URL.createObjectURL(file) : "";
    segment.overlay = "";
    segment.overlays = {};
    segment.activeOverlay = "all";
    segment.categories = [];
    segment.warning = "";
    segment.mode = "";
    segment.count = 0;
    segment.width = 0;
    segment.height = 0;
    segment.confirmed = [];
    segment.activeConfirmedId = "";
    resetSegmentPointDraft();
    segment.status = file
      ? segment.promptMode === "points"
        ? "图片已选择，点击目标内部添加正向点"
        : "图片已选择，可以开始分割"
      : "准备就绪";
  }

  function onSegmentImageLoad(event) {
    const image = event?.target;
    if (!image?.naturalWidth) return;
    segment.width = image.naturalWidth;
    segment.height = image.naturalHeight;
  }

  function setSegmentPromptMode(mode) {
    segment.promptMode = mode;
    resetSegmentPointDraft();
    if (mode === "points") {
      segment.overlay = "";
      segment.status = segment.file ? "点提示模式：左键正向点，右键负向点" : "请先选择图片";
    } else {
      segment.status = segment.file ? "图片已选择，可以开始分割" : "准备就绪";
    }
  }

  function showSegmentOverlay(key) {
    if (!segment.overlays?.[key]) return;
    segment.activeOverlay = key;
    segment.overlay = segment.overlays[key];
  }

  function pickLargestYoloSegment(candidate) {
    const segments = candidate?.yoloSegments;
    if (!Array.isArray(segments) || !segments.length) return null;
    let best = segments[0];
    let bestLen = Array.isArray(best) ? best.length : 0;
    for (const item of segments) {
      if (Array.isArray(item) && item.length > bestLen) {
        best = item;
        bestLen = item.length;
      }
    }
    return Array.isArray(best) && best.length >= 6 ? best.map(Number) : null;
  }

  function activePointCandidate(draft) {
    if (!draft?.candidates?.length) return null;
    const index = Math.max(0, Math.min(draft.candidates.length - 1, draft.activeCandidate || 0));
    return draft.candidates[index] || null;
  }

  function applyPointPromptResult(draft, data, requestId) {
    if (requestId !== draft.requestId) return false;
    draft.candidates = data.candidates || [];
    draft.activeCandidate = 0;
    draft.overlay = draft.candidates[0]?.overlay || data.overlay || "";
    draft.loading = false;
    return true;
  }

  let segmentPointRequestChain = Promise.resolve();
  let annotatePointRequestChain = Promise.resolve();

  async function requestSegmentPointPrompt() {
    if (!segment.file || !segment.pointDraft.points.length) return;
    if (!segment.pointDraft.points.some((point) => point.label === 1)) {
      segment.status = "请至少添加一个正向点";
      return;
    }

    const snapshotPoints = segment.pointDraft.points.map((point) => ({ ...point }));
    const requestId = ++segment.pointDraft.requestId;
    segment.pointDraft.loading = true;
    segment.status = "AI 正在根据提示点分割...";

    const run = async () => {
      // Only the latest click should hit the GPU; skip superseded snapshots.
      if (requestId !== segment.pointDraft.requestId) return;
      try {
        const form = new FormData();
        form.append("image", segment.file);
        form.append("points", JSON.stringify(snapshotPoints.map((point) => [point.x, point.y])));
        form.append("labels", JSON.stringify(snapshotPoints.map((point) => point.label)));
        form.append("conf", String(segment.conf));
        const data = await apiFetch("/api/segment/points", { method: "POST", body: form });
        if (!applyPointPromptResult(segment.pointDraft, data, requestId)) return;
        if (data.width) segment.width = data.width;
        if (data.height) segment.height = data.height;
        segment.activeConfirmedId = "";
        segment.overlay = segment.pointDraft.overlay;
        segment.mode = "points";
        segment.count = data.count || segment.pointDraft.candidates.length;
        segment.status = segment.pointDraft.candidates.length
          ? "当前目标 Mask 已更新，可继续加点精修，或确认目标"
          : `已生成 ${segment.pointDraft.candidates.length} 个候选 Mask，可切换或继续加点`;
      } catch (error) {
        if (requestId !== segment.pointDraft.requestId) return;
        segment.pointDraft.loading = false;
        segment.status = error.message;
      }
    };

    segmentPointRequestChain = segmentPointRequestChain.then(run, run);
    await segmentPointRequestChain;
  }

  async function requestAnnotatePointPrompt() {
    if (!annotate.current?.id || !annotate.pointDraft.points.length) return;
    if (!annotate.pointDraft.points.some((point) => point.label === 1)) {
      annotate.status = "请至少添加一个正向点";
      return;
    }

    const snapshotPoints = annotate.pointDraft.points.map((point) => ({ ...point }));
    const fileId = annotate.current.id;
    const requestId = ++annotate.pointDraft.requestId;
    annotate.pointDraft.loading = true;
    annotate.status = "AI 正在根据提示点分割...";

    const run = async () => {
      if (requestId !== annotate.pointDraft.requestId) return;
      try {
        const data = await apiFetch("/api/annotations/points", {
          method: "POST",
          body: {
            fileId,
            points: snapshotPoints.map((point) => [point.x, point.y]),
            labels: snapshotPoints.map((point) => point.label),
            conf: Number(annotate.conf)
          }
        });
        if (!applyPointPromptResult(annotate.pointDraft, data, requestId)) return;
        if (data.width) annotate.width = data.width;
        if (data.height) annotate.height = data.height;
        annotate.status = "当前目标 Mask 已更新，可继续加点精修，或确认目标";
      } catch (error) {
        if (requestId !== annotate.pointDraft.requestId) return;
        annotate.pointDraft.loading = false;
        annotate.status = error.message;
      }
    };

    annotatePointRequestChain = annotatePointRequestChain.then(run, run);
    await annotatePointRequestChain;
  }

  function pointerToPixelPoint(event, width, height) {
    const normalized = pointerToYoloPoint(event);
    if (!normalized || !width || !height) return null;
    return {
      x: normalized.x * width,
      y: normalized.y * height
    };
  }

  function addPromptPoint(draft, pixel, label) {
    draft.points.push({
      id: `pt_${Date.now()}_${Math.random().toString(16).slice(2, 6)}`,
      x: pixel.x,
      y: pixel.y,
      label
    });
  }

  async function handleSegmentPointClick(event) {
    if (segment.promptMode !== "points" || !segment.file) return;
    if (event.button !== undefined && event.button !== 0 && event.button !== 2) return;
    event.preventDefault();
    const width = segment.width || event.currentTarget?.querySelector?.("img")?.naturalWidth;
    const height = segment.height || event.currentTarget?.querySelector?.("img")?.naturalHeight;
    const pixel = pointerToPixelPoint(event, width, height);
    if (!pixel) return;
    const label = event.button === 2 || event.shiftKey ? 0 : segment.pointPolarity;
    addPromptPoint(segment.pointDraft, pixel, label);
    await requestSegmentPointPrompt();
  }

  async function handleAnnotatePointClick(event) {
    if (!annotate.pointMode || annotate.drawMode || !annotate.current || !annotate.ready) return;
    if (event.button !== undefined && event.button !== 0 && event.button !== 2) return;
    event.preventDefault();
    const width = annotate.width || event.currentTarget?.querySelector?.("img")?.naturalWidth;
    const height = annotate.height || event.currentTarget?.querySelector?.("img")?.naturalHeight;
    const pixel = pointerToPixelPoint(event, width, height);
    if (!pixel) {
      annotate.status = "无法定位点击坐标，请等图片加载完成";
      return;
    }
    if (!annotate.width && width) annotate.width = width;
    if (!annotate.height && height) annotate.height = height;
    const label = event.button === 2 || event.shiftKey ? 0 : annotate.pointPolarity;
    addPromptPoint(annotate.pointDraft, pixel, label);
    await requestAnnotatePointPrompt();
  }

  function selectSegmentPointCandidate(index) {
    if (!segment.pointDraft.candidates[index]) return;
    segment.pointDraft.activeCandidate = index;
    segment.pointDraft.overlay = segment.pointDraft.candidates[index].overlay || "";
    segment.overlay = segment.pointDraft.overlay;
  }

  function selectAnnotatePointCandidate(index) {
    if (!annotate.pointDraft.candidates[index]) return;
    annotate.pointDraft.activeCandidate = index;
    annotate.pointDraft.overlay = annotate.pointDraft.candidates[index].overlay || "";
  }

  function clearSegmentPointDraft() {
    resetSegmentPointDraft();
    segment.overlay = "";
    segment.status = "已清空提示点，可重新点击当前目标";
  }

  function clearAnnotatePointDraft() {
    resetAnnotatePointDraft();
    annotate.status = "已清空提示点，可重新点击当前目标";
  }

  function segmentCanvasSrc() {
    if (segment.promptMode !== "points") {
      return segment.overlay || segment.preview || "";
    }
    if (segment.pointDraft.overlay) return segment.pointDraft.overlay;
    if (segment.activeConfirmedId) {
      const selected = segment.confirmed.find((item) => item.id === segment.activeConfirmedId);
      if (selected?.overlay) return selected.overlay;
    }
    return segment.overlay || segment.preview || "";
  }

  function previewConfirmedSegmentTarget(targetId) {
    const selected = segment.confirmed.find((item) => item.id === targetId);
    if (!selected?.overlay) {
      segment.status = "该目标没有可预览的 Mask";
      return;
    }
    segment.activeConfirmedId = targetId;
    segment.overlay = selected.overlay;
    // Leave draft points as-is, but hide draft overlay so confirmed preview wins.
    segment.pointDraft.overlay = "";
    segment.pointDraft.candidates = [];
    segment.pointDraft.activeCandidate = 0;
    const index = segment.confirmed.findIndex((item) => item.id === targetId);
    segment.status = `正在预览已确认目标 ${index + 1}`;
  }

  function startNewSegmentPointTarget() {
    resetSegmentPointDraft();
    segment.activeConfirmedId = "";
    segment.overlay = "";
    segment.status = "已开始新目标：请点击正向点开始抠下一个物体";
  }

  function startNewAnnotatePointTarget() {
    resetAnnotatePointDraft();
    annotate.status = "已开始新目标：请点击正向点开始抠下一个物体";
  }

  function confirmSegmentPointTarget() {
    const candidate = activePointCandidate(segment.pointDraft);
    if (!candidate) {
      segment.status = "请先通过点击生成 Mask";
      return;
    }
    segment.confirmed.push({
      id: `seg_${Date.now()}_${Math.random().toString(16).slice(2, 6)}`,
      score: candidate.score,
      overlay: candidate.overlay,
      yoloBox: candidate.yoloBox,
      yoloSegments: candidate.yoloSegments,
      area: candidate.area
    });
    const confirmedId = segment.confirmed.at(-1).id;
    resetSegmentPointDraft();
    segment.activeConfirmedId = confirmedId;
    segment.overlay = candidate.overlay || "";
    segment.status = `已确认目标 ${segment.confirmed.length}。点击右侧列表可预览；要抠下一个请先点「新建目标」`;
  }

  function confirmAnnotatePointTarget() {
    const candidate = activePointCandidate(annotate.pointDraft);
    if (!candidate?.yoloBox) {
      annotate.status = "请先通过点击生成 Mask";
      return;
    }
    const segmentCoords = pickLargestYoloSegment(candidate);
    const label = manualAnnotationLabel();
    const item = normalizeAnnotationItem({
      id: `point_${Date.now()}_${Math.random().toString(16).slice(2, 8)}`,
      classId: findLabelIndex(label),
      label,
      bbox: {
        cx: Number(candidate.yoloBox.cx),
        cy: Number(candidate.yoloBox.cy),
        width: Number(candidate.yoloBox.width),
        height: Number(candidate.yoloBox.height)
      },
      segment: segmentCoords,
      confidence: Number(candidate.score || 1),
      confirmed: false
    });
    annotate.annotations.push(item);
    annotate.activeId = item.id;
    resetAnnotatePointDraft();
    markAnnotationDirty(
      segmentCoords
        ? label
          ? `已确认 ${label}（框+掩膜）。下一个物体请先点「新建目标」`
          : "已确认目标（框+掩膜）。下一个物体请先点「新建目标」"
        : label
          ? `已确认 ${label}（仅框）。下一个物体请先点「新建目标」`
          : "已确认目标（仅框）。下一个物体请先点「新建目标」"
    );
  }

  function togglePointPromptMode() {
    annotate.pointMode = !annotate.pointMode;
    if (annotate.pointMode) {
      annotate.drawMode = false;
      annotate.drawingBox = null;
      annotate.status = "点提示：多个正负点精修同一个目标；确认后需「新建目标」再抠下一个";
    } else {
      resetAnnotatePointDraft();
      annotate.status = "点提示模式已关闭";
    }
  }

  function setAnnotatePointPolarity(value) {
    annotate.pointPolarity = value === 0 ? 0 : 1;
  }

  function setSegmentPointPolarity(value) {
    segment.pointPolarity = value === 0 ? 0 : 1;
  }

  function promptPointStyle(point, width, height) {
    if (!width || !height) return { display: "none" };
    return {
      left: `${(point.x / width) * 100}%`,
      top: `${(point.y / height) * 100}%`
    };
  }

  function activeAnnotatePointCandidate() {
    return activePointCandidate(annotate.pointDraft);
  }

  function activeSegmentPointCandidate() {
    return activePointCandidate(segment.pointDraft);
  }

  function candidatePolygonPoints(candidate) {
    const segmentCoords = pickLargestYoloSegment(candidate);
    if (!segmentCoords?.length) return "";
    const points = [];
    for (let i = 0; i < segmentCoords.length - 1; i += 2) {
      points.push(`${segmentCoords[i] * 100},${segmentCoords[i + 1] * 100}`);
    }
    return points.join(" ");
  }

  function applyAnnotation(annotation, options = {}) {
    if (annotation.fileId !== annotate.current?.id) return;
    annotate.fileId = annotation.fileId;
    annotate.width = annotation.width || 0;
    annotate.height = annotation.height || 0;
    annotate.annotations = (annotation.annotations || []).map(normalizeAnnotationItem);
    annotate.activeId = annotate.annotations[0]?.id || "";
    normalizeAnnotationLabels();
    if (options.status) {
      markAnnotationSaved(options.status);
    } else if (annotate.annotations.length) {
      markAnnotationSaved(`已加载 ${annotate.annotations.length} 条标注`);
    } else {
      markAnnotationSaved("暂无标注");
    }
  }

  function clearAnnotation() {
    annotate.ready = false;
    annotate.annotations = [];
    annotate.activeId = "";
    annotate.width = 0;
    annotate.height = 0;
    annotate.dirty = false;
  }

  async function persistBatchLabels() {
    if (!projects.selectedId) return annotate.labels;
    try {
      const data = await apiFetch(`/api/projects/${projects.selectedId}/labels`, {
        method: "PUT",
        body: { labels: annotate.labels }
      });
      annotate.labels = data.labels ?? annotate.labels;
      syncDefaultRunLabel();
      return annotate.labels;
    } catch (error) {
      annotate.status = error.message;
      throw error;
    }
  }

  function normalizeAnnotationLabels(items = annotate.annotations) {
    for (const item of items) {
      if (isUnassignedLabel(item.label)) {
        item.label = null;
        item.classId = -1;
        continue;
      }
      const classId = findLabelIndex(item.label);
      if (classId < 0) {
        item.label = null;
        item.classId = -1;
        continue;
      }
      item.label = annotate.labels[classId];
      item.classId = classId;
    }
    return items;
  }

  function syncAnnotationLabels() {
    normalizeAnnotationLabels();
    markAnnotationDirty("标签已更新，记得保存");
  }

  async function syncBatchLabelsFromAnnotations() {
    for (const item of annotate.annotations) {
      const label = isUnassignedLabel(item.label) ? "" : String(item.label).trim();
      if (label && findLabelIndex(label) < 0) annotate.labels.push(label);
    }
    await persistBatchLabels();
  }

  async function addAnnotateLabel() {
    const label = annotate.newLabel.trim();
    if (!label) return;
    if (findLabelIndex(label) < 0) annotate.labels.push(label);
    if (!annotate.labels.length || annotate.labels.length === 1) annotate.defaultRunLabel = label;
    await persistBatchLabels();
    annotate.newLabel = "";
  }

  function beginDeleteAnnotateLabel(label) {
    annotate.pendingDeleteLabel = label;
    const others = annotate.labels.filter((item) => item !== label);
    annotate.labelDeleteReplace = others[0] ?? "";
  }

  function cancelDeleteAnnotateLabel() {
    annotate.pendingDeleteLabel = "";
    annotate.labelDeleteReplace = "";
  }

  async function confirmDeleteAnnotateLabel() {
    const label = annotate.pendingDeleteLabel;
    if (!label || !projects.selectedId) return;
    const replaceWith = annotate.labelDeleteReplace;
    loading.value = true;
    annotate.status = `正在从项目中删除标签 ${label}`;
    try {
      const query = replaceWith ? `?replaceWith=${encodeURIComponent(replaceWith)}` : "";
      const data = await apiFetch(
        `/api/projects/${projects.selectedId}/labels/${encodeURIComponent(label)}${query}`,
        { method: "DELETE" }
      );
      annotate.labels = data.labels ?? [];
      if (annotate.defaultRunLabel === label) {
        annotate.defaultRunLabel = annotate.labels[0] ?? "";
      }
      syncDefaultRunLabel();
      if (annotate.current?.id) {
        try {
          const current = await apiFetch(`/api/annotations/file/${annotate.current.id}`);
          applyAnnotation(current.annotation);
        } catch {
          normalizeAnnotationLabels();
        }
      } else {
        normalizeAnnotationLabels();
      }
      await refreshFiles();
      annotate.pendingDeleteLabel = "";
      annotate.labelDeleteReplace = "";
      const tail = replaceWith ? `已替换为 ${replaceWith}` : "相关目标已标为未分配";
      annotate.status = `标签 ${label} 已删除，${tail}`;
    } catch (error) {
      annotate.status = error.message;
    } finally {
      loading.value = false;
    }
  }

  function applyLabelToActive(label) {
    const active = annotate.annotations.find((item) => item.id === annotate.activeId);
    if (!active) {
      annotate.status = "请先在标注结果中选中一个目标";
      return;
    }
    active.label = label;
    normalizeAnnotationLabels();
    if (isExportableAnnotation(active)) active.confirmed = true;
    markAnnotationDirty(`已将选中目标设置为 ${label}`);
  }

  function applyAnnotationLabel(item, value) {
    item.label = value || null;
    normalizeAnnotationLabels([item]);
    if (isExportableAnnotation(item)) item.confirmed = true;
    markAnnotationDirty(isExportableAnnotation(item) ? "标签已更新并标记为人工已确认，记得保存" : "标签已更新，记得保存");
  }

  async function replaceLabelInBatch(fromLabel, toLabel) {
    const annotatedFiles = files.rows.filter((file) => file.annotated || file.id === annotate.current?.id);
    for (const file of annotatedFiles) {
      let annotation;
      if (file.id === annotate.current?.id) {
        annotation = {
          fileId: file.id,
          width: annotate.width,
          height: annotate.height,
          annotations: annotate.annotations.map((item) => ({ ...item }))
        };
      } else {
        try {
          const data = await apiFetch(`/api/annotations/file/${file.id}`);
          annotation = data.annotation;
        } catch {
          continue;
        }
      }
      const nextItems = (annotation.annotations || []).map((item) =>
        normalizeAnnotationItem({
          ...item,
          label: labelsEqual(item.label, fromLabel) ? toLabel : item.label
        })
      );
      normalizeAnnotationLabels(nextItems);
      const saved = await apiFetch(`/api/annotations/file/${file.id}`, {
        method: "PUT",
        body: { fileId: file.id, width: annotation.width, height: annotation.height, annotations: nextItems }
      });
      if (file.id === annotate.current?.id) applyAnnotation(saved.annotation);
    }
    await refreshFiles();
  }

  async function changeAnnotateFiles(fileList) {
    annotate.selected = fileList;
    await uploadAnnotateFiles();
  }

  async function uploadAnnotateFiles() {
    if (!annotate.selected?.length) return;
    if (!projects.selectedId) {
      annotate.status = "请先选择或创建项目";
      return;
    }
    loading.value = true;
    annotate.status = "正在上传图片";
    try {
      const total = annotate.selected.length;
      const result = await beginSequentialUpload(annotate.selected, projects.selectedId);
      if (result.cancelled) {
        annotate.status = "已取消上传";
        return;
      }
      await refreshFiles();
      const first = result.uploaded[0];
      if (first) await selectAnnotateFile(first);
      annotate.status = uploadQueue.skipped || uploadQueue.failed
        ? `${buildUploadSummary(result.uploaded, total)}，未自动标注`
        : `已上传 ${result.uploaded.length} / ${total} 张图片，未自动标注`;
    } catch (error) {
      annotate.status = error.message;
    } finally {
      loading.value = false;
    }
  }

  async function loadAnnotatePreview(file, selection) {
    if (annotate.preview) {
      URL.revokeObjectURL(annotate.preview);
      annotate.preview = "";
    }
    annotate.frame = { width: 0, height: 0 };
    if (!file?.downloadUrl) return;
    const response = await fetch(file.downloadUrl, { headers: authHeaders() });
    if (!response.ok) throw new Error("图片预览加载失败");
    const blob = await response.blob();
    if (selection !== annotationSelection || file.id !== annotate.current?.id) return;
    annotate.preview = URL.createObjectURL(blob);
    await nextTick();
  }

  const filteredFiles = computed(() => {
    if (annotate.filter === "annotated") return files.rows.filter((file) => file.annotated);
    if (annotate.filter === "unannotated") return files.rows.filter((file) => !file.annotated);
    return files.rows;
  });
  const currentFileIndex = computed(() => files.rows.findIndex((file) => file.id === annotate.current?.id));
  const activeAnnotation = computed(() => annotate.annotations.find((item) => item.id === annotate.activeId) || null);
  const canRunAnnotateAi = computed(() => annotate.labels.length > 0);
  const annotationStats = computed(() => {
    const total = annotate.annotations.length;
    const confirmed = annotate.annotations.filter((item) => isAnnotationConfirmed(item)).length;
    const unassigned = annotate.annotations.filter((item) => isUnassignedLabel(item.label)).length;
    return {
      total,
      confirmed,
      pending: Math.max(0, total - confirmed),
      unassigned
    };
  });
  const reviewFilterActive = computed(() => Object.values(annotate.reviewFilters).some((value) => String(value ?? "").trim() !== ""));
  const reviewFilterMatchedAnnotations = computed(() => {
    if (!reviewFilterActive.value) return [];
    return annotate.annotations.filter(item => matchesReviewFilters(item, annotate.width, annotate.height));
  });
  const reviewFilterMatchedIds = computed(() => new Set(reviewFilterMatchedAnnotations.value.map((item) => item.id)));
  const saveStateText = computed(() => {
    if (annotate.dirty) return "有未保存修改";
    if (annotate.savedAt) return `已保存 ${new Date(annotate.savedAt).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })}`;
    return "等待标注";
  });

  function matchesReviewFilters(item, width, height) {
    return matchesAnnotationFilters(item, annotate.reviewFilters, width, height);
  }

  function resetReviewFilters() {
    for (const key of Object.keys(annotate.reviewFilters)) annotate.reviewFilters[key] = "";
    annotate.reviewFilterGlobalMatches = null;
    annotate.status = "筛选条件已清空";
  }

  function toggleReviewFilterPanel() {
    annotate.reviewFilterOpen = !annotate.reviewFilterOpen;
  }

  async function collectReviewFilterMatches() {
    if (!reviewFilterActive.value) {
      annotate.reviewFilterGlobalMatches = null;
      return { files: 0, annotations: 0 };
    }
    let matchedFiles = 0;
    let matchedAnnotations = 0;
    const candidates = files.rows.filter((item) => item.annotated || item.id === annotate.current?.id);
    for (const file of candidates) {
      let annotation;
      if (file.id === annotate.current?.id) {
        annotation = { annotations: annotate.annotations, width: annotate.width, height: annotate.height };
      } else {
        try {
          const data = await apiFetch(`/api/annotations/file/${file.id}`);
          annotation = data.annotation;
        } catch {
          continue;
        }
      }
      const count = (annotation.annotations || []).map(normalizeAnnotationItem)
        .filter(item => matchesReviewFilters(item, annotation.width, annotation.height)).length;
      if (count) {
        matchedFiles += 1;
        matchedAnnotations += count;
      }
    }
    annotate.reviewFilterGlobalMatches = { files: matchedFiles, annotations: matchedAnnotations };
    annotate.status = `全项目筛选命中 ${matchedAnnotations} 个标注，分布在 ${matchedFiles} 张图片`;
    return annotate.reviewFilterGlobalMatches;
  }

  async function deleteReviewFilterMatches() {
    if (!reviewFilterActive.value) return;
    loading.value = true;
    let changedFiles = 0;
    let removed = 0;
    const candidates = files.rows.filter((item) => item.annotated || item.id === annotate.current?.id);
    try {
      for (const [index, file] of candidates.entries()) {
        annotate.status = `正在清理全项目命中项：${index + 1} / ${candidates.length} · ${file.name}`;
        let annotation;
        if (file.id === annotate.current?.id) {
          annotation = {
            fileId: file.id,
            width: annotate.width,
            height: annotate.height,
            annotations: annotate.annotations.map((item) => ({ ...item }))
          };
        } else {
          try {
            const data = await apiFetch(`/api/annotations/file/${file.id}`);
            annotation = data.annotation;
          } catch {
            continue;
          }
        }
        const items = (annotation.annotations || []).map(normalizeAnnotationItem);
        const nextItems = items.filter((item) => !matchesReviewFilters(item, annotation.width, annotation.height));
        const delta = items.length - nextItems.length;
        if (!delta) continue;
        removed += delta;
        changedFiles += 1;
        const saved = await apiFetch(`/api/annotations/file/${file.id}`, {
          method: "PUT",
          body: {
            fileId: file.id,
            width: annotation.width,
            height: annotation.height,
            annotations: nextItems
          }
        });
        if (file.id === annotate.current?.id) applyAnnotation(saved.annotation, { status: "当前图片命中项已删除" });
      }
      await refreshFiles();
      annotate.reviewFilterGlobalMatches = { files: changedFiles, annotations: removed };
      annotate.status = removed ? `已从全项目 ${changedFiles} 张图片删除 ${removed} 个命中标注` : "全项目没有命中的标注";
    } catch (error) {
      annotate.status = error.message;
    } finally {
      loading.value = false;
    }
  }

  function markAnnotationDirty(status = "有未保存修改") {
    annotationEditRevision++;
    annotate.dirty = true;
    annotate.status = status;
  }

  function markAnnotationSaved(status = "标注已保存") {
    annotate.dirty = false;
    annotate.savedAt = Date.now();
    annotate.status = status;
  }

  function canLeaveCurrentAnnotation() {
    return !annotate.dirty || window.confirm("当前图片有未保存修改，离开后将丢失这些改动。是否继续？");
  }

  async function selectAnnotateFile(file) {
    if (file?.id !== annotate.current?.id && !canLeaveCurrentAnnotation()) return;
    const selection = ++annotationSelection;
    annotate.ready = false;
    annotate.current = file;
    annotate.fileId = file?.id || null;
    clearAnnotation();
    resetAnnotatePointDraft();
    annotate.dirty = false;
    annotate.savedAt = null;
    annotate.zoom = 1;
    if (!file) {
      annotate.status = "请选择或上传图片";
      return;
    }
    annotate.status = file.annotated
      ? "正在加载已保存标注"
      : annotate.pointMode
        ? "图片已选择，点提示模式可点击抠图"
        : "图片已选择，可单张分割标注或批量标注全部图片";
    try {
      await loadAnnotatePreview(file, selection);
      if (selection !== annotationSelection || file.id !== annotate.current?.id) return;
      if (file.annotated) {
        const data = await apiFetch(`/api/annotations/file/${file.id}`);
        if (selection === annotationSelection) applyAnnotation(data.annotation);
      } else {
        markAnnotationSaved(annotate.pointMode ? "点提示模式：点击目标开始抠图" : "图片已选择，等待自动标注");
      }
      if (selection === annotationSelection) annotate.ready = true;
    } catch (error) {
      if (selection === annotationSelection) annotate.status = `加载失败：${error.message}，请重新选择图片重试。`;
    }
  }

  async function runMaskForFile(file, { updateCurrent = true } = {}) {
    const selection = annotationSelection;
    const revision = annotationEditRevision;
    const data = await apiFetch("/api/annotations/auto", {
      method: "POST",
      body: {
        fileId: file.id,
        conf: Number(annotate.conf),
        defaultLabel: annotate.defaultRunLabel || null
      }
    });
    if (updateCurrent && selection === annotationSelection && revision === annotationEditRevision) applyAnnotation(data.annotation);
    if (data.user) {
      account.value = data.user;
      saveSession({ ...session(), user: data.user });
    }
    return data.annotation;
  }

  async function runCurrentMask() {
    if (!annotate.current?.id || !annotate.ready) {
      annotate.status = "请先选择一张已上传图片";
      return;
    }
    if (!annotate.labels.length) {
      annotate.status = "请先添加至少一个项目标签";
      return;
    }
    loading.value = true;
    const file = annotate.current;
    const selection = annotationSelection;
    const revision = annotationEditRevision;
    annotate.status = `正在分割标注：${annotate.current.name}`;
    try {
      await runMaskForFile(file, { updateCurrent: true });
      await refreshFiles();
      if (selection !== annotationSelection || revision !== annotationEditRevision) return;
      const refreshed = files.rows.find((item) => item.id === file.id) || file;
      await selectAnnotateFile(refreshed);
      annotate.status = `当前图片已生成 ${annotate.annotations.length} 条标注`;
    } catch (error) {
      annotate.status = error.message;
    } finally {
      loading.value = false;
    }
  }

  async function runMasks() {
    if (!projects.selectedId) {
      annotate.status = "请先选择项目";
      return;
    }
    if (!files.rows.length) {
      annotate.status = "请先上传图片";
      return;
    }
    if (!annotate.labels.length) {
      annotate.status = "请先添加至少一个项目标签";
      return;
    }
    loading.value = true;
    const currentId = annotate.current?.id || files.rows[0]?.id;
    const selection = annotationSelection;
    const projectId = projects.selectedId;
    const candidates = [...files.rows];
    let success = 0;
    let failed = 0;
    try {
      for (const [index, file] of candidates.entries()) {
        if (projectId !== projects.selectedId) break;
        annotate.status = `AI 自动标注中：${index + 1} / ${files.rows.length} · ${file.name}`;
        try {
          await runMaskForFile(file, { updateCurrent: file.id === currentId });
          success += 1;
        } catch {
          failed += 1;
        }
      }
      await refreshFiles();
      if (selection !== annotationSelection || projectId !== projects.selectedId) return;
      const nextCurrent = files.rows.find((file) => file.id === currentId) || files.rows[0];
      if (nextCurrent) await selectAnnotateFile(nextCurrent);
      annotate.status = failed
        ? `已完成 ${success} 张，失败 ${failed} 张`
        : `已完成全部 ${success} 张图片的自动标注`;
    } catch (error) {
      annotate.status = error.message;
    } finally {
      loading.value = false;
    }
  }

  async function saveAnnotation() {
    if (!annotate.current?.id || !annotate.ready) return;
    const fileId = annotate.current.id;
    const selection = annotationSelection;
    const revision = annotationEditRevision;
    loading.value = true;
    const annotations = annotate.annotations.map(normalizeAnnotationItem);
    normalizeAnnotationLabels(annotations);
    try {
      const data = await apiFetch(`/api/annotations/file/${fileId}`, {
        method: "PUT",
        body: {
          fileId,
          width: annotate.width,
          height: annotate.height,
          annotations
        }
      });
      if (selection === annotationSelection && revision === annotationEditRevision)
        applyAnnotation(data.annotation, { status: "标注已保存" });
      await refreshFiles();
    } catch (error) {
      annotate.status = error.message;
    } finally {
      loading.value = false;
    }
  }

  function toggleAnnotationConfirmed(annotationId) {
    const item = annotate.annotations.find((a) => a.id === annotationId);
    if (item) {
      item.confirmed = !isAnnotationConfirmed(item);
      markAnnotationDirty(item.confirmed ? "目标已人工确认，记得保存" : "目标已取消确认，记得保存");
    }
  }

  function removeAnnotation(annotationId) {
    annotate.annotations = annotate.annotations.filter((item) => item.id !== annotationId);
    annotate.activeId = annotate.annotations[0]?.id || "";
    markAnnotationDirty("目标已删除，记得保存");
  }

  function yoloTxt() {
    normalizeAnnotationLabels();
    return formatYoloTxt(annotate.annotations, selectedProject.value?.dataType, isExportableAnnotation);
  }

  function downloadCurrentTxt() {
    let txt;
    try { txt = yoloTxt(); }
    catch (error) { annotate.status = error.message; return; }
    if (!txt) {
      annotate.status = "没有可导出的已分配标签标注";
      return;
    }
    const blob = new Blob([txt], { type: "text/plain" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `${annotate.current?.name?.replace(/\.[^.]+$/, "") || "annotation"}.txt`;
    a.click();
    URL.revokeObjectURL(url);
  }

  function annotationBoxStyle(item) {
    const highlighted = reviewFilterActive.value && reviewFilterMatchedIds.value.has(item.id);
    return {
      ...buildAnnotationBoxStyle(item, annotate.labels, annotate.activeId === item.id),
      outline: highlighted ? "3px solid rgba(250, 204, 21, 0.95)" : undefined,
      outlineOffset: highlighted ? "3px" : undefined
    };
  }

  function annotationPolygonStyle(item) {
    return buildAnnotationPolygonStyle(item, annotate.labels, annotate.activeId === item.id);
  }

  function labelChipStyle(label) {
    return buildLabelChipStyle(label, annotate.labels);
  }

  function labelSwatchStyle(label) {
    return buildLabelSwatchStyle(label, annotate.labels);
  }

  function annotationRowAccentStyle(item) {
    return buildAnnotationRowAccentStyle(item, annotate.labels);
  }

  function labelColor(label) {
    return getLabelColor(label, annotate.labels);
  }

  const yoloFrameStyle = computed(() => ({
    width: annotate.frame.width ? `${annotate.frame.width * annotate.zoom}px` : "auto",
    height: annotate.frame.height ? `${annotate.frame.height * annotate.zoom}px` : "auto"
  }));

  function updateYoloFrame(event) {
    const image = event?.target || document.querySelector(".annotate-canvas-panel .yolo-image-frame img:not(.point-mask-overlay)");
    const canvas = image?.closest?.(".yolo-canvas");
    if (!image || !canvas || !image.naturalWidth || !image.naturalHeight) return;
    annotate.width = image.naturalWidth;
    annotate.height = image.naturalHeight;
    const canvasStyle = getComputedStyle(canvas);
    const paddingX = parseFloat(canvasStyle.paddingLeft) + parseFloat(canvasStyle.paddingRight);
    const paddingY = parseFloat(canvasStyle.paddingTop) + parseFloat(canvasStyle.paddingBottom);
    const viewportWidth = Math.max(1, canvas.clientWidth - paddingX);
    const viewportHeight = Math.max(1, canvas.clientHeight - paddingY);
    const imageRatio = image.naturalWidth / image.naturalHeight;
    const viewportRatio = viewportWidth / viewportHeight;
    let width;
    let height;
    if (viewportRatio > imageRatio) {
      height = viewportHeight;
      width = height * imageRatio;
    } else {
      width = viewportWidth;
      height = width / imageRatio;
    }
    annotate.frame = { width: Math.max(1, width), height: Math.max(1, height) };
  }

  function clamp01(value) {
    return Math.max(0, Math.min(1, Number(value) || 0));
  }

  function pointerToYoloPoint(event) {
    const frame = event.currentTarget?.closest?.(".yolo-image-frame") || event.currentTarget;
    const rect = frame?.getBoundingClientRect?.();
    if (!rect?.width || !rect?.height) return null;
    return {
      x: clamp01((event.clientX - rect.left) / rect.width),
      y: clamp01((event.clientY - rect.top) / rect.height)
    };
  }

  function boxFromPoints(start, end) {
    const left = Math.min(start.x, end.x);
    const right = Math.max(start.x, end.x);
    const top = Math.min(start.y, end.y);
    const bottom = Math.max(start.y, end.y);
    return {
      cx: clamp01((left + right) / 2),
      cy: clamp01((top + bottom) / 2),
      width: clamp01(right - left),
      height: clamp01(bottom - top)
    };
  }

  function drawingBoxStyle() {
    const box = annotate.drawingBox?.bbox;
    if (!box) return {};
    return {
      left: `${(box.cx - box.width / 2) * 100}%`,
      top: `${(box.cy - box.height / 2) * 100}%`,
      width: `${box.width * 100}%`,
      height: `${box.height * 100}%`
    };
  }

  function manualAnnotationLabel() {
    if (!isUnassignedLabel(annotate.defaultRunLabel)) return annotate.defaultRunLabel;
    return activeAnnotation.value?.label || annotate.labels[0] || null;
  }

  function beginManualBox(event) {
    if (!annotate.drawMode || !annotate.current || !annotate.ready) return;
    if (event.button !== undefined && event.button !== 0) return;
    const point = pointerToYoloPoint(event);
    if (!point) return;
    event.preventDefault();
    event.currentTarget?.setPointerCapture?.(event.pointerId);
    annotate.drawingBox = { start: point, current: point, bbox: boxFromPoints(point, point) };
  }

  function updateManualBox(event) {
    if (!annotate.drawMode || !annotate.drawingBox) return;
    const point = pointerToYoloPoint(event);
    if (!point) return;
    annotate.drawingBox.current = point;
    annotate.drawingBox.bbox = boxFromPoints(annotate.drawingBox.start, point);
  }

  function finishManualBox(event) {
    if (!annotate.drawMode || !annotate.drawingBox) return;
    updateManualBox(event);
    const bbox = annotate.drawingBox.bbox;
    annotate.drawingBox = null;
    event.currentTarget?.releasePointerCapture?.(event.pointerId);
    if (!bbox || bbox.width < 0.003 || bbox.height < 0.003) {
      annotate.status = "框太小，已忽略";
      return;
    }
    const label = manualAnnotationLabel();
    const item = normalizeAnnotationItem({
      id: `manual_${Date.now()}_${Math.random().toString(16).slice(2, 8)}`,
      classId: findLabelIndex(label),
      label,
      bbox,
      segment: null,
      confidence: 1,
      confirmed: false
    });
    annotate.annotations.push(item);
    annotate.activeId = item.id;
    markAnnotationDirty(label ? `已新增 ${label} 手动画框，记得保存` : "已新增未分配手动画框，记得选择标签并保存");
  }

  function cancelManualBox() {
    annotate.drawingBox = null;
  }

  function toggleManualDrawMode() {
    annotate.drawMode = !annotate.drawMode;
    annotate.drawingBox = null;
    if (annotate.drawMode) {
      annotate.pointMode = false;
      resetAnnotatePointDraft();
      annotate.status = "画框模式已开启：在图片上拖拽补一个目标框";
    } else {
      annotate.status = "画框模式已关闭";
    }
  }

  function setYoloZoom(value) {
    const next = Math.max(0.25, Math.min(3, Number(value) || 1));
    annotate.zoom = Math.round(next * 100) / 100;
    // Keep the fitted base size; only re-fit when returning to 100% / 适配.
    if (Math.abs(annotate.zoom - 1) < 0.001) {
      annotate.zoom = 1;
      nextTick(updateYoloFrame);
    }
  }

  function resetYoloZoom() {
    annotate.zoom = 1;
    nextTick(updateYoloFrame);
  }

  async function selectAdjacentFile(direction) {
    if (!files.rows.length) return;
    const index = currentFileIndex.value;
    const nextIndex = Math.max(0, Math.min(files.rows.length - 1, (index < 0 ? 0 : index) + direction));
    const nextFile = files.rows[nextIndex];
    if (nextFile && nextFile.id !== annotate.current?.id) await selectAnnotateFile(nextFile);
  }

  function segmentPoints(item) {
    if (!item.segment?.length) return "";
    const points = [];
    for (let i = 0; i < item.segment.length - 1; i += 2) points.push(`${item.segment[i] * 100},${item.segment[i + 1] * 100}`);
    return points.join(" ");
  }

  function previewUrl(file) {
    return file?.id === annotate.current?.id ? annotate.preview : "";
  }

  function exportSplitTotal() {
    return Number(exportPage.split.train || 0) + Number(exportPage.split.val || 0) + Number(exportPage.split.test || 0);
  }

  function validExportConfig() {
    return [exportPage.split.train, exportPage.split.val, exportPage.split.test]
      .every(x => Number.isInteger(x) && x >= 0 && x <= 100)
      && exportSplitTotal() === 100 && Number.isInteger(exportPage.seed)
      && exportPage.seed >= -2147483648 && exportPage.seed <= 2147483647;
  }

  function exportRequest() {
    return { projectId: projects.selectedId, format: exportPage.format, seed: exportPage.seed, split: { ...exportPage.split } };
  }

  async function refreshExportAnalysis() {
    const requestId = ++analysisRequestId;
    exportPage.analysis = null;
    exportPage.analysisError = "";
    if (!projects.selectedId || !validExportConfig()) {
      exportPage.analysisLoading = false;
      return;
    }
    const request = exportRequest();
    exportPage.analysisLoading = true;
    try {
      const data = await apiFetch("/api/export/analyze", { method: "POST", body: request });
      if (requestId === analysisRequestId) exportPage.analysis = data.analysis;
    } catch (error) {
      if (requestId === analysisRequestId) exportPage.analysisError = error.message;
    } finally {
      if (requestId === analysisRequestId) exportPage.analysisLoading = false;
    }
  }

  function applyRecommendedSplit() {
    if (!exportPage.analysis?.recommendationAvailable) return;
    Object.assign(exportPage.split, exportPage.analysis.recommendedSplit);
  }

  watch(() => [page.value, projects.selectedId, exportPage.format, exportPage.seed,
    exportPage.split.train, exportPage.split.val, exportPage.split.test], (_, previous, onCleanup) => {
    analysisRequestId++;
    exportPage.analysis = null;
    exportPage.analysisLoading = false;
    exportPage.analysisError = "";
    if (page.value !== "export" || !projects.selectedId || !validExportConfig()) return;
    exportPage.analysisLoading = true;
    const timer = setTimeout(refreshExportAnalysis, 250);
    onCleanup(() => clearTimeout(timer));
  }, { flush: "sync" });

  function formatDateTime(value) {
    if (!value) return "-";
    return new Date(value).toLocaleString();
  }

  async function refreshExports() {
    if (!account.value) return;
    const query = projects.selectedId ? `?projectId=${encodeURIComponent(projects.selectedId)}` : "";
    const data = await apiFetch(`/api/export${query}`).catch(() => ({ exports: [] }));
    exportPage.rows = data.exports || [];
  }

  async function downloadExportItem(item) {
    if (!item?.downloadUrl) return;
    loading.value = true;
    exportPage.status = "正在下载导出文件...";
    try {
      await downloadAuthenticated(item.downloadUrl, `${item.id}.zip`);
      exportPage.status = "下载已开始";
    } catch (error) {
      exportPage.status = error.message;
    } finally {
      loading.value = false;
    }
  }

  async function createExport() {
    if (!projects.selectedId) {
      exportPage.status = "请先选择项目";
      return;
    }
    if (!validExportConfig()) {
      exportPage.status = "划分比例需为 0–100 的整数且合计 100；随机种子需为有效整数。";
      return;
    }
    if (!annotate.labels.length) {
      exportPage.status = "请先添加项目标签后再导出";
      if (page.value === "annotate") annotate.status = exportPage.status;
      return;
    }
    loading.value = true;
    exportPage.status = "正在生成数据集 ZIP，请稍候...";
    if (page.value === "annotate") annotate.status = exportPage.status;
    try {
      const data = await apiFetch("/api/export/dataset", {
        method: "POST",
        body: exportRequest()
      });
      await downloadAuthenticated(data.export.downloadUrl, `${data.export.id}.zip`);
      exportPage.status = `导出成功，文件大小 ${formatBytes(data.export.size)}`;
      if (page.value === "annotate") annotate.status = exportPage.status;
      exportPage.tab = "history";
      await refreshExports();
    } catch (error) {
      const message = error.message.includes("No annotated images")
        ? "当前项目没有已标注图片，请先完成标注并保存后再导出"
        : error.message;
      exportPage.status = message;
      if (page.value === "annotate") annotate.status = message;
    } finally {
      loading.value = false;
    }
  }

  async function subscribe(plan) {
    loading.value = true;
    message.value = "";
    try {
      const data = await apiFetch("/api/billing/subscribe", { method: "POST", body: { plan } });
      account.value = data.user;
      saveSession({ ...session(), user: data.user });
      message.value = "套餐已更新";
    } catch (error) {
      message.value = error.message;
    } finally {
      loading.value = false;
    }
  }

  async function saveSettings() {
    const data = await apiFetch("/api/account/profile", { method: "PUT", body: { username: settings.username, phone: settings.phone } });
    account.value = data.user;
    saveSession({ ...session(), user: data.user });
    message.value = "账户信息已保存";
  }

  async function changePassword() {
    if (!settings.newPassword || settings.newPassword !== settings.confirmPassword) {
      message.value = "两次输入的新密码不一致";
      return;
    }
    await apiFetch("/api/account/password", {
      method: "POST",
      body: { currentPassword: settings.currentPassword, newPassword: settings.newPassword }
    });
    settings.currentPassword = "";
    settings.newPassword = "";
    settings.confirmPassword = "";
    message.value = "密码已更新";
  }

  async function loadAccountSettings() {
    if (!account.value) return;
    settings.username = account.value.username || "";
    settings.phone = account.value.phone || "";
    const [notifications, tokens, team, devices] = await Promise.all([
      apiFetch("/api/account/notifications").catch(() => ({ settings: settings.notifications })),
      apiFetch("/api/account/api-tokens").catch(() => ({ tokens: [] })),
      apiFetch("/api/account/team").catch(() => ({ members: [] })),
      apiFetch("/api/account/devices").catch(() => ({ devices: [] }))
    ]);
    settings.notifications = { ...settings.notifications, ...(notifications.settings || {}) };
    settings.tokens = tokens.tokens || [];
    settings.members = team.members || [];
    settings.devices = devices.devices || [];
  }

  async function saveNotifications() {
    const data = await apiFetch("/api/account/notifications", { method: "PUT", body: settings.notifications });
    settings.notifications = data.settings;
    message.value = "通知设置已保存";
  }

  async function createApiToken() {
    if (!settings.tokenName.trim()) {
      message.value = "请输入 Token 名称";
      return;
    }
    const data = await apiFetch("/api/account/api-tokens", { method: "POST", body: { name: settings.tokenName } });
    settings.tokenValue = data.value;
    settings.tokenName = "";
    await loadAccountSettings();
    message.value = "Token 已创建，请及时保存";
  }

  async function revokeApiToken(tokenId) {
    await apiFetch(`/api/account/api-tokens/${tokenId}`, { method: "DELETE" });
    await loadAccountSettings();
    message.value = "Token 已撤销";
  }

  async function addTeamMember() {
    if (!settings.teamEmail.trim()) {
      message.value = "请输入成员邮箱";
      return;
    }
    await apiFetch("/api/account/team", { method: "POST", body: { email: settings.teamEmail, role: settings.teamRole } });
    settings.teamEmail = "";
    settings.teamRole = "member";
    await loadAccountSettings();
    message.value = "成员邀请已创建";
  }

  async function removeTeamMember(memberId) {
    await apiFetch(`/api/account/team/${memberId}`, { method: "DELETE" });
    await loadAccountSettings();
    message.value = "成员已移除";
  }

  async function revokeDevice(deviceId) {
    await apiFetch(`/api/account/devices/${deviceId}/revoke`, { method: "POST" });
    await loadAccountSettings();
    message.value = "设备已撤销";
  }

  function refreshPage() {
    if (page.value === "dashboard") refreshDashboard();
    if (page.value === "billing") refreshDashboard();
    if (page.value === "files" || page.value === "annotate" || page.value === "export") {
      refreshProjects().then(refreshFiles);
    }
    if (page.value === "records") refreshRecords();
    if (page.value === "export") {
      syncExportSplitFromProject();
      refreshExports();
    }
    if (page.value === "settings" && account.value) loadAccountSettings();
  }


  onMounted(() => {
    window.addEventListener("popstate", () => {
      path.value = window.location.pathname;
      refreshPage();
    });
    window.addEventListener("resize", updateYoloFrame);
    if (needsLogin(page.value) && !session()) go("/auth.html");
    account.value = currentUser();
    refreshPage();
  });

  const providers = {
    page, auth, go, logout, homeFeatures, heroPreviewRoad, message, loading, uploadQueue, uploadQueueStatusLabel,
    submitAuth, openForgotPassword, submitForgotPassword, submitResetPassword, annotate, projects, selectedProject, selectedProjectDataType, selectedProjectDataTypeLabel, selectedProjectExportHint, files, filteredFiles, account, formatBytes, dashboard, records,
    segment, settings, settingsTabs, billingPlans, billingExplain, billingFaqs, exportPage,
    saveSession, session, authHeaders, selectAnnotateFile, runCurrentMask, runMasks, saveAnnotation,
    removeAnnotation, toggleAnnotationConfirmed, activeAnnotation, annotationStats, currentFileIndex,
    saveStateText, yoloTxt, annotationBoxStyle, annotationPolygonStyle, labelChipStyle, labelSwatchStyle,
    annotationRowAccentStyle, labelColor, segmentPoints, yoloFrameStyle, updateYoloFrame,
    drawingBoxStyle, beginManualBox, updateManualBox, finishManualBox, cancelManualBox, toggleManualDrawMode,
    togglePointPromptMode, setAnnotatePointPolarity, setSegmentPointPolarity, setSegmentPromptMode,
    handleAnnotatePointClick, handleSegmentPointClick, onSegmentImageLoad,
    selectAnnotatePointCandidate, selectSegmentPointCandidate,
    clearAnnotatePointDraft, clearSegmentPointDraft,
    confirmAnnotatePointTarget, confirmSegmentPointTarget,
    startNewAnnotatePointTarget, startNewSegmentPointTarget,
    previewConfirmedSegmentTarget, segmentCanvasSrc,
    promptPointStyle, activeAnnotatePointCandidate, activeSegmentPointCandidate, candidatePolygonPoints,
    reviewFilterActive, reviewFilterMatchedAnnotations, collectReviewFilterMatches,
    resetReviewFilters, toggleReviewFilterPanel, deleteReviewFilterMatches,
    setYoloZoom, resetYoloZoom, selectAdjacentFile, addAnnotateLabel, beginDeleteAnnotateLabel,
    confirmDeleteAnnotateLabel, cancelDeleteAnnotateLabel, canRunAnnotateAi, formatAnnotationLabel,
    applyLabelToActive, applyAnnotationLabel, syncBatchLabelsFromAnnotations, syncAnnotationLabels, changeAnnotateFiles,
    previewUrl, downloadCurrentTxt, selectProject, createProject, copyCurrentProject, deleteFile, deleteCurrentProject,
    createExport, exportSplitTotal, validExportConfig, refreshExportAnalysis, applyRecommendedSplit, downloadExportItem, formatDateTime, needsLogin, uploadFiles,
    runSegment, selectSegmentFile, showSegmentOverlay, subscribe, saveSettings, changePassword,
    saveNotifications, createApiToken, revokeApiToken, addTeamMember, removeTeamMember, revokeDevice,
    refreshExports, uploadDuplicatePrompt, resolveDuplicateUpload
  };
  for (const [key, value] of Object.entries(providers)) {
    provide(key, value);
  }

  return {
    uploadDuplicatePrompt,
    resolveDuplicateUpload,
    page,
    path,
    auth,
    go,
    logout,
    account,
    message,
    loading,
    dashboard,
    files,
    records,
    projects,
    exportPage,
    selectedProject,
    selectedProjectDataType,
    selectedProjectDataTypeLabel,
    selectedProjectExportHint,
    segment,
    annotate,
    settings,
    settingsTabs,
    billingPlans,
    billingExplain,
    billingFaqs,
    homeFeatures,
    heroPreviewRoad,
    submitAuth,
    openForgotPassword,
    submitForgotPassword,
    submitResetPassword,
    uploadQueue,
    uploadQueueStatusLabel,
    filteredFiles,
    currentFileIndex,
    activeAnnotation,
    annotationStats,
    saveStateText,
    yoloFrameStyle,
    needsLogin,
    refreshDashboard,
    createProject,
    copyCurrentProject,
    selectProject,
    deleteCurrentProject,
    uploadFiles,
    deleteFile,
    runSegment,
    selectSegmentFile,
    showSegmentOverlay,
    selectAnnotateFile,
    runCurrentMask,
    runMasks,
    saveAnnotation,
    removeAnnotation,
    toggleAnnotationConfirmed,
    yoloTxt,
    downloadCurrentTxt,
    annotationBoxStyle,
    annotationPolygonStyle,
    labelChipStyle,
    labelSwatchStyle,
    annotationRowAccentStyle,
    labelColor,
    segmentPoints,
    updateYoloFrame,
    setYoloZoom,
    resetYoloZoom,
    selectAdjacentFile,
    addAnnotateLabel,
    beginDeleteAnnotateLabel,
    confirmDeleteAnnotateLabel,
    cancelDeleteAnnotateLabel,
    canRunAnnotateAi,
    formatAnnotationLabel,
    applyLabelToActive,
    applyAnnotationLabel,
    syncBatchLabelsFromAnnotations,
    syncAnnotationLabels,
    changeAnnotateFiles,
    previewUrl,
    createExport,
    exportSplitTotal,
    downloadExportItem,
    formatDateTime,
    subscribe,
    saveSettings,
    changePassword,
    saveNotifications,
    createApiToken,
    revokeApiToken,
    addTeamMember,
    removeTeamMember,
    revokeDevice,
    refreshExports
  };
}
