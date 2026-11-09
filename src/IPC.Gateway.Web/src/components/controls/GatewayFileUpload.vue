<template>
  <component
    :is="drag ? Upload.Dragger : Upload"
    v-bind="$attrs"
    :before-upload="beforeUpload"
    :show-upload-list="showUploadList"
    :disabled="disabled"
  >
    <slot />
  </component>
  <slot name="tip" />
</template>
<script setup lang="ts">
import type { FileSelection } from "../../utils/ui";
import { Upload } from "ant-design-vue";
defineOptions({ inheritAttrs: false });
const props = defineProps<{
  onChange?: (file: FileSelection) => unknown;
  showUploadList?: boolean;
  disabled?: boolean;
  drag?: boolean;
}>();
function beforeUpload(file: File) {
  props.onChange?.({ raw: file, name: file.name });
  return false;
}
</script>
