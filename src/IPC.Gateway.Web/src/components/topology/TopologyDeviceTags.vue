<template>
  <a-modal open centered :title="device.name + ' · ' + tx('标签')" :footer="null" width="min(960px, 95vw)" @cancel="emit('close')">
    <div class="tag-content">
      <a-input v-model:value="search" allow-clear :aria-label="tx('搜索分组、标签或地址')" :placeholder="tx('搜索分组、标签或地址')"/>
      <AutoHeightTable :paging="false" resize-key="topology-device-tags" :columns="columns" :data-source="rows" row-key="key" :scroll-x="760">
        <template #bodyCell="{ column, record }">
          <template v-if="column.key === 'quality'">{{ tx(qualityText(record.quality)) }}</template>
          <template v-if="column.key === 'value'"><span class="tag-value" :title="record.value">{{ record.value }}</span></template>
        </template>
      </AutoHeightTable>
      <TablePager :total="matches.length" :current="page" :page-size="size" @change="changePage" />
    </div>
  </a-modal>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { DeviceConfig, TagValueSnapshot } from '../../api'
import { tx } from '../../i18n'
import { qualityText } from '../../utils/display'
import AutoHeightTable from '../AutoHeightTable.vue'
import TablePager from '../controls/TablePager.vue'
import { normalizeKey, tagConfigKey } from './topologyStatus'
const props = defineProps<{
  device: DeviceConfig
  channelEnabled: boolean
  runtimeTags?: ReadonlyMap<string, TagValueSnapshot>
  initialSearch: string
}>()
const emit = defineEmits<{ close: [] }>()
const search = ref(props.initialSearch)
const page = ref(1), size = ref(20)
const columns = computed(() => [
  { key:'group', dataIndex:'group', title:tx('分组'), width:150, ellipsis:true },
  { key:'name', dataIndex:'name', title:tx('标签名'), width:170, ellipsis:true },
  { key:'address', dataIndex:'address', title:tx('地址'), width:125, ellipsis:true },
  { key:'dataType', dataIndex:'dataType', title:tx('数据类型'), width:110 },
  { key:'value', dataIndex:'value', title:tx('当前值'), width:180, ellipsis:true },
  { key:'quality', title:tx('质量'), width:120 }
])
const matches = computed(() => {
  const needle = normalizeKey(search.value)
  const direct = { id:'', name:tx('直属标签'), tags:props.device.tags ?? [], enabled:true }
  return [direct, ...(props.device.groups ?? [])].flatMap(g => {
    const groupMatch = normalizeKey(g.name).includes(needle)
    return (g.tags ?? []).filter(t => groupMatch || normalizeKey(t.name).includes(needle) || normalizeKey(t.address).includes(needle))
      .map(t => ({key:tagConfigKey(props.device,g.id,t), group:g.name, name:t.name, address:t.address, dataType:t.dataType,
        enabled:props.channelEnabled && props.device.enabled !== false && g.enabled !== false && t.enabled !== false}))
  })
})
const rows = computed(() => matches.value.slice((page.value-1)*size.value,page.value*size.value).map(item => {
  const state = props.runtimeTags?.get(item.key)
  return {...item, quality:item.enabled ? state?.quality : 'Disabled',
    value:item.enabled && state?.valueText != null ? state.valueText : '—'}
}))
watch(search, () => {page.value = 1})
watch(() => matches.value.length, total => {page.value = Math.min(page.value,Math.max(1,Math.ceil(total/size.value)))})
function changePage(next: number, newSize: number) { page.value = next; size.value = newSize }
</script>

<style scoped>
.tag-content { display: grid; grid-template-rows: auto minmax(0,1fr) auto; gap: 8px; height: min(560px, 62dvh); min-height: 180px; }
.tag-value { display:block; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
</style>
