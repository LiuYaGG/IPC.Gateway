<template>
  <div class="tag-selector">
    <div class="tag-selector__filters">
      <el-select v-model="deviceFilter" clearable filterable placeholder="设备" @change="changeDeviceFilter">
        <el-option v-for="item in deviceOptions" :key="item.id" :label="item.name" :value="item.id" />
      </el-select>
      <el-select
        v-model="groupFilter"
        clearable
        filterable
        placeholder="分组"
        :disabled="!deviceFilter"
        @change="changeGroupFilter"
      >
        <el-option label="直属标签" :value="DIRECT_TAG_GROUP_KEY" />
        <el-option v-for="item in groupOptions" :key="item.id" :label="item.name" :value="item.id" />
      </el-select>
    </div>

    <el-select
      v-model="selectedKey"
      filterable
      clearable
      placeholder="选择标签"
      :disabled="disabled || filteredTags.length === 0"
      @change="changeTag"
    >
      <el-option v-for="item in filteredTags" :key="item.key" :label="item.label" :value="item.key">
        <div class="tag-selector__option">
          <span>{{ item.label }}</span>
          <small>{{ item.pointCode || item.address || '-' }} · {{ item.dataType || '-' }}</small>
        </div>
      </el-option>
    </el-select>

    <div v-if="selectedTag" class="tag-selector__summary">
      <div><span>设备名</span><strong>{{ selectedTag.deviceName || '-' }}</strong></div>
      <div><span>分组名</span><strong>{{ selectedTag.groupLabel }}</strong></div>
      <div><span>标签名</span><strong>{{ selectedTag.tagName || '-' }}</strong></div>
      <div><span>点位编码</span><strong>{{ selectedTag.pointCode || '-' }}</strong></div>
      <div><span>数据类型</span><strong>{{ selectedTag.dataType || '-' }}</strong></div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { ProjectConfig } from '../api'
import { buildTagSelections, DIRECT_TAG_GROUP_KEY, type TagSelection } from '../utils/tagSelection'

const props = defineProps<{
  modelValue: string
  project: ProjectConfig | null
  disabled?: boolean
}>()

const emit = defineEmits<{
  'update:modelValue': [value: string]
  change: [value: TagSelection | null]
}>()

const selectedKey = ref(props.modelValue)
const deviceFilter = ref('')
const groupFilter = ref('')

const allTags = computed(() => buildTagSelections(props.project))
const selectedTag = computed(() => allTags.value.find(item => item.key === selectedKey.value) ?? null)
const deviceOptions = computed(() => props.project?.devices ?? [])
const groupOptions = computed(() => {
  const device = props.project?.devices?.find(item => item.id === deviceFilter.value)
  return device?.groups ?? []
})
const filteredTags = computed(() => allTags.value.filter(item => {
  if (deviceFilter.value && item.deviceId !== deviceFilter.value) return false
  if (groupFilter.value === DIRECT_TAG_GROUP_KEY) return !item.groupId
  if (groupFilter.value && item.groupId !== groupFilter.value) return false
  return true
}))

watch(() => props.modelValue, value => {
  selectedKey.value = value || ''
  syncFiltersFromSelection({ resetWhenNoSelection: true })
})

watch(() => props.project, () => {
  syncFiltersFromSelection({ resetWhenNoSelection: false })
})

function changeDeviceFilter() {
  groupFilter.value = ''
  clearSelection()
}

function changeGroupFilter() {
  clearSelection()
}

function changeTag(value: string) {
  selectedKey.value = value || ''
  emit('update:modelValue', selectedKey.value)
  const selection = allTags.value.find(item => item.key === selectedKey.value) ?? null
  if (selection) {
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
      deviceFilter.value = ''
      groupFilter.value = ''
      return
    }

    keepValidFilters()
    return
  }

  const selection = allTags.value.find(item => item.key === selectedKey.value)
  if (!selection) return
  deviceFilter.value = selection.deviceId
  groupFilter.value = selection.groupId || DIRECT_TAG_GROUP_KEY
}

function keepValidFilters() {
  if (!deviceFilter.value) {
    groupFilter.value = ''
    return
  }

  const deviceExists = deviceOptions.value.some(item => item.id === deviceFilter.value)
  if (!deviceExists) {
    deviceFilter.value = ''
    groupFilter.value = ''
    return
  }

  if (!groupFilter.value || groupFilter.value === DIRECT_TAG_GROUP_KEY) return
  const groupExists = groupOptions.value.some(item => item.id === groupFilter.value)
  if (!groupExists) groupFilter.value = ''
}

syncFiltersFromSelection({ resetWhenNoSelection: true })
</script>
