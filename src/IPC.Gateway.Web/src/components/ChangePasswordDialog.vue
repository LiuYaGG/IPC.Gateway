<template>
  <a-modal
    centered
    :open="modelValue"
    :title="tx('修改密码')"
    width="460px"
    destroy-on-close
    class="change-password-dialog"
    @update:open="emit('update:modelValue', $event)"
    @after-close="reset"
    :footer="null"
  >
    <a-form
      ref="formRef"
      :model="form"
      :rules="antRules"
      :label-col="{ style: { width: '96px' } }"
      :wrapper-col="{ style: { flex: 1 } }"
      label-align="left"
    >
      <a-form-item :label="tx('当前密码')" :name="['currentPassword']">
        <gateway-input
          v-model="form.currentPassword"
          type="password"
          show-password
          autocomplete="current-password"
          :placeholder="tx('请输入当前密码')"
        />
      </a-form-item>
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
import { changeCurrentPassword } from '../api'

interface ChangePasswordForm {
  currentPassword: string
  newPassword: string
  confirmPassword: string
}

defineProps<{ modelValue: boolean }>()

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  saved: []
}>()

const formRef = ref<FormInstance>()
const saving = ref(false)
const form = reactive<ChangePasswordForm>({
  currentPassword: '',
  newPassword: '',
  confirmPassword: ''
})

const rules: FormRules<ChangePasswordForm> = {
  currentPassword: [{ required: true, message: '请输入当前密码', trigger: 'blur' }],
  newPassword: [{ validator: validateNewPassword, trigger: 'blur' }],
  confirmPassword: [{ validator: validateConfirmPassword, trigger: 'blur' }]
}

const antRules = computed(() => toAntRules(rules, tx))

function validateNewPassword(_: unknown, value: string, callback: (error?: Error) => void) {
  const password = value ?? ''
  if (!password) return callback(new Error('请输入新密码'))
  if (password === form.currentPassword) return callback(new Error('新密码不能与当前密码相同'))
  if (form.confirmPassword) formRef.value?.validateFields(['confirmPassword']).catch(() => {})
  callback()
}

function validateConfirmPassword(_: unknown, value: string, callback: (error?: Error) => void) {
  if (!value) return callback(new Error('请再次输入新密码'))
  if (value !== form.newPassword) return callback(new Error('两次输入的新密码不一致'))
  callback()
}

async function submit() {
  if (saving.value) return
  const valid = await formRef.value?.validate().catch(() => false)
  if (!valid) return

  saving.value = true
  try {
    await changeCurrentPassword(form.currentPassword, form.newPassword)
    notify.success('密码已修改')
    emit('saved')
    emit('update:modelValue', false)
  } catch (error) {
    notify.error(error instanceof Error ? error.message : '密码修改失败')
  } finally {
    saving.value = false
  }
}

function reset() {
  form.currentPassword = ''
  form.newPassword = ''
  form.confirmPassword = ''
  formRef.value?.clearValidate()
}
</script>

<style scoped>
.password-policy-note {
  margin: -4px 0 4px 96px;
  color: #64748b;
  font-size: calc(12px + 1pt);
  line-height: 1.6;
}

@media (max-width: 560px) {
  .password-policy-note {
    margin-left: 0;
  }
}
</style>
