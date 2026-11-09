import { createVNode, render, type Directive } from 'vue'
import { Modal, Spin, message } from 'ant-design-vue'
import type { FormInstance as AntFormInstance } from 'ant-design-vue'
import { tx } from '../i18n'

export const notify: typeof message = new Proxy(message, {
  get(target, property) {
    const value = Reflect.get(target, property)
    if (
      typeof value === 'function' &&
      ['success', 'error', 'warning', 'info', 'loading', 'open'].includes(String(property))
    ) {
      return (content: any, ...args: any[]) =>
        Reflect.apply(value, target, [
          typeof content === 'string'
            ? tx(content)
            : content && typeof content.content === 'string'
              ? { ...content, content: tx(content.content) }
              : content,
          ...args
        ])
    }
    return value
  }
})

export type FormInstance = AntFormInstance
export type { FormRules } from './validation'
export { toAntRules } from './validation'
export interface FileSelection {
  raw?: File
  name: string
}

export function tagColor(type?: string) {
  return (
    ({ danger: 'error', info: 'default', primary: 'processing' } as Record<string, string>)[type ?? ''] ??
    type ??
    'default'
  )
}

export function confirmAction(
  content: string,
  title: string,
  options: { type?: string; confirmButtonText?: string; cancelButtonText?: string } = {}
) {
  return new Promise<boolean>((resolve, reject) =>
    Modal.confirm({
      centered: true,
      title: tx(title),
      content: tx(content),
      okText: tx(options.confirmButtonText ?? '确定'),
      cancelText: tx(options.cancelButtonText ?? '取消'),
      okButtonProps: { danger: options.type === 'warning' && /删除/.test(title) },
      onOk: () => {
        resolve(true)
      },
      onCancel: () => {
        reject('cancel')
      }
    })
  )
}

export function showBusy(options: { text: string; [key: string]: unknown }) {
  const modal = Modal.info({
    centered: true,
    title: tx(options.text),
    content: createVNode(Spin, { spinning: true }),
    icon: null,
    closable: false,
    maskClosable: false,
    keyboard: false,
    footer: null
  })
  return { close: () => modal.destroy() }
}

const busyElements = new WeakMap<HTMLElement, HTMLElement>()
function updateBusy(element: HTMLElement, value: boolean) {
  let overlay = busyElements.get(element)
  if (value && !overlay) {
    overlay = document.createElement('div')
    overlay.className = 'gateway-busy-overlay'
    if (getComputedStyle(element).position === 'static') element.style.position = 'relative'
    element.append(overlay)
    render(createVNode(Spin, { spinning: true }), overlay)
    busyElements.set(element, overlay)
  } else if (!value && overlay) {
    render(null, overlay)
    overlay.remove()
    busyElements.delete(element)
  }
}
export const busyDirective: Directive<HTMLElement, boolean> = {
  mounted: (element, binding) => updateBusy(element, binding.value),
  updated: (element, binding) => updateBusy(element, binding.value),
  beforeUnmount: (element) => updateBusy(element, false)
}
