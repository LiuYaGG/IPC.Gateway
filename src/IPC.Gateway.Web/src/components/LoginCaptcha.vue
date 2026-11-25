<template>
  <a-form-item
    :label="tx('安全验证')"
    :help="!modalOpen && errorMessage ? tx(errorMessage) : undefined"
    class="login-captcha-item"
    :validate-status="!modalOpen && errorMessage ? 'error' : undefined"
  >
    <a-button ref="entryRef" block html-type="button" class="login-captcha__entry" :class="{ 'login-captcha__entry--verified': verified }" @click="openVerification">
      <template #icon><CheckCircleOutlined v-if="verified" /><SafetyCertificateOutlined v-else /></template>
      {{ tx(verified ? '验证通过' : '点击完成安全验证') }}
    </a-button>
  </a-form-item>

  <a-modal
    :open="modalOpen"
    :title="tx('滑块拼图验证')"
    :width="400"
    centered
    :footer="null"
    :mask-closable="false"
    :z-index="securityModalLayers.captcha"
    :after-close="restoreEntryFocus"
    transition-name=""
    wrap-class-name="login-captcha-modal"
    @cancel="closeVerification"
  >
    <div class="login-captcha__content">
      <a-typography-paragraph class="login-captcha__instructions">{{ tx('拖动滑块，将拼图移入缺口') }}</a-typography-paragraph>
      <a-spin :spinning="pending === 'challenge'" :tip="tx('正在加载验证码')">
        <div
          ref="imageRef"
          class="login-captcha__puzzle"
          :style="{ aspectRatio: challenge ? `${challenge.width} / ${challenge.height}` : '320 / 160' }"
          :aria-busy="pending === 'challenge'"
        >
          <template v-if="challenge">
            <img
              :key="`${challenge.challengeId}-background`"
              :src="challenge.backgroundImage"
              :data-challenge-id="challenge.challengeId"
              class="login-captcha__background"
              :alt="tx('拼图验证码背景')"
              :draggable="false"
              @load="imageLoaded($event, 1)"
              @error="imageFailed"
            />
            <img
              :key="`${challenge.challengeId}-piece`"
              :src="challenge.pieceImage"
              :data-challenge-id="challenge.challengeId"
              class="login-captcha__piece"
              :style="{
                width: `${challenge.pieceWidth / challenge.width * 100}%`,
                height: `${challenge.pieceHeight / challenge.height * 100}%`,
                transform: pieceTransform,
                top: `${challenge.pieceY / challenge.height * 100}%`
              }"
              alt=""
              :draggable="false"
              @load="imageLoaded($event, 2)"
              @error="imageFailed"
            />
          </template>
          <a-typography-text v-else-if="pending !== 'challenge'" type="secondary">{{ tx('点击刷新获取新拼图') }}</a-typography-text>
        </div>
      </a-spin>

      <div class="login-captcha__rail" :class="{ 'login-captcha__rail--dragging': dragging }">
        <a-progress
          class="login-captcha__progress"
          :percent="maxOffset ? offsetX / maxOffset * 100 : 0"
          :show-info="false"
          :stroke-width="44"
          stroke-linecap="square"
          stroke-color="#c6e0d4"
          trail-color="#eef3ef"
        />
        <a-typography-text class="login-captcha__rail-label">{{ tx(pending === 'verify' ? '正在验证' : '向右拖动滑块') }}</a-typography-text>
        <a-button
          ref="sliderRef"
          html-type="button"
          type="primary"
          class="login-captcha__thumb"
          role="slider"
          aria-orientation="horizontal"
          :aria-label="tx('水平移动拼图')"
          :aria-valuemin="0"
          :aria-valuemax="maxOffset"
          :aria-valuenow="Math.round(offsetX)"
          :aria-describedby="keyboardHelpId"
          :disabled="!ready"
          :style="{
            transform: pieceTransform,
            width: challenge ? `${challenge.pieceWidth / challenge.width * 100}%` : '17.5%'
          }"
          @pointerdown="startDrag"
          @pointermove="moveDrag"
          @pointerup="finishDrag"
          @pointercancel="cancelDrag"
          @lostpointercapture="cancelDrag"
          @keydown="handleSliderKey"
        >
          <template #icon><LoadingOutlined v-if="pending === 'verify'" /><ArrowRightOutlined v-else /></template>
        </a-button>
      </div>
      <a-typography-paragraph :id="keyboardHelpId" type="secondary" class="login-captcha__keyboard-help">
        {{ tx('也可用方向键调整，按 Enter 或点击验证') }}
      </a-typography-paragraph>
      <div aria-live="polite" aria-atomic="true"><a-alert v-if="errorMessage" type="error" show-icon :message="tx(errorMessage)" /></div>
      <a-flex justify="space-between" align="center" :gap="12" class="login-captcha__actions">
        <a-button html-type="button" :disabled="!!pending" @click="requestChallenge()">
          <template #icon><ReloadOutlined /></template>{{ tx('刷新拼图') }}
        </a-button>
        <a-button html-type="button" type="primary" :loading="pending === 'verify'" :disabled="!ready || dragging" @click="verify">{{ tx('验证') }}</a-button>
      </a-flex>
    </div>
  </a-modal>
