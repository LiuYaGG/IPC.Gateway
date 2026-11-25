<template>
  <div class="ha-node-row">
    <b>{{ member.nodeId }}</b>
    <a-form-item :label="tx('仲裁客户端地址')" required>
      <a-input :value="member.clientUrl" :disabled="locked || transportLocked" placeholder="https://host:port" @update:value="update('clientUrl',$event)" />
    </a-form-item>
    <a-form-item :label="tx('成员通信地址')" required>
      <a-input :value="member.peerUrl" :disabled="locked || transportLocked" placeholder="https://host:port" @update:value="update('peerUrl',$event)" />
    </a-form-item>
    <a-form-item :label="tx('证书引用')" required>
      <a-input :value="member.certificateRef" :disabled="locked || transportLocked" :placeholder="tx('已登记的 SHA-256 指纹或证书别名')" @update:value="update('certificateRef',$event)" />
    </a-form-item>
    <a-form-item v-if="member.nodeId !== 'C'" :label="tx('网关管理地址')" required>
      <a-input :value="member.gatewayUrl" :disabled="locked" placeholder="https://host:port" @update:value="update('gatewayUrl',$event)" />
    </a-form-item>
  </div>
</template>
<script setup lang="ts">
import { tx } from '../../i18n'
import type { HaMember } from '../../haApi'
const props = defineProps<{member: HaMember; locked: boolean; transportLocked?:boolean}>()
const emit = defineEmits<{change: [HaMember]}>()
function update(key: Exclude<keyof HaMember, 'nodeId'>, value: string) {
  emit('change', { ...props.member, [key]: value })
}
</script>
