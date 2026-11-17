import { useEffect, useRef, useState } from 'react'
import { ChevronLeft, Bell, Landmark, LayoutGrid, UsersRound, ReceiptText } from 'lucide-react'
import logo from './assets/logo-4money.png'
import { Provider } from './store'
import { UserProvider } from './userStore'
import { useUser } from './userContext'
import { ProfileIcon } from './views/ProfileIcon'
import { GroupsProvider } from './groupsStore'
import { ExpensesProvider } from './expensesStore'
import { AUTH_ROUTES, useApp, type Route } from './context'
import { cx } from './ui'
import Dashboard from './views/Dashboard'
import { AccountPage, AttemptPage, BankingPage, TransactionPage } from './views/Banking'
import { NotificationsPage } from './views/Notifications'
import Profile from './views/Profile'
import GroupsList from './views/groups/GroupsList'
import GroupForm from './views/groups/GroupForm'
import GroupDetail from './views/groups/GroupDetail'
import InvitePreview from './views/groups/InvitePreview'
import ExpensesList from './views/expenses/ExpensesList'
import ExpenseForm from './views/expenses/ExpenseForm'
import ExpenseDetail from './views/expenses/ExpenseDetail'
import AddExpenseFlow from './views/expenses/AddExpenseFlow'
import { ConfirmDeactivation, ConfirmEmail, ConfirmEmailChange, Forgot, Login, Register, ResendConfirmation, ResetPassword } from './views/Auth'

const NAV: { name: Route['name']; label: string; icon: typeof LayoutGrid }[] = [
  { name: 'dashboard', label: 'Pulpit', icon: LayoutGrid },
  { name: 'expenses', label: 'Wydatki', icon: ReceiptText },
  { name: 'groups', label: 'Grupy', icon: UsersRound },
  { name: 'banking', label: 'Konta bankowe', icon: Landmark },
  { name: 'notifications', label: 'Powiadomienia', icon: Bell },
]

function NavBtn({ n, active, collapsed, onClick }: { n: (typeof NAV)[number]; active: boolean; collapsed: boolean; onClick: () => void }) {
  const { unread } = useApp()
  const count = n.name === 'notifications' ? unread : 0
  const label = count > 0 ? `${n.label}, nieprzeczytane: ${count}` : n.label
  return (
    <button
      onClick={onClick}
      title={label}
      aria-label={label}
      aria-current={active ? 'page' : undefined}
      className={cx('relative flex w-full items-center rounded-xl py-2.5 text-sm font-semibold transition', collapsed ? 'justify-center px-0' : 'gap-3 px-3', active ? 'bg-mint-soft text-ink [&>svg]:text-ink' : 'text-ink2 hover:bg-wash hover:text-ink')}
    >
      <n.icon size={20} />
      {!collapsed && <span>{n.label}</span>}
      {count > 0 && <span aria-hidden="true" className={cx('num flex h-5 min-w-5 shrink-0 items-center justify-center rounded-full bg-ink px-1 text-xs font-bold text-white', collapsed ? 'absolute -right-1 -top-1' : 'ml-auto')}>{count}</span>}
    </button>
  )
}

function Sidebar({ collapsed, onToggle }: { collapsed: boolean; onToggle: () => void }) {
  const { route, navigate } = useApp()
  const { profile, profileImageUrl } = useUser()
  const active = ['account', 'transaction', 'connection', 'attempt'].includes(route.name) ? 'banking' : ['expense', 'expense-new', 'expense-edit'].includes(route.name) ? 'expenses' : ['group', 'group-new', 'group-edit', 'invite'].includes(route.name) ? 'groups' : route.name
  return (
    <aside className={cx('flex shrink-0 flex-col border-r border-line bg-white py-5 transition-[width] duration-200', collapsed ? 'w-[72px] px-3' : 'w-64 px-4')}>
      {collapsed ? (
        <button
          onClick={onToggle}
          aria-label="Rozwiń menu"
          title="Rozwiń menu"
          className="mb-7 flex h-11 w-full cursor-pointer items-center justify-center rounded-xl transition hover:bg-wash"
        >
          <img src={logo} alt="4Money" className="block aspect-square h-11 w-11 min-h-11 min-w-11 shrink-0 object-contain" />
        </button>
      ) : (
        <div className="mb-7 flex items-center justify-between gap-2 px-1">
          <div className="flex items-center gap-3">
            <img src={logo} alt="4Money" className="block aspect-square h-11 w-11 min-h-11 min-w-11 shrink-0 object-contain" />
            <span className="text-xl font-extrabold tracking-tight text-ink">4Money</span>
          </div>
          <button
            onClick={onToggle}
            aria-label="Zwiń menu"
            title="Zwiń menu"
            className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg text-ink2/70 transition hover:bg-wash hover:text-ink"
          >
            <ChevronLeft size={18} />
          </button>
        </div>
      )}

      <nav aria-label="Główna nawigacja" className="space-y-1">
        {NAV.map((n) => <NavBtn key={n.name} n={n} collapsed={collapsed} active={active === n.name} onClick={() => navigate({ name: n.name })} />)}
      </nav>

      <button
        onClick={() => navigate({ name: 'profile' })}
        aria-label="Otwórz konto"
        title="Konto"
        className={cx('mt-auto flex w-full items-center gap-3 rounded-2xl border-t border-line pt-4 text-left transition hover:bg-wash', collapsed ? 'justify-center' : 'px-2 pb-2', active === 'profile' && 'bg-mint-soft')}
      >
        <ProfileIcon name={profile.name} surname={profile.surname} url={profileImageUrl} size={36} />
        {!collapsed && <span className="min-w-0"><span className="block truncate text-sm font-semibold">{profile.name} {profile.surname}</span><span className="block truncate text-xs text-ink2">@{profile.login}</span></span>}
      </button>
    </aside>
  )
}

