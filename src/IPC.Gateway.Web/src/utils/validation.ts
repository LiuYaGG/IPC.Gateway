export type FormRules<T = unknown> = Partial<
  Record<
    keyof T | string,
    Array<{
      required?: boolean
      message?: string
      trigger?: string
      min?: number
      max?: number
      type?: string
      validator?: (rule: any, value: any, callback: (error?: Error) => void) => unknown
    }>
  >
>

// Ant Form uses name paths and promise validators for nested tag settings.
export function toAntRules(rules: FormRules, translate: (value: string) => string = (value) => value) {
  const result: Record<string, any> = {}
  for (const [path, values] of Object.entries(rules)) {
    const keys = path.split('.')
    let target = result
    for (const key of keys.slice(0, -1)) target = target[key] ??= {}
    target[keys.at(-1)!] = values?.map(({ trigger, validator, ...rule }) => ({
      ...rule,
      ...(rule.message ? { message: translate(rule.message) } : {}),
      validateTrigger: trigger,
      ...(validator
        ? {
            validator: (current: any, value: any) =>
              new Promise<void>((resolve, reject) => {
                const fail = (error: unknown) =>
                  reject(error instanceof Error ? new Error(translate(error.message)) : error)
                const pending = validator(current, value, (error) => (error ? fail(error) : resolve()))
                if (pending instanceof Promise) pending.then(() => resolve(), fail)
              })
          }
        : {})
    }))
  }
  return result
}
