type GoogleIdentity = {
  initialize: (config: { client_id: string; nonce: string; callback: (response: { credential: string }) => void; auto_select: boolean; ux_mode: 'popup' }) => void
  renderButton: (parent: HTMLElement, options: { type: 'standard'; theme: 'outline'; size: 'large'; text: 'continue_with' | 'signin_with' | 'signup_with'; shape: 'pill'; width: number; locale: 'pl' }) => void
}
declare global { interface Window { google?: { accounts: { id: GoogleIdentity } } } }

let loading: Promise<GoogleIdentity> | null = null

/** Official GIS SDK; ID tokens are sent only to the backend and are never persisted. */
export function loadGoogleIdentity(): Promise<GoogleIdentity> {
  if (window.google?.accounts.id) return Promise.resolve(window.google.accounts.id)
  loading ??= new Promise<GoogleIdentity>((resolve, reject) => {
    const script = document.createElement('script')
    const fail = () => { clearTimeout(timeout); script.remove(); reject(new Error('Google Identity Services unavailable')) }
    const timeout = setTimeout(fail, 15_000)
    script.src = 'https://accounts.google.com/gsi/client?hl=pl'
    script.async = true
    script.onload = () => {
      clearTimeout(timeout)
      if (window.google?.accounts.id) resolve(window.google.accounts.id)
      else fail()
    }
    script.onerror = fail
    document.head.append(script)
  }).catch((error) => { loading = null; throw error })
  return loading
}