function Shell() {
  const { route, toast } = useApp()
  const { isSignedIn } = useUser()
  const main = useRef<HTMLElement>(null)
  const [collapsed, setCollapsed] = useState(() => window.matchMedia('(max-width: 63.999rem)').matches)
  useEffect(() => {
    const media = window.matchMedia('(max-width: 63.999rem)')
    const update = () => setCollapsed(media.matches)
    media.addEventListener('change', update)
    return () => media.removeEventListener('change', update)
  }, [])
  useEffect(() => { main.current?.scrollTo({ top: 0 }) }, [route.name, route.id, route.tab])

  let view
  switch (route.name) {
    case 'expenses': view = <ExpensesList />; break
    case 'expense-edit': view = <ExpenseForm id={route.id} />; break
    case 'expense': view = <ExpenseDetail id={route.id!} />; break
    case 'expense-new': view = <AddExpenseFlow />; break
    case 'groups': view = <GroupsList />; break
    case 'group': view = <GroupDetail id={route.id!} tab={route.tab} />; break
    case 'group-new': view = <GroupForm />; break
    case 'group-edit': view = <GroupForm id={route.id} />; break
    case 'invite': view = <InvitePreview token={route.id} />; break
    case 'banking': view = <BankingPage tab={route.tab} />; break
    case 'account': view = <AccountPage id={route.id!} />; break
    case 'transaction': view = <TransactionPage id={route.id!} />; break
    case 'connection': view = <BankingPage tab="connections" />; break
    case 'attempt': view = <AttemptPage id={route.id!} />; break
    case 'notifications': view = <NotificationsPage />; break
    case 'profile': view = <Profile />; break
    default: view = <Dashboard />
  }

  if (!isSignedIn && !AUTH_ROUTES.includes(route.name)) return <Login />

  if (AUTH_ROUTES.includes(route.name)) {
    const auth = { login: <Login />, register: <Register />, 'confirm-email': <ConfirmEmail />, resend: <ResendConfirmation />, forgot: <Forgot />, reset: <ResetPassword />, 'confirm-email-change': <ConfirmEmailChange />, 'confirm-deactivation': <ConfirmDeactivation /> } as const
    return <div key={route.name + (route.id ?? '')} className="h-full overflow-y-auto">{auth[route.name as keyof typeof auth]}</div>
  }

  return (
    <div className="flex h-full bg-white">
      <Sidebar collapsed={collapsed} onToggle={() => setCollapsed(!collapsed)} />
      <div className="flex min-w-0 flex-1 flex-col">
        <main ref={main} className="soft-scroll min-h-0 flex-1 overflow-y-auto px-4 py-5 sm:px-6 sm:py-6 lg:px-10">
          <div key={route.name + (route.id ?? '') + (route.tab ?? '')} className={cx('animate-rise mx-auto max-w-[1180px]', route.name === 'notifications' && 'h-full min-h-0')}>{view}</div>
        </main>
      </div>
      {toast && (
        <div role="status" className="animate-rise fixed bottom-6 left-1/2 z-[70] max-w-md -translate-x-1/2 rounded-2xl bg-ink px-5 py-3 text-sm font-medium text-white shadow-xl">{toast}</div>
      )}
    </div>
  )
}

export default function App() {
  return (
    <UserProvider>
      <Provider>
        <GroupsProvider>
          <ExpensesProvider>
            <Shell />
          </ExpensesProvider>
        </GroupsProvider>
      </Provider>
    </UserProvider>
  )
}
