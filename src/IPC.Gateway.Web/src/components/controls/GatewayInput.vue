<template>
  <component
    :is="inputComponent"
    v-bind="$attrs"
    :type="type === 'textarea' || type === 'password' ? undefined : type"
    :value="modelValue"
    :allow-clear="allowClear"
    :rows="rows"
    :auto-size="autoSize"
    @update:value="update"
    @change="change"
    @input="input"
  >
    <template v-for="name in Object.keys($slots)" #[name]="slotProps">
      <slot :name="name" v-bind="slotProps || {}" />
    </template>
    <template v-if="prefixIcon" #prefix><component :is="prefixIcon" /></template>
  </component>
</template>
<script setup lang="ts">
import { computed, type Component } from 'vue'
import { Input, type InputProps } from 'ant-design-vue'
defineOptions({ inheritAttrs: false })
const props = defineProps<{
  modelValue?: any
  type?: InputProps['type'] | 'textarea'
  showPassword?: boolean
  prefixIcon?: Component
  allowClear?: boolean
  rows?: number
  autoSize?: boolean | object
  modelModifiers?: { number?: boolean; trim?: boolean }
}>()
const emit = defineEmits<{ 'update:modelValue': [value: any]; change: [value: any]; input: [value: any] }>()
const inputComponent = computed(() =>
  props.type === 'textarea' ? Input.TextArea : props.type === 'password' ? Input.Password : Input
)
const valueOf = (event: Event) => (event.target as HTMLInputElement).value
function update(value: string) {
  const result = props.modelModifiers?.number ? (value === '' ? '' : Number(value)) : value
  emit('update:modelValue', result)
}
function change(event: Event) {
  emit('change', valueOf(event))
}
function input(event: Event) {
  emit('input', valueOf(event))
}
</script>
