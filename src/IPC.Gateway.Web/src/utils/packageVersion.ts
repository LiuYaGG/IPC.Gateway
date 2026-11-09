// Assembly versions omit numeric padding present in date-based package versions.
export function isSamePackageVersion(packageVersion?: string, currentVersion?: string) {
  function normalize(value?: string) {
    const version = value?.trim() ?? ''
    if (!/^\d+(?:\.\d+){1,3}$/.test(version)) return version
    const parts = version.split('.').map(part => part.replace(/^0+(?=\d)/, ''))
    while (parts.length < 4) parts.push('0')
    return parts.join('.')
  }
  const installed = normalize(currentVersion)
  return installed !== '' && normalize(packageVersion) === installed
}
