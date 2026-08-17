import { NavLink, Outlet } from 'react-router-dom'
import {
  ArrowLeftRight,
  ChevronLeft,
  ChevronRight,
  CreditCard,
  LayoutGrid,
  LogOut,
  PiggyBank,
  Settings,
  Wallet,
} from 'lucide-react'
import { formatMonth, shiftMonth } from '../lib/format'
import { useMonth } from '../lib/MonthContext'
import { useAuth } from '../lib/AuthContext'

/**
 * Navegacao da v1. Investimentos e Metas ficaram de fora de proposito (SPEC secao 4): sao v2.
 * "Faturas" e tela nova, que o prototipo nao tinha: nasceu da decisao de modelar cartao de
 * credito com ciclo de fatura (SPEC D5).
 */
const NAV = [
  { to: '/', label: 'Dashboard', icon: LayoutGrid, end: true },
  { to: '/transacoes', label: 'Transacoes', icon: ArrowLeftRight },
  { to: '/faturas', label: 'Faturas', icon: CreditCard },
  { to: '/orcamento', label: 'Orcamento', icon: PiggyBank },
  { to: '/configuracoes', label: 'Configuracoes', icon: Settings },
]

export function AppLayout() {
  const { month, setMonth } = useMonth()
  const auth = useAuth()
  const user = auth.status === 'authenticated' ? auth.user : null

  return (
    <div className="flex min-h-full">
      <aside className="flex w-64 shrink-0 flex-col border-r border-line bg-sidebar">
        <div className="flex items-center gap-3 px-5 py-6">
          <span className="grid size-9 place-items-center rounded-full bg-brand text-brand-ink">
            <Wallet className="size-4.5" />
          </span>
          <div>
            <p className="font-semibold leading-tight text-ink">FinanceMove</p>
            <p className="text-xs text-ink-faint">Financas pessoais</p>
          </div>
        </div>

        <nav className="flex flex-col gap-1 px-3">
          {NAV.map(({ to, label, icon: Icon, end }) => (
            <NavLink
              key={to}
              to={to}
              end={end}
              className={({ isActive }) =>
                `flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm transition ${
                  isActive
                    ? 'bg-brand/10 font-medium text-brand'
                    : 'text-ink-muted hover:bg-surface-hover hover:text-ink'
                }`
              }
            >
              <Icon className="size-4" />
              {label}
            </NavLink>
          ))}
        </nav>

        <div className="mt-auto m-3 rounded-xl border border-line bg-surface p-3">
          <p className="truncate text-sm text-ink">{user?.name}</p>
          <p className="truncate text-xs text-ink-faint">{user?.email}</p>
          <button
            onClick={() => auth.logout()}
            className="mt-2.5 flex items-center gap-2 text-xs text-ink-muted transition hover:text-expense"
          >
            <LogOut className="size-3.5" />
            Sair
          </button>
        </div>
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex items-center justify-between border-b border-line px-8 py-4">
          <p className="text-sm text-ink-muted">Ola, {user?.name?.split(' ')[0]}</p>

          {/* Navegador de mes: filtro GLOBAL de Dashboard, Transacoes e Orcamento. */}
          <div className="flex items-center gap-1 rounded-full border border-line bg-surface px-1.5 py-1">
            <button
              onClick={() => setMonth(shiftMonth(month, -1))}
              className="grid size-7 place-items-center rounded-full text-ink-muted transition hover:bg-surface-hover hover:text-ink"
              aria-label="Mes anterior"
            >
              <ChevronLeft className="size-4" />
            </button>
            <span className="min-w-36 text-center text-sm font-medium text-ink">{formatMonth(month)}</span>
            <button
              onClick={() => setMonth(shiftMonth(month, 1))}
              className="grid size-7 place-items-center rounded-full text-ink-muted transition hover:bg-surface-hover hover:text-ink"
              aria-label="Proximo mes"
            >
              <ChevronRight className="size-4" />
            </button>
          </div>
        </header>

        <main className="min-w-0 flex-1 px-8 py-7">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
