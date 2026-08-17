import type { ReactNode } from 'react'
import { AlertTriangle, Inbox, Loader2, X } from 'lucide-react'

/** Card padrao do app: a moldura escura com borda sutil do prototipo. */
export function Card({
  title,
  subtitle,
  action,
  children,
  className = '',
}: {
  title?: string
  subtitle?: string
  action?: ReactNode
  children: ReactNode
  className?: string
}) {
  return (
    <section className={`rounded-[--radius-card] border border-line bg-surface p-5 ${className}`}>
      {(title || action) && (
        <header className="mb-4 flex items-start justify-between gap-4">
          <div>
            {title && <h2 className="text-lg font-semibold text-ink">{title}</h2>}
            {subtitle && <p className="mt-0.5 text-sm text-ink-faint">{subtitle}</p>}
          </div>
          {action}
        </header>
      )}
      {children}
    </section>
  )
}

/** Card de numero grande do topo do dashboard (saldo, receitas, despesas). */
export function StatCard({
  label,
  value,
  hint,
  icon,
  tone = 'neutral',
}: {
  label: string
  value: string
  hint?: string
  icon: ReactNode
  tone?: 'neutral' | 'income' | 'expense'
}) {
  const valueTone = tone === 'income' ? 'text-income' : tone === 'expense' ? 'text-expense' : 'text-ink'

  return (
    <div className="rounded-[--radius-card] border border-line bg-surface p-5">
      <div className="flex items-center gap-2.5 text-sm text-ink-muted">
        <span className="grid size-8 place-items-center rounded-lg border border-line bg-surface-hover">
          {icon}
        </span>
        {label}
      </div>
      <p className={`tabular mt-3 text-3xl font-semibold ${valueTone}`}>{value}</p>
      {hint && <p className="mt-1 text-xs text-ink-faint">{hint}</p>}
    </div>
  )
}

/**
 * Barra de progresso do orcamento.
 * A cor vem do campo status que a API devolve: a UI nao decide faixa (fluxos-usuario secao 3),
 * para a regra de 80% e 100% morar num lugar so.
 */
export function ProgressBar({ percent, status }: { percent: number; status: 'normal' | 'warning' | 'over' }) {
  const color = status === 'over' ? 'bg-expense' : status === 'warning' ? 'bg-warning' : 'bg-brand'

  return (
    <div className="h-1.5 w-full overflow-hidden rounded-full bg-surface-hover">
      <div
        className={`h-full rounded-full transition-[width] ${color}`}
        style={{ width: `${Math.min(percent, 100)}%` }}
      />
    </div>
  )
}

export function Button({
  children,
  variant = 'primary',
  className = '',
  ...props
}: React.ButtonHTMLAttributes<HTMLButtonElement> & { variant?: 'primary' | 'ghost' | 'danger' }) {
  const styles =
    variant === 'primary'
      ? 'bg-brand text-brand-ink hover:brightness-110'
      : variant === 'danger'
        ? 'border border-expense/40 text-expense hover:bg-expense/10'
        : 'border border-line bg-surface text-ink hover:bg-surface-hover'

  return (
    <button
      {...props}
      className={`inline-flex items-center gap-2 rounded-xl px-4 py-2.5 text-sm font-medium transition disabled:cursor-not-allowed disabled:opacity-50 ${styles} ${className}`}
    >
      {children}
    </button>
  )
}

export function Loading({ label = 'Carregando...' }: { label?: string }) {
  return (
    <div className="flex items-center justify-center gap-2 py-12 text-sm text-ink-muted">
      <Loader2 className="size-4 animate-spin" />
      {label}
    </div>
  )
}

/** Estado vazio honesto: diz o que falta e o que fazer. */
export function EmptyState({
  title,
  description,
  action,
}: {
  title: string
  description: string
  action?: ReactNode
}) {
  return (
    <div className="flex flex-col items-center gap-3 py-14 text-center">
      <span className="grid size-11 place-items-center rounded-xl border border-line bg-surface-hover text-ink-faint">
        <Inbox className="size-5" />
      </span>
      <div>
        <p className="font-medium text-ink">{title}</p>
        <p className="mx-auto mt-1 max-w-md text-sm text-ink-faint">{description}</p>
      </div>
      {action}
    </div>
  )
}

export function ErrorState({ message }: { message: string }) {
  return (
    <div className="flex items-start gap-3 rounded-xl border border-expense/30 bg-expense/5 p-3 text-sm">
      <AlertTriangle className="mt-0.5 size-4 shrink-0 text-expense" />
      <p className="text-ink-muted">{message}</p>
    </div>
  )
}

/** Janela modal simples, sem dependencia externa. */
export function Modal({
  title,
  subtitle,
  onClose,
  children,
}: {
  title: string
  subtitle?: string
  onClose: () => void
  children: ReactNode
}) {
  return (
    <div
      className="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto bg-black/70 p-4 py-10"
      onClick={onClose}
    >
      <div
        className="w-full max-w-lg rounded-[--radius-card] border border-line bg-surface p-6"
        onClick={(event) => event.stopPropagation()}
      >
        <header className="mb-5 flex items-start justify-between gap-4">
          <div>
            <h2 className="text-lg font-semibold text-ink">{title}</h2>
            {subtitle && <p className="mt-0.5 text-sm text-ink-faint">{subtitle}</p>}
          </div>
          <button
            type="button"
            onClick={onClose}
            className="grid size-8 place-items-center rounded-lg text-ink-faint transition hover:bg-surface-hover hover:text-ink"
            aria-label="Fechar"
          >
            <X className="size-4" />
          </button>
        </header>
        {children}
      </div>
    </div>
  )
}

/** Campo de formulario padrao. */
export function Field({
  label,
  hint,
  children,
}: {
  label: string
  hint?: string
  children: ReactNode
}) {
  return (
    <label className="block">
      <span className="mb-1.5 block text-sm text-ink-muted">{label}</span>
      {children}
      {hint && <span className="mt-1 block text-xs text-ink-faint">{hint}</span>}
    </label>
  )
}

export const inputClass =
  'w-full rounded-xl border border-line bg-canvas px-3 py-2.5 text-sm text-ink outline-none transition focus:border-brand'
