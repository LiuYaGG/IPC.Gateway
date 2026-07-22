export type RockwellLogixProfileSettings = {
  routeMode: unknown
  maxRequestBytes: unknown
  maxServicesPerPacket: unknown
}

export function applyRockwellControllerProfile(
  options: Record<string, unknown>,
  value: unknown,
  rememberedLogixSettings?: RockwellLogixProfileSettings
) {
  const previousProfile = String(options.controllerProfile || 'Logix').toLowerCase()
  const nextProfile = String(value).toLowerCase()
  let remembered = rememberedLogixSettings

  if (previousProfile === 'logix' && nextProfile !== 'logix') {
    remembered = {
      routeMode: options.cipRouteMode || 'Slot',
      maxRequestBytes: options.cipMaxRequestBytes || 400,
      maxServicesPerPacket: options.cipMaxServicesPerPacket || 16
    }
  }

  options.controllerProfile = value
  if (nextProfile === 'logix') {
    const settings = remembered ?? {
      routeMode: 'Slot',
      maxRequestBytes: 400,
      maxServicesPerPacket: 16
    }
    options.cipRouteMode = settings.routeMode
    options.cipMaxRequestBytes = settings.maxRequestBytes
    options.cipMaxServicesPerPacket = settings.maxServicesPerPacket
  } else if (nextProfile === 'micro800') {
    if (!options.cipRouteMode || String(options.cipRouteMode).toLowerCase() === 'slot') options.cipRouteMode = 'Direct'
    if (!options.cipMaxRequestBytes || Number(options.cipMaxRequestBytes) === 400) options.cipMaxRequestBytes = 240
    if (!options.cipMaxServicesPerPacket || Number(options.cipMaxServicesPerPacket) === 16) options.cipMaxServicesPerPacket = 1
  } else if (nextProfile === 'generic') {
    if (!options.cipRouteMode || String(options.cipRouteMode).toLowerCase() === 'slot') options.cipRouteMode = 'Direct'
    if (!options.cipMaxRequestBytes || Number(options.cipMaxRequestBytes) === 240) options.cipMaxRequestBytes = 400
    if (!options.cipMaxServicesPerPacket || Number(options.cipMaxServicesPerPacket) === 1) options.cipMaxServicesPerPacket = 16
  }

  return remembered
}
