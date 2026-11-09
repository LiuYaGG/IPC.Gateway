<template>
  <a-dropdown :trigger="['click']">
    <a-button type="text" class="language-switcher" :aria-label="tx('切换语言')">
      <template #icon><GlobalOutlined /></template>
      {{ locale === 'zh-CN' ? '中文' : 'English' }}
    </a-button>
    <template #overlay>
      <a-menu class="language-switcher-menu" :selected-keys="[locale]">
        <a-menu-item key="zh-CN" @click="setLocale('zh-CN')">简体中文</a-menu-item>
        <a-menu-item key="en-US" @click="setLocale('en-US')">English</a-menu-item>
      </a-menu>
    </template>
  </a-dropdown>
</template>
<script setup lang="ts">
import { GlobalOutlined } from '@ant-design/icons-vue'
import { locale, setLocale, tx } from '../i18n'
</script>
