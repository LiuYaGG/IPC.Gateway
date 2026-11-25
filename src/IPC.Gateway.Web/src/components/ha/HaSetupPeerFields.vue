<template>
  <div class="setup-fields">
    <a-form-item :label="locale==='zh-CN'?'配对地址':'Pairing URL'">
      <a-input :value="address" :disabled="disabled" placeholder="https://192.168.1.11:7381" @update:value="emit('update:address',$event)" />
    </a-form-item>
    <a-form-item :label="locale==='zh-CN'?'配对码':'Pairing code'">
      <a-input-password :value="code" :disabled="disabled" autocomplete="off" @update:value="emit('update:code',$event)" />
    </a-form-item>
  </div>
</template>
<script setup lang="ts">
import { locale } from '../../i18n'
defineProps<{address:string;code:string;disabled:boolean}>()
const emit=defineEmits<{'update:address':[string];'update:code':[string]}>()
</script>
