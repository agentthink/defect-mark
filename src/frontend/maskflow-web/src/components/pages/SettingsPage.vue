<script setup>
import { inject } from "vue";

const account = inject("account");
const message = inject("message");
const settings = inject("settings");
const settingsTabs = inject("settingsTabs");
const saveSettings = inject("saveSettings");
const changePassword = inject("changePassword");
const saveNotifications = inject("saveNotifications");
const createApiToken = inject("createApiToken");
const revokeApiToken = inject("revokeApiToken");
const addTeamMember = inject("addTeamMember");
const removeTeamMember = inject("removeTeamMember");
const revokeDevice = inject("revokeDevice");
</script>

<template>
  <main class="mf-main page-pad account-settings-page">
    <div class="settings-heading"><div><p class="page-kicker">Account</p><h1>账户设置</h1></div><p v-if="message" class="settings-toast">{{ message }}</p></div>
    <section class="account-settings-layout">
      <nav class="account-settings-nav"><button v-for="tab in settingsTabs" :key="tab[0]" :class="{ active: settings.active === tab[0] }" type="button" @click="settings.active = tab[0]; message = ''">{{ tab[1] }}</button></nav>
      <section v-if="settings.active === 'profile'" class="account-settings-card">
        <div class="settings-card-title"><h2>个人信息</h2><p>维护账户展示信息和联系方式。</p></div>
        <form class="settings-form-grid" @submit.prevent="saveSettings">
          <label><span>用户名</span><input v-model="settings.username" placeholder="请输入用户名" /></label>
          <label><span>邮箱</span><input :value="account?.email" disabled /></label>
          <label><span>手机号</span><input v-model="settings.phone" placeholder="请输入手机号" /></label>
          <div class="settings-actions"><button class="btn" type="submit">保存修改</button></div>
        </form>
      </section>
      <section v-if="settings.active === 'password'" class="account-settings-card">
        <div class="settings-card-title"><h2>修改密码</h2><p>建议使用至少 8 位，包含字母和数字的密码。</p></div>
        <form class="settings-form-grid" @submit.prevent="changePassword">
          <label><span>当前密码</span><input v-model="settings.currentPassword" type="password" autocomplete="current-password" /></label>
          <label><span>新密码</span><input v-model="settings.newPassword" type="password" autocomplete="new-password" /></label>
          <label><span>确认新密码</span><input v-model="settings.confirmPassword" type="password" autocomplete="new-password" /></label>
          <div class="settings-actions"><button class="btn" type="submit">更新密码</button></div>
        </form>
      </section>
      <section v-if="settings.active === 'notifications'" class="account-settings-card">
        <div class="settings-card-title"><h2>通知设置</h2><p>选择希望接收的任务、账单和报告通知。</p></div>
        <div class="settings-switch-list">
          <label><input v-model="settings.notifications.emailTask" type="checkbox" /> <span>任务完成邮件通知</span></label>
          <label><input v-model="settings.notifications.emailBilling" type="checkbox" /> <span>账单和套餐邮件通知</span></label>
          <label><input v-model="settings.notifications.browserNotice" type="checkbox" /> <span>浏览器内通知提醒</span></label>
          <label><input v-model="settings.notifications.weeklyReport" type="checkbox" /> <span>每周处理报告</span></label>
        </div>
        <div class="settings-actions"><button class="btn" type="button" @click="saveNotifications">保存通知设置</button></div>
      </section>
      <section v-if="settings.active === 'tokens'" class="account-settings-card">
        <div class="settings-card-title"><h2>API Token</h2><p>用于通过接口上传图片、创建任务和导出数据集。</p></div>
        <div class="settings-inline-form">
          <input v-model="settings.tokenName" placeholder="Token 名称，例如 本地脚本" />
          <button class="btn" type="button" @click="createApiToken">创建 Token</button>
        </div>
        <div v-if="settings.tokenValue" class="token-secret"><span>新 Token</span><code>{{ settings.tokenValue }}</code></div>
        <table class="settings-table">
          <thead><tr><th>名称</th><th>前缀</th><th>创建时间</th><th>最后使用</th><th></th></tr></thead>
          <tbody>
            <tr v-for="token in settings.tokens" :key="token.id">
              <td>{{ token.name }}</td>
              <td>{{ token.tokenPrefix || token.prefix }}</td>
              <td>{{ token.createdAt }}</td>
              <td>{{ token.lastUsedAt || '-' }}</td>
              <td><button class="text-danger" @click="revokeApiToken(token.id)">撤销</button></td>
            </tr>
            <tr v-if="!settings.tokens.length"><td colspan="5">还没有 API Token。</td></tr>
          </tbody>
        </table>
      </section>
      <section v-if="settings.active === 'team'" class="account-settings-card">
        <div class="settings-card-title"><h2>团队管理</h2><p>邀请成员加入当前工作空间。</p></div>
        <div class="settings-inline-form">
          <input v-model="settings.teamEmail" placeholder="成员邮箱" />
          <select v-model="settings.teamRole"><option value="member">成员</option><option value="admin">管理员</option></select>
          <button class="btn" type="button" @click="addTeamMember">邀请成员</button>
        </div>
        <table class="settings-table">
          <thead><tr><th>邮箱</th><th>角色</th><th>状态</th><th>加入时间</th><th></th></tr></thead>
          <tbody>
            <tr v-for="member in settings.members" :key="member.id">
              <td>{{ member.email }}</td>
              <td>{{ member.role }}</td>
              <td>{{ member.status }}</td>
              <td>{{ member.createdAt }}</td>
              <td><button v-if="member.role !== 'owner'" class="text-danger" @click="removeTeamMember(member.id)">移除</button></td>
            </tr>
          </tbody>
        </table>
      </section>
      <section v-if="settings.active === 'devices'" class="account-settings-card">
        <div class="settings-card-title"><h2>设备管理</h2><p>查看并撤销已登录设备。</p></div>
        <table class="settings-table">
          <thead><tr><th>设备</th><th>IP</th><th>User Agent</th><th>最后使用</th><th>状态</th><th></th></tr></thead>
          <tbody>
            <tr v-for="device in settings.devices" :key="device.id">
              <td>{{ device.name }}</td>
              <td>{{ device.ip || '-' }}</td>
              <td>{{ device.userAgent || '-' }}</td>
              <td>{{ device.lastSeenAt }}</td>
              <td>{{ device.revokedAt ? '已撤销' : '有效' }}</td>
              <td><button v-if="!device.revokedAt" class="text-danger" @click="revokeDevice(device.id)">撤销</button></td>
            </tr>
            <tr v-if="!settings.devices.length"><td colspan="6">暂无设备记录。</td></tr>
          </tbody>
        </table>
      </section>
    </section>
  </main>
</template>
