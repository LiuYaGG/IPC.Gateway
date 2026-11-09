<template><a-segmented v-bind="$attrs" :value="selected" :options="choices" @change="change" /></template>
<script setup lang="ts">
import { computed } from 'vue'
import { tx } from '../../i18n'
defineOptions({ inheritAttrs: false })
const props = defineProps<{ modelValue?: any; options: any[] }>()
const emit = defineEmits<{ 'update:modelValue': [value: any]; change: [value: any] }>()
const choices = computed(() =>
  props.options.map((option, index) => ({
    label: tx(typeof option === 'object' ? option.label : option),
    value: index,
    disabled: typeof option === 'object' && option.disabled
  }))
)
const selected = computed(() =>
  props.options.findIndex((option) => (typeof option === 'object' ? option.value : option) === props.modelValue)
)
function change(index: any) {
  const option = props.options[Number(index)]
  const value = typeof option === 'object' ? option.value : option
  emit('update:modelValue', value)
  emit('change', value)
}
</script>
