<template>
  <div ref="container" class="auto-height-table">
    <GatewayTable
      resize-key="components.AutoHeightTable.vue.0"
      v-bind="$attrs"
      :columns="columns"
      :scroll="{ x: scrollX, y: bodyHeight }"
      :pagination="false"
      size="small"
    >
      <template v-for="name in Object.keys($slots)" #[name]="slotProps">
        <slot :name="name" v-bind="slotProps || {}" />
      </template>
    </GatewayTable>
  </div>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, useAttrs } from 'vue'
import { localizeColumns } from '../i18n'
defineOptions({ inheritAttrs: false })
withDefaults(defineProps<{ scrollX?: number }>(), { scrollX: 1000 })
const container = ref<HTMLElement>()
const attrs = useAttrs()
const columns = computed(() => localizeColumns((attrs.columns ?? []) as any[]))
const bodyHeight = ref(300)
let observer: ResizeObserver | undefined
onMounted(() => {
  const element = container.value
  if (!element) return
  const update = () => {
    const header = element.querySelector('.ant-table-header') ?? element.querySelector('.ant-table-thead')
    bodyHeight.value = Math.max(
      80,
      Math.floor(element.clientHeight - (header?.getBoundingClientRect().height ?? 38) - 2)
    )
  }
  observer = new ResizeObserver(update)
  observer.observe(element)
  const header = element.querySelector('.ant-table-header')
  if (header) observer.observe(header)
  update()
})
onBeforeUnmount(() => observer?.disconnect())
</script>
