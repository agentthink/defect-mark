<script setup>
import { inject } from "vue";

const records = inject("records");
</script>

<template>
  <main class="mf-main page-pad work-page">
    <section class="work-wrap">
      <header class="work-title">
        <div class="work-title-icon">R</div>
        <div><h1>处理记录</h1><p>追踪自动分割、标注、导出等任务的执行状态。</p></div>
      </header>
      <nav class="billing-proto-tabs work-tabs"><a class="active">全部</a><a>运行中</a><a>已完成</a><a>失败</a><a>已取消</a></nav>
      <section class="work-card">
        <h2>任务列表</h2>
        <table>
          <tbody>
            <tr v-for="task in records.rows" :key="task.id">
              <td>{{ task.title }}</td>
              <td>{{ task.type }}</td>
              <td>{{ task.status }}</td>
              <td>{{ task.imageCount }}</td>
              <td>{{ task.createdAt }}</td>
            </tr>
            <tr v-if="!records.rows.length"><td colspan="5">暂无处理任务。</td></tr>
          </tbody>
        </table>
      </section>
    </section>
  </main>
</template>
