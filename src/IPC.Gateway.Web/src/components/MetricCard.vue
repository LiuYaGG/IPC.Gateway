<template>
  <section class="metric-card" :class="toneClass">
    <div class="metric-card__head">
      <span>{{ tx(label) }}</span>
      <span v-if="icon" class="ui-icon"><component :is="icon" /></span>
    </div>
    <strong>{{ value }}</strong>
    <small v-if="hint">{{ tx(hint) }}</small>
  </section>
</template>

<script setup lang="ts">
import { tx } from '../i18n'
import { computed, type Component } from 'vue'

const props = defineProps<{
  label: string
  value: string | number
  hint?: string
  tone?: 'normal' | 'good' | 'warn' | 'bad'
  icon?: Component
}>()

const toneClass = computed(() => `metric-card--${props.tone ?? 'normal'}`)
</script>
