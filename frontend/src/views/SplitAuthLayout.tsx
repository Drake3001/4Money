import { useLayoutEffect, useRef, type ReactNode } from 'react'
import { ChartNoAxesCombined, ReceiptText, UsersRound } from 'lucide-react'
import logo from '../assets/logo-4money.png'

const features = [
  { icon: ReceiptText, title: 'Skanowanie paragonów', text: 'Dodawaj wydatki ze zdjęcia.' },
  { icon: UsersRound, title: 'Podział wydatków', text: 'Dziel wydatki i monitoruj wzajemne zobowiązania.' },
  { icon: ChartNoAxesCombined, title: 'Raporty', text: 'Zobacz, na co wydajesz najwięcej.' },
]

export default function SplitAuthLayout({ children }: { children: ReactNode }) {
  const page = useRef<HTMLDivElement>(null)
  useLayoutEffect(() => {
    const fit = () => {
      const rootSize = parseFloat(getComputedStyle(document.documentElement).fontSize)
      // Both forms use the same design height and minimum readable scale.
      const scale = Math.min(1, Math.max(.8, (window.innerHeight - 1.5 * rootSize - 2) / (42.5 * rootSize)))
      page.current?.style.setProperty('--auth-scale', String(scale))
    }
    fit()
    window.addEventListener('resize', fit)
    return () => window.removeEventListener('resize', fit)
  }, [])
  return (
    <div ref={page} className="login-page grid min-h-dvh w-full bg-white lg:grid-cols-[1.2fr_1fr] xl:grid-cols-[1.4fr_1fr]">
      <section aria-label="4Money" className="login-brand relative isolate flex min-w-0 flex-col overflow-hidden bg-ink px-7 py-8 text-white sm:px-12 lg:px-10 xl:px-20">
        <div aria-hidden="true" className="pointer-events-none absolute inset-0 -z-10" style={{ backgroundImage: 'radial-gradient(ellipse at 95% 10%, rgba(34,217,162,.16), transparent 55%), radial-gradient(ellipse at 0% 100%, rgba(58,111,196,.2), transparent 55%)' }} />
        <div className="flex items-center gap-3.5">
          <img src={logo} alt="" className="h-14 w-14 object-contain lg:h-16 lg:w-16" />
          <span className="text-3xl font-extrabold tracking-tight">4Money</span>
        </div>

        <div className="login-hero relative my-12 max-w-xl lg:my-0 lg:flex lg:max-w-none lg:flex-1 lg:flex-col lg:justify-evenly lg:gap-8 lg:py-8">
          <div className="relative top-3 mb-6 inline-flex self-start lg:mb-0 items-center justify-center gap-2.5 rounded-full border border-white/15 bg-white/5 px-3.5 py-2 text-center text-xs font-semibold uppercase tracking-[0.16em] text-white/80">
            <span aria-hidden="true" className="h-1.5 w-1.5 shrink-0 rounded-full bg-mint" /><span className="leading-none">Finanse w jednym miejscu</span>
          </div>
          <h1 className="login-slogan font-display text-[clamp(2.5rem,4.6vw,4.75rem)] leading-[1.06] font-bold tracking-[-0.045em]">
            Przejmij kontrolę<br className="hidden sm:block" /> nad swoimi<br className="hidden sm:block" /> <span className="text-mint">wydatkami.</span>
          </h1>
          <div className="login-features mt-8 lg:mt-0 hidden gap-3 lg:grid lg:grid-cols-3">
            {features.map(({ icon: Icon, title, text }) => <div key={title} className="rounded-2xl border border-white/10 bg-white/[0.035] p-4">
              <Icon size={22} strokeWidth={1.6} className="mb-4 text-mint" />
              <p className="whitespace-nowrap text-sm font-semibold">{title}</p>
              <p className="mt-1.5 text-xs leading-relaxed text-white/50">{text}</p>
            </div>)}
          </div>
        </div>
      </section>

      <main className="login-form-panel flex min-h-0 min-w-0 items-center justify-center bg-[#fcfdfe] px-6 py-12 sm:px-12 lg:px-10 lg:py-8 xl:px-16">
        <div className="auth-form-stage w-full max-w-[400px]">{children}</div>
      </main>
    </div>
  )
}
