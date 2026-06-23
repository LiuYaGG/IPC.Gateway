<template>
  <section class="device-topology-view">
    <TopologyToolbar
      v-model:search="searchText"
      v-model:show-tag-nodes="showTagNodes"
      @fit="fitCanvas"
    />
    <TopologySummary :summary="model.summary" />

    <el-alert
      v-if="model.tagNodeLimitReached"
      type="warning"
      :closable="false"
      show-icon
      title="标签数量较多，拓扑已自动折叠部分标签节点，避免画布卡顿。"
    />

    <div class="device-topology-view__main">
      <div class="device-topology-view__canvas">
        <TopologyCanvas
          :option="chartOption"
          :empty="model.nodes.length <= 1"
          :fit-token="fitToken"
          @select-node="selectNode"
        />
        <TopologyLegend />
      </div>
      <TopologyDetails :node="selectedNode" @select-error="emit('select-error', $event)" />
    </div>
  </section>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { GatewayStatus, ProjectConfig, RuntimeErrorDetail } from '../../api'
import { buildDeviceTopology } from './topologyModel'
import { buildTopologyOption } from './topologyOption'
import type { TopologyNode } from './topologyTypes'
import TopologyCanvas from './TopologyCanvas.vue'
import TopologyDetails from './TopologyDetails.vue'
import TopologyLegend from './TopologyLegend.vue'
import TopologySummary from './TopologySummary.vue'
import TopologyToolbar from './TopologyToolbar.vue'

const props = defineProps<{
  project: ProjectConfig | null
  status: GatewayStatus | null
}>()

const emit = defineEmits<{
  'select-error': [error: RuntimeErrorDetail]
}>()

const searchText = ref('')
const debouncedSearch = ref('')
const showTagNodes = ref(false)
const selectedNodeId = ref('gateway')
const fitToken = ref(0)
let searchTimer: number | undefined

const model = computed(() => buildDeviceTopology({
  project: props.project,
  status: props.status,
  search: debouncedSearch.value,
  showTagNodes: showTagNodes.value
}))

const selectedNode = computed(() => model.value.nodes.find(node => node.id === selectedNodeId.value) ?? model.value.nodes[0] ?? null)
const chartOption = computed(() => buildTopologyOption(model.value, selectedNode.value?.id || ''))

watch(searchText, value => {
  window.clearTimeout(searchTimer)
  searchTimer = window.setTimeout(() => {
    debouncedSearch.value = value
  }, 260)
})

watch(model, value => {
  if (!value.nodes.some(node => node.id === selectedNodeId.value)) {
    selectedNodeId.value = value.nodes[0]?.id || 'gateway'
  }
})

function selectNode(node: TopologyNode) {
  selectedNodeId.value = node.id
}

function fitCanvas() {
  fitToken.value += 1
}
</script>

<style scoped>
.device-topology-view {
  display: grid;
  gap: 14px;
}

.device-topology-view__main {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 320px;
  gap: 14px;
  align-items: start;
}

.device-topology-view__canvas {
  display: grid;
  gap: 10px;
  min-width: 0;
}

@media (max-width: 1240px) {
  .device-topology-view__main {
    grid-template-columns: 1fr;
  }
}
</style>
