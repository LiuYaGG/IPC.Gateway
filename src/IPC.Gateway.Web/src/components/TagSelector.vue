<template>
  <div class="tag-selector" :class="{ 'tag-selector--stacked': stacked }">
    <div class="tag-selector__filters">
      <gateway-select
        v-model="channelFilter"
        allow-clear
        searchable
        :placeholder="tx('通道')"
        @change="changeChannelFilter"
      >
        <a-select-option v-for="item in channelOptions" :key="item.id" :label="item.name" :value="item.id">
          {{ item.name }}
        </a-select-option>
      </gateway-select>
      <gateway-select
        v-model="deviceFilter"
        allow-clear
        searchable
        :placeholder="tx('设备')"
        :disabled="!channelFilter"
        @change="changeDeviceFilter"
      >
        <a-select-option v-for="item in deviceOptions" :key="item.id" :label="item.name" :value="item.id">
          {{ item.name }}
        </a-select-option>
      </gateway-select>
      <gateway-select
        v-model="groupFilter"
        allow-clear
        searchable
        :placeholder="tx('分组')"
        :disabled="!deviceFilter"
        @change="changeGroupFilter"
      >
        <a-select-option :label="tx('直属标签')" :value="DIRECT_TAG_GROUP_KEY">{{ tx('直属标签') }}</a-select-option>
        <a-select-option v-for="item in groupOptions" :key="item.id" :label="item.name" :value="item.id">
          {{ item.name }}
        </a-select-option>
      </gateway-select>
    </div>
    <gateway-select
      v-model="selectedKey"
      searchable
      allow-clear
      :placeholder="tx('选择标签')"
      :disabled="disabled || filteredTags.length === 0"
      @change="changeTag"
    >
      <a-select-option v-for="item in filteredTags" :key="item.key" :label="nameOnly ? item.tagName : tx(item.label)" :value="item.key">
        <div class="tag-selector__option">
          <span>{{ nameOnly ? item.tagName : tx(item.label) }}</span>
          <small v-if="!nameOnly">{{ item.pointCode || item.address || '-' }} · {{ item.dataType || '-' }}</small>
        </div>
      </a-select-option>
    </gateway-select>
    <div v-if="selectedTag" class="tag-selector__summary">
      <div>
        <span>{{ tx('通道名') }}</span>
        <strong>{{ selectedTag.channelName || '-' }}</strong>
      </div>
      <div>
        <span>{{ tx('设备名') }}</span>
        <strong>{{ selectedTag.deviceName || '-' }}</strong>
      </div>
      <div>
        <span>{{ tx('分组名') }}</span>
        <strong>{{ tx(selectedTag.groupLabel) }}</strong>
      </div>
      <div>
        <span>{{ tx('标签名') }}</span>
        <strong>{{ selectedTag.tagName || '-' }}</strong>
      </div>
      <div>
        <span>{{ tx('点位编码') }}</span>
        <strong>{{ selectedTag.pointCode || '-' }}</strong>
      </div>
      <div>
        <span>{{ tx('数据类型') }}</span>
        <strong>{{ selectedTag.dataType || '-' }}</strong>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { tx } from '../i18n'
import { computed, ref, watch } from 'vue'
import type { ProjectConfig } from '../api'
import { buildTagSelections, DIRECT_TAG_GROUP_KEY, type TagSelection } from '../utils/tagSelection'

const props = defineProps<{
  modelValue: string
  project: ProjectConfig | null
  disabled?: boolean
  stacked?: boolean
  nameOnly?: boolean
}>()

const emit = defineEmits<{
  'update:modelValue': [value: string]
  change: [value: TagSelection | null]
}>()

const selectedKey = ref(props.modelValue)
const channelFilter = ref('')
const deviceFilter = ref('')
const groupFilter = ref('')

