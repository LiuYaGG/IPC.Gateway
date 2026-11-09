<template>
  <a-select
    v-bind="$attrs"
    :value="modelValue === '' ? undefined : modelValue"
    :show-search="searchable"
    :allow-clear="allowClear"
    :mode="mode"
    option-filter-prop="label"
    option-label-prop="label"
    @update:value="update"
    @change="change"
  >
    <slot />
  </a-select>
</template>
<script setup lang="ts">
defineOptions({ inheritAttrs: false })
const props = defineProps<{
  modelValue?: any
  searchable?: boolean
  allowClear?: boolean
  mode?: 'multiple' | 'tags'
}>()
const emit = defineEmits<{ 'update:modelValue': [value: any]; change: [value: any] }>()
const normalize = (value: any) => value ?? (props.mode ? [] : '')
function update(value: any) {
  emit('update:modelValue', normalize(value))
}
function change(value: any) {
  emit('change', normalize(value))
}
</script>
