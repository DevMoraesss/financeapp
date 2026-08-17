import { useState } from 'react'
import { Plus, Trash2 } from 'lucide-react'
import {
  Button,
  Card,
  EmptyState,
  ErrorState,
  Field,
  Loading,
  Modal,
  ProgressBar,
  inputClass,
} from '../components/ui'
import { ApiError, endpoints } from '../lib/api'
import { formatMoney, formatMonth, formatPercent } from '../lib/format'
import { useApi } from '../lib/useApi'
import { useMonth } from '../lib/MonthContext'

export function Budget() {
  const { month } = useMonth()
  const overview = useApi(() => endpoints.budgets(month), [month])
  const categories = useApi(() => endpoints.categories('expense'), [])
  const [modalOpen, setModalOpen] = useState(false)

  async function remove(categoryId: string) {
    await endpoints.removeBudget(categoryId)
    overview.reload()
  }

  return (
    <div className="space-y-6">
      <div className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-3xl font-semibold text-ink">Orcamento</h1>
          <p className="mt-1 text-sm text-ink-faint">{formatMonth(month)}</p>
        </div>
        <Button onClick={() => setModalOpen(true)} disabled={categories.status !== 'ready'}>
          <Plus className="size-4" />
          Definir limite
        </Button>
      </div>

      {overview.status === 'loading' && <Loading />}
      {overview.status === 'error' && <ErrorState message={overview.message} />}

      {overview.status === 'ready' && (
        <>
          <Card>
            <div className="flex flex-wrap items-end justify-between gap-6">
              <div>
                <p className="text-sm text-ink-muted">Orcamento do mes</p>
                <p className="tabular mt-1 text-3xl font-semibold text-ink">
                  {formatMoney(overview.data.summary.totalSpent)}
                  <span className="ml-2 text-base font-normal text-ink-faint">
                    de {formatMoney(overview.data.summary.totalLimit)}
                  </span>
                </p>
              </div>

              <div className="flex gap-3">
                <div className="rounded-xl border border-line px-4 py-2.5 text-center">
                  <p className="text-xs text-ink-faint">No limite</p>
                  <p className="text-xl font-semibold text-warning">
                    {overview.data.summary.categoriesNearLimit}
                  </p>
                </div>
                <div className="rounded-xl border border-line px-4 py-2.5 text-center">
                  <p className="text-xs text-ink-faint">Estouradas</p>
                  <p className="text-xl font-semibold text-expense">
                    {overview.data.summary.categoriesOverLimit}
                  </p>
                </div>
              </div>
            </div>
          </Card>

          {overview.data.items.length === 0 ? (
            <Card>
              <EmptyState
                title="Nenhum limite definido"
                description="Defina um limite mensal por categoria para acompanhar quanto ainda pode gastar. O app informa, nao bloqueia."
              />
            </Card>
          ) : (
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
              {overview.data.items.map((item) => (
                <div
                  key={item.categoryId}
                  className="group rounded-[--radius-card] border border-line bg-surface p-5"
                >
                  <div className="mb-3 flex items-center justify-between gap-3">
                    <div className="flex min-w-0 items-center gap-2.5">
                      <span
                        className="size-2.5 shrink-0 rounded-full"
                        style={{ backgroundColor: item.color }}
                      />
                      <p className="truncate font-medium text-ink">{item.category}</p>
                    </div>

                    <div className="flex shrink-0 items-center gap-2">
                      <span
                        className={`tabular rounded-full px-2 py-0.5 text-xs font-medium ${
                          item.status === 'over'
                            ? 'bg-expense/15 text-expense'
                            : item.status === 'warning'
                              ? 'bg-warning/15 text-warning'
                              : 'bg-brand/15 text-brand'
                        }`}
                      >
                        {formatPercent(item.percent, 0)}
                      </span>
                      <button
                        onClick={() => remove(item.categoryId)}
                        className="text-ink-faint opacity-0 transition hover:text-expense group-hover:opacity-100"
                        title="Remover limite"
                      >
                        <Trash2 className="size-3.5" />
                      </button>
                    </div>
                  </div>

                  <p className="tabular mb-2.5 text-xl font-semibold text-ink">
                    {formatMoney(item.spent)}
                    <span className="ml-1.5 text-sm font-normal text-ink-faint">
                      / {formatMoney(item.limit)}
                    </span>
                  </p>

                  <ProgressBar percent={item.percent} status={item.status} />

                  <p className="mt-2 text-xs text-ink-faint">
                    {item.difference >= 0
                      ? `${formatMoney(item.difference)} restantes`
                      : `${formatMoney(Math.abs(item.difference))} acima do limite`}
                  </p>
                </div>
              ))}
            </div>
          )}
        </>
      )}

      {modalOpen && categories.status === 'ready' && (
        <SetLimitModal
          categories={categories.data.categories.filter((category) => !category.archived)}
          onClose={() => setModalOpen(false)}
          onSaved={() => overview.reload()}
        />
      )}
    </div>
  )
}

function SetLimitModal({
  categories,
  onClose,
  onSaved,
}: {
  categories: Array<{ id: string; name: string }>
  onClose: () => void
  onSaved: () => void
}) {
  const [categoryId, setCategoryId] = useState(categories[0]?.id ?? '')
  const [limit, setLimit] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function save(event: React.FormEvent) {
    event.preventDefault()
    setError(null)

    const value = Number(limit.trim().replace(/\./g, '').replace(',', '.'))

    if (!Number.isFinite(value) || value <= 0) {
      setError('Informe um limite maior que zero.')
      return
    }

    setBusy(true)

    try {
      await endpoints.setBudget(categoryId, value)
      onSaved()
      onClose()
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.displayMessage : 'Nao foi possivel salvar.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      title="Limite mensal"
      subtitle="Vale para todos os meses. So despesas confirmadas contam."
      onClose={onClose}
    >
      <form onSubmit={save} className="space-y-3">
        <Field label="Categoria">
          <select
            className={inputClass}
            value={categoryId}
            onChange={(event) => setCategoryId(event.target.value)}
            required
          >
            {categories.map((category) => (
              <option key={category.id} value={category.id}>
                {category.name}
              </option>
            ))}
          </select>
        </Field>

        <Field label="Limite" hint="Use virgula para os centavos.">
          <input
            className={inputClass}
            inputMode="decimal"
            placeholder="600,00"
            value={limit}
            onChange={(event) => setLimit(event.target.value)}
            required
          />
        </Field>

        {error && <ErrorState message={error} />}

        <div className="flex justify-end gap-2 pt-2">
          <Button type="button" variant="ghost" onClick={onClose}>
            Cancelar
          </Button>
          <Button type="submit" disabled={busy}>
            {busy ? 'Salvando...' : 'Salvar'}
          </Button>
        </div>
      </form>
    </Modal>
  )
}
