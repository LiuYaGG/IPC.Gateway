<template>
  <a-input-number
    v-bind="$attrs"
    :value="modelValue"
    :controls="controls"
    :precision="integer ? 0 : undefined"
    @update:value="emit('update:modelValue', $event)"
    @change="emit('change', $event)"
  />
</template>
<script setup lang="ts">
defineOptions({ inheritAttrs: false })
withDefaults(defineProps<{ modelValue?: any; controls?: boolean; integer?: boolean }>(), { controls: true })
const emit = defineEmits<{ 'update:modelValue': [value: any]; change: [value: any] }>()
</script>
