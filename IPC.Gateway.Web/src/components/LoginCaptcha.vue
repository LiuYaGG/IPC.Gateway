<template>
  <el-form-item label="验证码" :error="errorMessage" class="login-captcha-item">
    <div class="login-captcha">
      <el-input
        v-model="captchaInput"
        maxlength="4"
        autocomplete="off"
        placeholder="请输入验证码"
        @input="clearError"
      />
      <button class="login-captcha__image" type="button" title="点击刷新验证码" @click="refresh">
        <canvas ref="canvasRef" width="112" height="38"></canvas>
      </button>
    </div>
  </el-form-item>
</template>

<script setup lang="ts">
import { nextTick, onMounted, ref } from 'vue'

const chars = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789'
const canvasRef = ref<HTMLCanvasElement | null>(null)
const captchaCode = ref('')
const captchaInput = ref('')
const errorMessage = ref('')

onMounted(refresh)

function clearError() {
  if (errorMessage.value) errorMessage.value = ''
}

function generateCode() {
  let value = ''
  for (let index = 0; index < 4; index += 1) {
    value += chars[Math.floor(Math.random() * chars.length)]
  }
  return value
}

function drawCaptcha() {
  const canvas = canvasRef.value
  const context = canvas?.getContext('2d')
  if (!canvas || !context) return

  context.clearRect(0, 0, canvas.width, canvas.height)
  const gradient = context.createLinearGradient(0, 0, canvas.width, canvas.height)
  gradient.addColorStop(0, 'rgba(20, 184, 166, 0.2)')
  gradient.addColorStop(1, 'rgba(37, 99, 235, 0.22)')
  context.fillStyle = gradient
  context.fillRect(0, 0, canvas.width, canvas.height)

  for (let index = 0; index < 9; index += 1) {
    context.strokeStyle = index % 2 === 0 ? 'rgba(45, 212, 191, 0.42)' : 'rgba(96, 165, 250, 0.3)'
    context.beginPath()
    context.moveTo(Math.random() * canvas.width, Math.random() * canvas.height)
    context.lineTo(Math.random() * canvas.width, Math.random() * canvas.height)
    context.stroke()
  }

  captchaCode.value.split('').forEach((char, index) => {
    context.save()
    context.translate(18 + index * 24, 24)
    context.rotate((Math.random() - 0.5) * 0.42)
    context.fillStyle = '#e8feff'
    context.font = '700 22px Arial'
    context.fillText(char, 0, 0)
    context.restore()
  })
}

async function refresh() {
  captchaCode.value = generateCode()
  captchaInput.value = ''
  errorMessage.value = ''
  await nextTick()
  drawCaptcha()
}

function validate() {
  const normalizedInput = captchaInput.value.trim().toUpperCase()
  if (!normalizedInput) {
    errorMessage.value = '请输入验证码'
    return false
  }

  if (normalizedInput !== captchaCode.value) {
    errorMessage.value = '验证码不正确'
    refresh()
    return false
  }

  errorMessage.value = ''
  return true
}

defineExpose({ refresh, validate })
</script>

<style scoped>
.login-captcha {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 112px;
  gap: 10px;
  width: 100%;
  align-items: center;
}

.login-captcha__image {
  width: 112px;
  height: 38px;
  padding: 0;
  overflow: hidden;
  cursor: pointer;
  border: 1px solid rgba(45, 212, 191, 0.34);
  border-radius: 8px;
  background: rgba(15, 23, 42, 0.54);
  box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.08);
}

.login-captcha__image canvas {
  display: block;
}

@media (max-width: 520px) {
  .login-captcha {
    grid-template-columns: 1fr;
  }

  .login-captcha__image {
    width: 100%;
  }
}
</style>
