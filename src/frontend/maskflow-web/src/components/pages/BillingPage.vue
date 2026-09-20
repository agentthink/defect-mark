<script setup>
import { inject } from "vue";

const account = inject("account");
const billingPlans = inject("billingPlans");
const billingExplain = inject("billingExplain");
const billingFaqs = inject("billingFaqs");
const subscribe = inject("subscribe");
</script>

<template>
  <main class="mf-main page-pad billing-proto-page">
    <section class="billing-proto-wrap">
      <header class="billing-proto-title">
        <div class="billing-title-icon">B</div>
        <div><h1>账单套餐</h1><p>选择适合当前标注规模的 AI 处理次数和存储空间。</p></div>
      </header>
      <nav class="billing-proto-tabs"><a class="active">套餐订阅</a><a>使用记录</a></nav>
      <section class="billing-proto-plans">
        <article v-for="plan in billingPlans" :key="plan.id" :class="['billing-proto-card', { featured: plan.id === 'pro' }]">
          <b v-if="plan.id === 'pro'" class="billing-recommend">推荐</b>
          <h2>{{ plan.name }}</h2>
          <div class="billing-proto-price"><strong>¥{{ plan.price }}</strong><span>/月</span></div>
          <ul><li v-for="feature in plan.features" :key="feature"><span>✓</span>{{ feature }}</li></ul>
          <button :class="{ primary: plan.id === 'pro' }" :disabled="account?.plan === plan.id" @click="subscribe(plan.id)">{{ account?.plan === plan.id ? '当前套餐' : '立即升级' }}</button>
        </article>
      </section>
      <section class="billing-proto-bottom">
        <article class="billing-explain-card"><h2>套餐说明</h2><div class="billing-explain-grid"><div v-for="item in billingExplain" :key="item[1]"><span>{{ item[0] }}</span><section><h3>{{ item[1] }}</h3><p>{{ item[2] }}</p></section></div></div></article>
        <article class="billing-faq-card"><h2>常见问题</h2><p v-for="faq in billingFaqs" :key="faq"><span>{{ faq }}</span><b>›</b></p></article>
      </section>
      <footer class="billing-proto-note">所有套餐按月计费，可随时取消。升级后剩余资源将自动叠加。</footer>
    </section>
  </main>
</template>
