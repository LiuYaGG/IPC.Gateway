export function requireSigningCrypto() {
  if (!globalThis.crypto?.subtle) throw new Error('驱动签名需要 HTTPS 或 localhost、127.0.0.1 地址。')
  return globalThis.crypto.subtle
}

function bytesToBase64(bytes: Uint8Array) {
  return btoa(Array.from(bytes, b => String.fromCharCode(b)).join(''))
}

function pem(label: string, bytes: ArrayBuffer) {
  return `-----BEGIN ${label}-----\n${bytesToBase64(new Uint8Array(bytes)).match(/.{1,64}/g)?.join('\n')}\n-----END ${label}-----`
}

export async function createSigningKey() {
  const crypto = requireSigningCrypto()
  const keys = await crypto.generateKey({ name: 'RSASSA-PKCS1-v1_5', modulusLength: 3072, publicExponent: new Uint8Array([1, 0, 1]), hash: 'SHA-256' }, true, ['sign', 'verify'])
  return pem('PRIVATE KEY', await crypto.exportKey('pkcs8', keys.privateKey))
}

export async function signDriverPayload(privatePem: string, payloadBase64: string) {
  const crypto = requireSigningCrypto()
  if (!privatePem.includes('-----BEGIN PRIVATE KEY-----')) throw new Error('请选择 PKCS#8 PEM 格式的 RSA 私钥文件。')
  const raw = Uint8Array.from(atob(privatePem.replace(/-----[^-]+-----/g, '').replace(/\s/g, '')), c => c.charCodeAt(0))
  const key = await crypto.importKey('pkcs8', raw, { name: 'RSASSA-PKCS1-v1_5', hash: 'SHA-256' }, true, ['sign'])
  const jwk = await crypto.exportKey('jwk', key)
  const publicKey = await crypto.importKey('jwk', { kty: jwk.kty, n: jwk.n, e: jwk.e, alg: jwk.alg, ext: true }, { name: 'RSASSA-PKCS1-v1_5', hash: 'SHA-256' }, true, ['verify'])
  const payload = Uint8Array.from(atob(payloadBase64), c => c.charCodeAt(0))
  return {
    publicKeyPem: pem('PUBLIC KEY', await crypto.exportKey('spki', publicKey)),
    signature: bytesToBase64(new Uint8Array(await crypto.sign('RSASSA-PKCS1-v1_5', key, payload))),
  }
}
