<template>
  <section class="view-stack">
    <el-card shadow="never" class="panel-card">
      <template #header>
        <div class="card-header">
          <span>项目 JSON</span>
          <el-button v-if="canSave" type="success" :icon="Check" @click="emit('save-json')">保存</el-button>
        </div>
      </template>
      <el-input
        :model-value="projectJson"
        type="textarea"
        :autosize="{ minRows: 24, maxRows: 34 }"
        class="json-editor"
        :readonly="!canSave"
        @update:model-value="emit('update:project-json', $event)"
      />
    </el-card>
  </section>
</template>

<script setup lang="ts">
import { Check } from '@element-plus/icons-vue'

defineProps<{
  projectJson: string
  canSave?: boolean
}>()

const emit = defineEmits<{
  'update:project-json': [value: string]
  'save-json': []
}>()
</script>