</template>

<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, useId, watch } from 'vue'
import { ArrowRightOutlined, CheckCircleOutlined, LoadingOutlined, ReloadOutlined, SafetyCertificateOutlined } from '@ant-design/icons-vue'
import { createCaptchaChallenge, verifyCaptcha, type GatewayCaptchaChallenge } from '../api'
import { tx } from '../i18n'
import { securityModalLayers } from '../utils/securityModalLayers'

const keyboardHelpId = `captcha-help-${useId()}`
const modalOpen = ref(false)
const challenge = ref<GatewayCaptchaChallenge | null>(null)
const imageRef = ref<HTMLElement | null>(null)
const entryRef = ref<{ focus: () => void } | null>(null)
const sliderRef = ref<{ focus: () => void } | null>(null)
const loadedImages = ref(0)
const offsetX = ref(0)
const errorMessage = ref('')
const pending = ref<'' | 'challenge' | 'verify'>('')
const dragging = ref(false)
const verificationToken = ref('')
const proofExpiresAt = ref(0)
const verified = computed(() => !!verificationToken.value && proofExpiresAt.value > Date.now())
const maxOffset = computed(() => challenge.value ? challenge.value.width - challenge.value.pieceWidth : 0)
// The image and the thumb have the same proportional width, so this transform
// uses their own width as its reference and stays aligned at every viewport size.
const pieceTransform = computed(() => `translate3d(${challenge.value ? offsetX.value / challenge.value.pieceWidth * 100 : 0}%, 0, 0)`)
const ready = computed(() => !!challenge.value && loadedImages.value === 3 && !pending.value)
watch(ready, async value => {
  if (!value || !modalOpen.value) return
  await nextTick()
  if (ready.value && modalOpen.value) sliderRef.value?.focus()
}, { flush: 'post' })
let requestId = 0
let requestController: AbortController | null = null
let proofTimer: ReturnType<typeof setTimeout> | undefined
let challengeTimer: ReturnType<typeof setTimeout> | undefined
let dragState: { pointerId: number; startX: number; startOffset: number; scale: number; target: HTMLElement } | null = null
let dragFrame = 0
let pendingOffset: number | null = null

function flushDragOffset() {
  if (dragFrame) cancelAnimationFrame(dragFrame)
  dragFrame = 0
  if (pendingOffset !== null) offsetX.value = pendingOffset
  pendingOffset = null
}

function stopDrag() {
  if (dragFrame) cancelAnimationFrame(dragFrame)
  dragFrame = 0
  pendingOffset = null
  const state = dragState
  dragState = null
  dragging.value = false
  if (state?.target.hasPointerCapture(state.pointerId)) state.target.releasePointerCapture(state.pointerId)
}

function cancelRequest() {
  requestId += 1
  requestController?.abort()
  requestController = null
  pending.value = ''
  stopDrag()
}

function clearChallenge() {
  clearTimeout(challengeTimer)
  challenge.value = null
  loadedImages.value = 0
  offsetX.value = 0
}

function clearProof() {
  clearTimeout(proofTimer)
  verificationToken.value = ''
  proofExpiresAt.value = 0
}

function getToken() {
  if (!verified.value || proofExpiresAt.value <= Date.now()) {
    if (verificationToken.value) {
      clearProof()
      errorMessage.value = '安全验证已过期，请重新验证'
    }
    return ''
  }
  return verificationToken.value
}

function refresh() {
  cancelRequest()
  clearChallenge()
  clearProof()
  modalOpen.value = false
  errorMessage.value = ''
}

function openVerification() {
  if (getToken() || modalOpen.value) return
  modalOpen.value = true
  void requestChallenge()
}

function closeVerification() {
  modalOpen.value = false
  cancelRequest()
  clearChallenge()
  if (!errorMessage.value) errorMessage.value = '安全验证已取消，请重新验证'
}

function restoreEntryFocus() {
  // Updating form help can replace the entry button. Focus its current instance
  // after the dialog closes instead of the stale DOM node captured on opening.
  entryRef.value?.focus()
}

