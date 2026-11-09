<template>
  <a-modal
    centered
    :open="modelValue"
    :title="tx('重置密码')"
    width="460px"
    destroy-on-close
    class="reset-password-dialog"
    @update:open="emit('update:modelValue', $event)"
    @after-close="reset"
    :footer="null"
  >
    <div class="reset-password-target">
      <span>{{ tx('目标人员') }}</span>
      <strong>{{ displayName || username || '-' }}</strong>
      <small>{{ username }}</small>
    </div>
    <a-form
      ref="formRef"
      :model="form"
      :rules="antRules"
      :label-col="{ style: { width: '96px' } }"
      :wrapper-col="{ style: { flex: 1 } }"
      label-align="left"
    >
      <a-form-item :label="tx('新密码')" :name="['newPassword']">
        <gateway-input
          v-model="form.newPassword"
          type="password"
          show-password
          autocomplete="new-password"
          :placeholder="tx('请输入新密码')"
        />
      </a-form-item>
      <a-form-item :label="tx('确认密码')" :name="['confirmPassword']">
        <gateway-input
          v-model="form.confirmPassword"
          type="password"
          show-password
          autocomplete="new-password"
          :placeholder="tx('请再次输入新密码')"
          @keyup.enter="submit"
        />
      </a-form-item>
      <div class="password-policy-note">{{ tx('密码复杂度以后端工业安全策略为准，保存时会自动校验。') }}</div>
    </a-form>
    <template #footer>
      <div class="dialog-footer">
        <a-button @click="emit('update:modelValue', false)">{{ tx('取消') }}</a-button>
        <a-button type="primary" :loading="saving" @click="submit">{{ tx('保存') }}</a-button>
      </div>
    </template>
  </a-modal>
</template>

<script setup lang="ts">
import { notify } from '../utils/ui'
import { tx } from '../i18n'
import { toAntRules, type FormRules } from '../utils/ui'
import { computed, reactive, ref } from 'vue'
import { type FormInstance } from 'ant-design-vue'
import { resetUserPassword } from '../api'

interface ResetPasswordForm {
  newPassword: string
  confirmPassword: string
}

const props = defineProps<{
  modelValue: boolean
  username: string
  displayName: string
}>()

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  saved: []
}>()

const formRef = ref<FormInstance>()
const saving = ref(false)
const form = reactive<ResetPasswordForm>({
  newPassword: '',
  confirmPassword: ''
})

const rules: FormRules<ResetPasswordForm> = {
  newPassword: [{ validator: validateNewPassword, trigger: 'blur' }],
  confirmPassword: [{ validator: validateConfirmPassword, trigger: 'blur' }]
}

const antRules = computed(() => toAntRules(rules, tx))

function validateNewPassword(_: unknown, value: string, callback: (error?: Error) => void) {
  if (!value) return callback(new Error('请输入新密码'))
  if (value.length < 8) return callback(new Error('新密码长度不能少于 8 位'))
  if (form.confirmPassword) formRef.value?.validateFields(['confirmPassword']).catch(() => {})
  callback()
}

function validateConfirmPassword(_: unknown, value: string, callback: (error?: Error) => void) {
  if (!value) return callback(new Error('请再次输入新密码'))
  if (value !== form.newPassword) return callback(new Error('两次输入的新密码不一致'))
  callback()
}

async function submit() {
  if (saving.value || !props.username) return
  const valid = await formRef.value?.validate().catch(() => false)
  if (!valid) return

  saving.value = true
  try {
    await resetUserPassword(props.username, { newPassword: form.newPassword })
    notify.success('密码已重置')
    emit('saved')
    emit('update:modelValue', false)
  } catch (error) {
    notify.error(error instanceof Error ? error.message : '密码重置失败')
  } finally {
    saving.value = false
  }
}

function reset() {
  form.newPassword = ''
  form.confirmPassword = ''
  formRef.value?.clearValidate()
}
</script>

<style scoped>
.reset-password-target {
  display: grid;
  gap: 4px;
  margin-bottom: 18px;
  padding: 12px 14px;
  border: 1px solid #d8e2ea;
  border-radius: 8px;
  background: #f8fafc;
}

.reset-password-target span,
.reset-password-target small,
.password-policy-note {
  color: #64748b;
  font-size: calc(12px + 1pt);
}

.reset-password-target strong {
  color: #0f172a;
}

.password-policy-note {
  margin: -4px 0 4px 96px;
  line-height: 1.6;
}

@media (max-width: 560px) {
  .password-policy-note {
    margin-left: 0;
  }
}
</style>
