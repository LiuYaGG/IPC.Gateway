<template>
  <el-dialog
    :model-value="modelValue"
    title="重置密码"
    width="460px"
    destroy-on-close
    class="reset-password-dialog"
    @update:model-value="emit('update:modelValue', $event)"
    @closed="reset"
  >
    <div class="reset-password-target">
      <span>目标人员</span>
      <strong>{{ displayName || username || '-' }}</strong>
      <small>{{ username }}</small>
    </div>

    <el-form ref="formRef" :model="form" :rules="rules" label-width="96px">
      <el-form-item label="新密码" prop="newPassword">
        <el-input
          v-model="form.newPassword"
          type="password"
          show-password
          autocomplete="new-password"
          placeholder="请输入新密码"
        />
      </el-form-item>
      <el-form-item label="确认密码" prop="confirmPassword">
        <el-input
          v-model="form.confirmPassword"
          type="password"
          show-password
          autocomplete="new-password"
          placeholder="请再次输入新密码"
          @keyup.enter="submit"
        />
      </el-form-item>
      <div class="password-policy-note">
        密码复杂度以后端工业安全策略为准，保存时会自动校验。
      </div>
    </el-form>

    <template #footer>
      <div class="dialog-footer">
        <el-button @click="emit('update:modelValue', false)">取消</el-button>
        <el-button type="primary" :loading="saving" @click="submit">保存</el-button>
      </div>
    </template>
  </el-dialog>
</template>

<script setup lang="ts">
import { reactive, ref } from 'vue'
import { ElMessage, type FormInstance, type FormRules } from 'element-plus'
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

function validateNewPassword(_: unknown, value: string, callback: (error?: Error) => void) {
  if (!value) return callback(new Error('请输入新密码'))
  if (value.length < 8) return callback(new Error('新密码长度不能少于 8 位'))
  if (form.confirmPassword) formRef.value?.validateField('confirmPassword')
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
    ElMessage.success('密码已重置')
    emit('saved')
    emit('update:modelValue', false)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '密码重置失败')
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
  font-size: 12px;
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