function validate() {
  if (getToken()) return true
  openVerification()
  return false
}

async function requestChallenge(preserveError = false) {
  if (!modalOpen.value || pending.value) return
  cancelRequest()
  clearChallenge()
  clearProof()
  if (!preserveError) errorMessage.value = ''
  const id = requestId
  const controller = new AbortController()
  requestController = controller
  pending.value = 'challenge'
  let timedOut = false
  const timer = setTimeout(() => { timedOut = true; controller.abort() }, 15000)
  try {
    const result = await createCaptchaChallenge(controller.signal)
    if (id !== requestId || !modalOpen.value) return
    const expiry = Date.parse(result.expiresAt)
    if (!result.challengeId || !result.backgroundImage.startsWith('data:image/png;base64,') ||
      !result.pieceImage.startsWith('data:image/png;base64,') ||
      !Number.isFinite(result.width) || !Number.isFinite(result.height) ||
      !Number.isFinite(result.pieceWidth) || !Number.isFinite(result.pieceHeight) || !Number.isFinite(result.pieceY) ||
      result.width <= result.pieceWidth || result.pieceWidth <= 0 || result.pieceHeight <= 0 ||
      result.height < result.pieceHeight || result.pieceY < 0 || result.pieceY + result.pieceHeight > result.height ||
      !Number.isFinite(expiry) || expiry <= Date.now()) throw new Error('验证码数据无效，请刷新重试')
    challenge.value = result
    challengeTimer = setTimeout(() => {
      if (challenge.value?.challengeId !== result.challengeId) return
      cancelRequest()
      clearChallenge()
      errorMessage.value = '拼图已过期，请刷新后重试'
    }, expiry - Date.now())
  } catch (error) {
    if (id !== requestId || !modalOpen.value) return
    errorMessage.value = timedOut ? '验证码请求超时，请刷新重试' : error instanceof Error ? error.message : '验证码加载失败，请刷新重试'
  } finally {
    clearTimeout(timer)
    if (id === requestId) {
      requestController = null
      pending.value = ''
    }
  }
}

function imageLoaded(event: Event, bit: number) {
  if ((event.currentTarget as HTMLImageElement).dataset.challengeId === challenge.value?.challengeId) loadedImages.value |= bit
}

function imageFailed(event: Event) {
  if ((event.currentTarget as HTMLImageElement).dataset.challengeId !== challenge.value?.challengeId) return
  cancelRequest()
  clearChallenge()
  errorMessage.value = '验证码图片加载失败，请刷新重试'
}

function clampOffset(value: number) {
  return Math.max(0, Math.min(maxOffset.value, value))
}

function startDrag(event: PointerEvent) {
  if (dragState || !ready.value || !challenge.value || !event.isPrimary || (event.pointerType === 'mouse' && event.button !== 0)) return
  const displayedWidth = imageRef.value?.getBoundingClientRect().width ?? 0
  if (displayedWidth <= 0) return
  event.preventDefault()
  const target = event.currentTarget as HTMLElement
  target.focus({ preventScroll: true })
  dragState = { pointerId: event.pointerId, startX: event.clientX, startOffset: offsetX.value,
    scale: challenge.value.width / displayedWidth, target }
  dragging.value = true
  target.setPointerCapture(event.pointerId)
}

function moveDrag(event: PointerEvent) {
  if (!dragState || event.pointerId !== dragState.pointerId || !challenge.value) return
  event.preventDefault()
  pendingOffset = clampOffset(dragState.startOffset + (event.clientX - dragState.startX) * dragState.scale)
  if (!dragFrame) {
    dragFrame = requestAnimationFrame(() => {
      dragFrame = 0
      if (pendingOffset !== null) offsetX.value = pendingOffset
      pendingOffset = null
    })
  }
}

function finishDrag(event: PointerEvent) {
  if (!dragState || event.pointerId !== dragState.pointerId) return
  moveDrag(event)
  // pointerup may precede the next animation frame. Verify the final pointer
  // position rather than the previous frame, including quick flicks/releases.
  flushDragOffset()
  stopDrag()
  void verify()
}

function cancelDrag(event: PointerEvent) {
  if (!dragState || event.pointerId !== dragState.pointerId) return
  stopDrag()
  offsetX.value = 0
  errorMessage.value = '拖动已取消，请重新拖动'
}

