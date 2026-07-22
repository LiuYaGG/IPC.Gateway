import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { Buffer } from 'node:buffer'
import ts from 'typescript'

const source = await readFile(new URL('../src/utils/rockwellProfile.ts', import.meta.url), 'utf8')
const compiled = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 }
}).outputText
const moduleUrl = `data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`
const { applyRockwellControllerProfile } = await import(moduleUrl)

const defaults = { controllerProfile: 'Logix', cipRouteMode: 'Slot', cipMaxRequestBytes: 400, cipMaxServicesPerPacket: 16 }
let remembered = applyRockwellControllerProfile(defaults, 'Micro800')
assert.equal(defaults.cipRouteMode, 'Direct')
assert.equal(defaults.cipMaxRequestBytes, 240)
assert.equal(defaults.cipMaxServicesPerPacket, 1)

remembered = applyRockwellControllerProfile(defaults, 'Logix', remembered)
assert.equal(defaults.cipRouteMode, 'Slot')
assert.equal(defaults.cipMaxRequestBytes, 400)
assert.equal(defaults.cipMaxServicesPerPacket, 16)

const generic = { controllerProfile: 'Logix', cipRouteMode: 'Slot' }
const genericRemembered = applyRockwellControllerProfile(generic, 'Generic')
applyRockwellControllerProfile(generic, 'Logix', genericRemembered)
assert.equal(generic.cipRouteMode, 'Slot')

const direct = { controllerProfile: 'Logix', cipRouteMode: 'Direct', cipMaxRequestBytes: 320, cipMaxServicesPerPacket: 8 }
const directRemembered = applyRockwellControllerProfile(direct, 'Micro800')
applyRockwellControllerProfile(direct, 'Logix', directRemembered)
assert.deepEqual(direct, {
  controllerProfile: 'Logix',
  cipRouteMode: 'Direct',
  cipMaxRequestBytes: 320,
  cipMaxServicesPerPacket: 8
})

console.log('PASS Rockwell controller profile visibility state restoration')
