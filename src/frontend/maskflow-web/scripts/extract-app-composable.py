from pathlib import Path

app_vue = Path(__file__).resolve().parents[1] / "src" / "App.vue"
out = Path(__file__).resolve().parents[1] / "src" / "composables" / "useMaskFlowApp.js"
text = app_vue.read_text(encoding="utf-8")
start = text.index("<script setup>") + len("<script setup>\n")
end = text.index("</script>", start)
body = text[start:end]
lines = body.splitlines()
filtered = []
skip_imports = True
for line in lines:
    if skip_imports:
        if line.startswith("import "):
            continue
        if line.strip() == "":
            continue
        skip_imports = False
    if line.strip().startswith("provide("):
        continue
    if line.strip().startswith("onMounted("):
        break
    filtered.append(line)

indented = "\n".join(("  " + line if line.strip() else line) for line in filtered)
header = """import { computed, nextTick, onMounted, provide, reactive, ref } from "vue";
import { apiFetch, authHeaders, clearSession, downloadAuthenticated, formatBytes, saveSession, session, user as currentUser } from "../lib/api";
import heroPreviewRoad from "../assets/hero-preview-road.png";

export function useMaskFlowApp() {
"""
onmounted = """
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
"""
provide_block = """
  const providers = {
    page, auth, go, homeFeatures, heroPreviewRoad, message, loading, uploadQueue, uploadQueueStatusLabel,
    submitAuth, annotate, projects, selectedProject, files, filteredFiles, account, formatBytes,
    saveSession, session, authHeaders, selectAnnotateFile, runCurrentMask, runMasks, saveAnnotation,
    removeAnnotation, toggleAnnotationConfirmed, activeAnnotation, annotationStats, currentFileIndex,
    saveStateText, yoloTxt, annotationBoxStyle, segmentPoints, yoloFrameStyle, updateYoloFrame,
    setYoloZoom, resetYoloZoom, selectAdjacentFile, addAnnotateLabel, deleteAnnotateLabel,
    applyLabelToActive, syncBatchLabelsFromAnnotations, syncAnnotationLabels, changeAnnotateFiles,
    previewUrl, downloadCurrentTxt, selectProject, createProject, deleteFile, createExport,
    exportPage, exportSplitTotal, downloadExportItem, formatDateTime, needsLogin
  };
  for (const [key, value] of Object.entries(providers)) {
    provide(key, value);
  }
"""
footer = """
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
    segmentPoints,
    updateYoloFrame,
    setYoloZoom,
    resetYoloZoom,
    selectAdjacentFile,
    addAnnotateLabel,
    deleteAnnotateLabel,
    applyLabelToActive,
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
"""
out.write_text(header + indented + onmounted + provide_block + footer, encoding="utf-8")
print(f"Wrote {out} ({out.stat().st_size} bytes)")