const allTags = computed(() => buildTagSelections(props.project))
const selectedTag = computed(() => allTags.value.find((item) => item.key === selectedKey.value) ?? null)
const channelOptions = computed(() => props.project?.channels ?? [])
const deviceOptions = computed(() =>
  (props.project?.devices ?? []).filter((item) => item.channelId === channelFilter.value)
)
const groupOptions = computed(() => {
  const device = props.project?.devices?.find((item) => item.id === deviceFilter.value)
  return device?.groups ?? []
})
const filteredTags = computed(() =>
  allTags.value.filter((item) => {
    if (channelFilter.value && item.channelId !== channelFilter.value) return false
    if (deviceFilter.value && item.deviceId !== deviceFilter.value) return false
    if (groupFilter.value === DIRECT_TAG_GROUP_KEY) return !item.groupId
    if (groupFilter.value && item.groupId !== groupFilter.value) return false
    return true
  })
)

watch(
  () => props.modelValue,
  (value) => {
    const nextKey = value || ''
    if (nextKey === selectedKey.value) return
    selectedKey.value = nextKey
    syncFiltersFromSelection({ resetWhenNoSelection: true })
  }
)

watch(
  () => props.project,
  () => {
    syncFiltersFromSelection({ resetWhenNoSelection: false })
  }
)

function changeDeviceFilter() {
  groupFilter.value = ''
  clearSelection()
}

function changeChannelFilter() {
  deviceFilter.value = ''
  groupFilter.value = ''
  clearSelection()
}

function changeGroupFilter() {
  clearSelection()
}

function changeTag(value: string) {
  selectedKey.value = value || ''
  emit('update:modelValue', selectedKey.value)
  const selection = allTags.value.find((item) => item.key === selectedKey.value) ?? null
  if (selection) {
    channelFilter.value = selection.channelId
    deviceFilter.value = selection.deviceId
    groupFilter.value = selection.groupId || DIRECT_TAG_GROUP_KEY
  }
  emit('change', selection)
}

function clearSelection() {
  selectedKey.value = ''
  emit('update:modelValue', '')
  emit('change', null)
}

function syncFiltersFromSelection(options: { resetWhenNoSelection: boolean } = { resetWhenNoSelection: true }) {
  if (!selectedKey.value) {
    if (options.resetWhenNoSelection) {
      channelFilter.value = ''
      deviceFilter.value = ''
      groupFilter.value = ''
      return
    }

    keepValidFilters()
    return
  }

  const selection = allTags.value.find((item) => item.key === selectedKey.value)
  if (!selection) return
  channelFilter.value = selection.channelId
  deviceFilter.value = selection.deviceId
  groupFilter.value = selection.groupId || DIRECT_TAG_GROUP_KEY
}

function keepValidFilters() {
  if (!channelFilter.value || !channelOptions.value.some((item) => item.id === channelFilter.value)) {
    channelFilter.value = ''
    deviceFilter.value = ''
    groupFilter.value = ''
    return
  }

  if (!deviceFilter.value) {
    groupFilter.value = ''
    return
  }

  const deviceExists = deviceOptions.value.some((item) => item.id === deviceFilter.value)
  if (!deviceExists) {
    deviceFilter.value = ''
    groupFilter.value = ''
    return
  }

  if (!groupFilter.value || groupFilter.value === DIRECT_TAG_GROUP_KEY) return
  const groupExists = groupOptions.value.some((item) => item.id === groupFilter.value)
  if (!groupExists) groupFilter.value = ''
}

syncFiltersFromSelection({ resetWhenNoSelection: true })
</script>

<style scoped>
.tag-selector--stacked {
  min-width: 0;
}

.tag-selector--stacked .tag-selector__filters,
.tag-selector--stacked .tag-selector__summary {
  grid-template-columns: minmax(0, 1fr);
}

.tag-selector--stacked .tag-selector__option {
  grid-template-columns: minmax(0, 1fr);
}

.tag-selector--stacked :deep(.ant-select) {
  width: 100%;
  min-width: 0;
}

.tag-selector--stacked .tag-selector__summary strong {
  overflow-wrap: anywhere;
}
</style>
