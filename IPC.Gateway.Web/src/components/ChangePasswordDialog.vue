<template>
  <el-dialog
    :model-value="modelValue"
    title="修改密码"
    width="460px"
    destroy-on-close
    class="change-password-dialog"
    @update:model-value="emit('update:modelValue', $event)"
    @closed="reset"
  >
    <el-form ref="formRef" :model="form" :rules="rules" label-width="96px">
      <el-form-item label="当前密码" prop="currentPassword">
        <el-input
          v-model="form.currentPassword"
          type="password"
          show-password
          autocomplete="current-password"
          placeholder="请输入当前密码"
        />
      </el-form-item>
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

function validateNewPassword(_: unknown, value: string, callback: (error?: Error) => void) {
  const password = value ?? ''
  if (!password) return callback(new Error('请输入新密码'))
  if (password === form.currentPassword) return callback(new Error('新密码不能与当前密码相同'))
  if (form.confirmPassword) formRef.value?.validateField('confirmPassword')
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
    ElMessage.success('密码已修改')
    emit('saved')
    emit('update:modelValue', false)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '密码修改失败')
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
  font-size: 12px;
  line-height: 1.6;
}

@media (max-width: 560px) {
  .password-policy-note {
    margin-left: 0;
  }
}
</style>
