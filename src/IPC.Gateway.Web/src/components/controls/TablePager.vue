<template>
  <footer class="table-pager">
    <span class="table-pager__summary">{{ formatMessage('本页 {0} 条 · 共 {1} 条', [range.count, total]) }}</span>
    <div class="table-pager__controls">
      <span>{{ formatMessage('第 {0}/{1} 页', [range.current, range.pages]) }}</span>
      <a-pagination
        :current="range.current"
        :page-size="range.pageSize"
        :total="total"
        size="small"
        show-less-items
        :show-size-changer="false"
        :hide-on-single-page="false"
        :disabled="disabled"
        @change="changePage"
      />
      <a-select
        class="table-pager__size"
        :value="range.pageSize"
        :aria-label="tx('每页行数')"
        :disabled="disabled"
        :get-popup-container="popupContainer"
        placement="topLeft"
        :options="pageSizeOptions.map((size) => ({ value: size, label: formatMessage('{0} 条/页', [size]) }))"
        @change="changeSize"
      />
      <span class="table-pager__jump">
        {{ tx('跳至') }}
        <a-input-number
          v-model:value="jumpPage"
          :aria-label="tx('跳转页码')"
          :controls="false"
          :min="1"
          :max="range.pages"
          :precision="0"
          :disabled="disabled"
          @press-enter="jump"
          @blur="jump"
        />
        {{ tx('页') }}
      </span>
    </div>
  </footer>
</template>
<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { formatMessage, tx } from '../../i18n'
import { pageSizeOptions, pageWindow } from '../../utils/pagination'
const props = defineProps<{ total: number; current: number; pageSize: number; disabled?: boolean }>()
const emit = defineEmits<{ change: [page: number, size: number] }>()
const range = computed(() => pageWindow(props.total, props.current, props.pageSize))
const jumpPage = ref<number | null>(props.current)
watch(
  () => range.value.current,
  (value) => {
    jumpPage.value = value
  }
)
const popupContainer = () => document.body
function changePage(page: number) {
  emit('change', page, range.value.pageSize)
}
function changeSize(size: unknown) {
  emit('change', 1, Number(size))
}
function jump() {
  if (props.disabled) return
  const page = pageWindow(props.total, jumpPage.value ?? range.value.current, range.value.pageSize).current
  jumpPage.value = page
  if (page !== range.value.current) emit('change', page, range.value.pageSize)
}
</script>
