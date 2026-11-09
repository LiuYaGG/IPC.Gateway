<template>
  <div
    ref="shell"
    :class="['gateway-table-shell', { 'gateway-table-shell--fill': fillHeight }]"
    :style="{ '--table-body-height': bodyHeight + 'px', '--table-viewport-width': tableWidth + 'px' }"
  >
    <div class="gateway-table-frame">
      <AntTable
        v-bind="$attrs"
        :data-source="dataSource"
        :pagination="paging ? { current: currentPage, pageSize: currentPageSize, position: [] } : false"
        :columns="resizableColumns"
        :scroll="tableScroll"
        @resize-column="resize"
        @change="tableChange"
      >
        <template v-for="name in Object.keys($slots)" #[name]="slotProps">
          <slot :name="name" v-bind="slotProps || {}" />
        </template>
      </AntTable>
    </div>
    <TablePager
      v-if="paging"
      :total="dataSource?.length ?? 0"
      :current="currentPage"
      :page-size="currentPageSize"
      @change="changePage"
    />
  </div>
</template>
<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, useAttrs, watch } from 'vue'
import { Table as AntTable } from 'ant-design-vue'
import { locale } from '../../i18n'
import TablePager from './TablePager.vue'
import { pageWindow } from '../../utils/pagination'
import { columnWidth, readColumnWidths, saveColumnWidths, type ColumnWidths } from '../../utils/columnWidths'
defineOptions({ inheritAttrs: false })
const props = withDefaults(
  defineProps<{
    columns?: any[]
    resizeKey: string
    scroll?: { x?: number | string | true; y?: number | string }
    dataSource?: any[]
    paging?: boolean
  }>(),
  { paging: true }
)
const attrs = useAttrs()
const shell = ref<HTMLElement>()
const fillHeight = ref(false)
const bodyHeight = ref(200)
const tableWidth = ref(0)
const currentPage = ref(1)
const currentPageSize = ref(20)
watch(
  () => props.dataSource?.length,
  () => {
    currentPage.value = pageWindow(props.dataSource?.length ?? 0, currentPage.value, currentPageSize.value).current
  }
)
function changePage(page: number, size: number) {
  currentPageSize.value = size
  currentPage.value = page
}
function tableChange(pagination: any, filters: any, sorter: any, extra: any) {
  if (pagination.current) currentPage.value = pagination.current
  if (typeof attrs.onChange === 'function') attrs.onChange(pagination, filters, sorter, extra)
}
let observer: ResizeObserver | undefined
onMounted(async () => {
  fillHeight.value = !!shell.value?.closest('.page-body--tables-fill, .auto-height-table')
  if (!fillHeight.value) return
  await nextTick()
  const element = shell.value!
  const update = () => {
    tableWidth.value = element.clientWidth
    const header = element.querySelector('.ant-table-thead')
    const pagination = element.querySelector('.table-pager')
    bodyHeight.value = Math.max(
      100,
      Math.floor(
        element.clientHeight -
          (header?.getBoundingClientRect().height ?? 38) -
          (pagination?.getBoundingClientRect().height ?? 0) -
          18
      )
    )
  }
  observer = new ResizeObserver(update)
  observer.observe(element)
  const header = element.querySelector('.ant-table-thead')
  if (header) observer.observe(header)
  const pager = element.querySelector('.table-pager')
  if (pager) observer.observe(pager)
  update()
})
onBeforeUnmount(() => observer?.disconnect())
const storageKey = computed(
  () =>
    'ipc.gateway.columns.' +
    props.resizeKey +
    '.' +
    (props.columns ?? []).map((column) => column.key ?? column.dataIndex ?? '').join('|')
)
const widths = ref<ColumnWidths>({})
watch(
  storageKey,
  (key) => {
    widths.value = readColumnWidths(key)
  },
  { immediate: true }
)
function isActions(column: any, index: number) {
  return (
    column.key === 'actions' ||
    /^(操作|Actions)$/i.test(String(column.title ?? '')) ||
    (!column.title && !column.dataIndex && index === (props.columns?.length ?? 0) - 1)
  )
}
const resizableColumns = computed(() =>
  (props.columns ?? []).map((column, index) => ({
    ...column,
    key: column.key ?? column.dataIndex ?? index,
    width: isActions(column, index)
      ? columnWidth(
          locale.value === 'en-US'
            ? Math.max((Number(column.width) || 150) * 1.4, (Number(column.width) || 150) + 60)
            : Math.max(Number(column.width) || 150, 90)
        )
      : columnWidth(widths.value[String(column.key ?? column.dataIndex ?? index)] ?? Number(column.width), 150),
    minWidth: 80,
    maxWidth: 1200,
    resizable: !column.children && !isActions(column, index),
    ...(isActions(column, index)
      ? { fixed: 'right' as const, className: [column.className, 'gateway-actions-cell'].filter(Boolean).join(' ') }
      : {})
  }))
)
const tableScroll = computed(() => ({
  ...props.scroll,
  y: fillHeight.value
    ? bodyHeight.value
    : typeof props.scroll?.y === 'number' && !Number.isFinite(props.scroll.y)
      ? undefined
      : props.scroll?.y,
  x: Math.max(
    typeof props.scroll?.x === 'number' ? props.scroll.x : 0,
    resizableColumns.value.reduce((sum, column) => sum + column.width, 0)
  )
}))
function resize(width: number, column: any) {
  if (column.fixed === 'right' && column.className?.includes('gateway-actions-cell')) return
  widths.value = { ...widths.value, [String(column.key)]: columnWidth(width) }
  saveColumnWidths(storageKey.value, widths.value)
  if (typeof attrs.onResizeColumn === 'function') attrs.onResizeColumn(width, column)
}
</script>
