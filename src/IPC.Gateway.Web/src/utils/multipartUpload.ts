export interface FileUploadProgress {
  loaded: number
  total: number
  lengthComputable: boolean
}

export function sendMultipartFile(url: string, file: File, onProgress?: (progress: FileUploadProgress) => void, onTransferred?: () => void) {
  return new Promise<{ status: number; responseText: string }>((resolve, reject) => {
    const xhr = new XMLHttpRequest()
    xhr.open('POST', url, true)
    xhr.withCredentials = true
    xhr.timeout = 30 * 60 * 1000
    xhr.upload.onprogress = event => onProgress?.({ loaded: event.loaded, total: event.total, lengthComputable: event.lengthComputable })
    xhr.upload.onload = () => onTransferred?.()
    xhr.onload = () => resolve({ status: xhr.status, responseText: xhr.responseText })
    xhr.onerror = () => reject(new Error('升级包上传失败，请检查网络或后端服务。'))
    xhr.ontimeout = () => reject(new Error('升级包上传超时，请稍后重试。'))
    xhr.onabort = () => reject(new Error('升级包上传已中断。'))
    const form = new FormData()
    form.append('file', file)
    xhr.send(form)
  })
}