function handleSliderKey(event: KeyboardEvent) {
  if (!['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End', 'Enter', ' '].includes(event.key)) return
  event.preventDefault()
  event.stopPropagation()
  if (!ready.value || dragging.value) return
  if (event.key === 'Enter' || event.key === ' ') { void verify(); return }
  const step = event.shiftKey ? 10 : 1
  if (event.key === 'Home') offsetX.value = 0
  else if (event.key === 'End') offsetX.value = maxOffset.value
  else offsetX.value = clampOffset(offsetX.value + (['ArrowRight', 'ArrowUp'].includes(event.key) ? step : -step))
}

async function verify() {
  if (!ready.value || dragging.value || !challenge.value) return
  if (Date.parse(challenge.value.expiresAt) <= Date.now()) {
    errorMessage.value = '拼图已过期，请刷新后重试'
    void requestChallenge(true)
    return
  }
  const id = requestId
  const controller = new AbortController()
  requestController = controller
  pending.value = 'verify'
  let timedOut = false
  const timer = setTimeout(() => { timedOut = true; controller.abort() }, 15000)
  try {
    const result = await verifyCaptcha(challenge.value.challengeId, Math.round(offsetX.value), controller.signal)
    if (id !== requestId || !modalOpen.value) return
    const expiry = Date.parse(result.expiresAt)
    if (!result.verificationToken || !Number.isFinite(expiry) || expiry <= Date.now()) throw new Error('安全验证已过期，请重新验证')
    cancelRequest()
    clearChallenge()
    verificationToken.value = result.verificationToken
    proofExpiresAt.value = expiry
    proofTimer = setTimeout(() => {
      clearProof()
      errorMessage.value = '安全验证已过期，请重新验证'
    }, expiry - Date.now())
    errorMessage.value = ''
    modalOpen.value = false
  } catch (error) {
    if (id !== requestId || !modalOpen.value) return
    errorMessage.value = timedOut ? '验证码请求超时，请刷新重试' : error instanceof Error ? error.message : '拼图验证失败，请重新验证'
    pending.value = ''
    void requestChallenge(true)
  } finally {
    clearTimeout(timer)
    if (id === requestId) {
      requestController = null
      pending.value = ''
    }
  }
}

// A resized/zoomed viewport changes the image-to-pointer scale. End the current
// drag; the next drag measures the new geometry instead of reusing a stale scale.
onMounted(() => window.addEventListener('resize', stopDrag))
onBeforeUnmount(() => {
  window.removeEventListener('resize', stopDrag)
  cancelRequest()
  clearTimeout(challengeTimer)
  clearTimeout(proofTimer)
})

defineExpose({ refresh, validate, getToken, isOpen: () => modalOpen.value })
</script>

<style scoped>
.login-captcha__entry.ant-btn { height: 38px; }
.login-captcha__entry--verified.ant-btn { color: #23784e; border-color: #91c5a4; background: #edf7ef; }
.login-captcha__content { width: 100%; max-width: 320px; margin: 0 auto; }
.login-captcha__instructions.ant-typography { margin-bottom: 12px; }
.login-captcha__puzzle {
  position: relative;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 100%;
  overflow: hidden;
  border-radius: 8px;
  background: #edf2ec;
  user-select: none;
}
.login-captcha__background { display: block; width: 100%; height: 100%; }
.login-captcha__piece { position: absolute; left: 0; display: block; pointer-events: none; transition: none; will-change: transform; }
.login-captcha__rail {
  position: relative;
  height: 44px;
  margin-top: 16px;
  overflow: hidden;
  border-radius: 7px;
  touch-action: none;
  user-select: none;
}
.login-captcha__progress.ant-progress {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  margin: 0;
  padding: 0;
  pointer-events: none;
}
.login-captcha__progress :deep(.ant-progress-inner) { border-radius: 7px; }
.login-captcha__progress :deep(.ant-progress-bg),
.login-captcha__progress :deep(.ant-progress-success-bg) { transition: none; }
.login-captcha__rail-label.ant-typography {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  color: #52665a;
  font-size: 12px;
  pointer-events: none;
}
.login-captcha__thumb.ant-btn {
  position: absolute;
  top: 0;
  left: 0;
  min-width: 0;
  height: 44px;
  margin: 0;
  padding: 0;
  border-radius: 7px;
  cursor: grab;
  touch-action: none;
  transition: none;
  will-change: transform;
}
.login-captcha__thumb.ant-btn:focus-visible { outline: 2px solid #24534a; outline-offset: -4px; }
.login-captcha__rail--dragging .login-captcha__thumb.ant-btn { cursor: grabbing; }
.login-captcha__keyboard-help.ant-typography { margin: 8px 0 12px; font-size: 12px; }
.login-captcha__actions { margin-top: 16px; }
@media (max-width: 400px) {
  .login-captcha__rail-label.ant-typography { padding-left: 28px; font-size: 11px; }
}
</style>
