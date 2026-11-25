// A protected request can pause for approval while its operation dialog or a
// blocking progress overlay (100000/100001) remains open. Keep both approval
// steps above those callers, with the nested puzzle above the password form.
export const securityModalLayers = {
  password: 100010,
  captcha: 100020
} as const
