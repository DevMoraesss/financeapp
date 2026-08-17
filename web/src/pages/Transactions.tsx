import { useState } from 'react'
import { Plus, Search, Trash2 } from 'lucide-react'
import { Button, Card, EmptyState, ErrorState, Loading, StatCard, inputClass } from '../components/ui'
import { TransactionModal } from '../components/TransactionModal'
import { endpoints } from '../lib/api'
import { formatDate, formatMoney, formatMonth, formatSigned } from '../lib/format'
import { useApi } from '../lib/useApi'
import { useMonth } from '../lib/MonthContext'

export function Transactions() {
  const { month } = useMonth()
  const [search, setSearch] = useState('')
  const [modalOpen, setModalOpen] = useState(false)

  const query = `?month=${month}${search.trim() ? `&search=${encodeURIComponent(search.trim())}` : ''}`
  const list = useApi(() => endpoints.transactions(query), [query])
  const accounts = useApi(() => endpoints.accounts(), [])
  const categories = useApi(() => endpoints.categories(), [])

  const canCreate = accounts.status === 'ready' && accounts.data.accounts.length > 0

  async function remove(id: string) {
    await endpoints.deleteTransaction(id)
    list.reload()
  }

  return (
    <div className="space-y-6">
      <div className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-3xl font-semibold text-ink">Transacoes</h1>
          <p className="mt-1 text-sm text-ink-faint">{formatMonth(month)}</p>
        </div>
        <Button
          onClick={() => setModalOpen(true)}
          disabled={!canCreate}
          title={canCreate ? undefined : 'Cadastre uma conta antes de lancar transacoes.'}
        >
          <Plus className="size-4" />
          Nova transacao
        </Button>
      </div>

      {list.status === 'loading' && <Loading />}
      {list.status === 'error' && <ErrorState message={list.message} />}

      {list.status === 'ready' && (
        <>
          <div className="grid gap-4 md:grid-cols-3">
            <StatCard
              label="Total de entradas"
              value={formatMoney(list.data.monthSummary.income)}
              tone="income"
              icon={<span className="text-income">+</span>}
            />
            <StatCard
              label="Total de saidas"
              value={formatMoney(list.data.monthSummary.expenses)}
              tone="expense"
              icon={<span className="text-expense">-</span>}
            />
            <StatCard
              label="Saldo do mes"
              value={formatMoney(list.data.monthSummary.balance)}
              icon={<span className="text-ink-muted">=</span>}
            />
          </div>

          <Card>
            <div className="mb-4 flex items-center gap-2 rounded-xl border border-line bg-canvas px-3 py-2">
              <Search className="size-4 text-ink-faint" />
              <input
                placeholder="Buscar descricao..."
                value={search}
                onChange={(event) => setSearch(event.target.value)}
                className="w-full bg-transparent text-sm text-ink outline-none placeholder:text-ink-faint"
              />
            </div>

            {list.data.items.length === 0 ? (
              <EmptyState
                title={`Nenhuma transacao em ${formatMonth(month)}`}
                description="Use o navegador de mes no topo para olhar outro periodo, ou lance uma nova transacao."
              />
            ) : (
              <ul className="divide-y divide-line">
                {list.data.items.map((transaction) => (
                  <li key={transaction.id} className="group flex items-center gap-4 py-3">
                    <div className="min-w-0 flex-1">
                      <p className="truncate text-sm text-ink">
                        {transaction.description}
                        {transaction.status === 'pending' && (
                          <span className="ml-2 rounded-full bg-warning/15 px-2 py-0.5 text-xs text-warning">
                            prevista
                          </span>
                        )}
                      </p>
                      <p className="text-xs text-ink-faint">
                        {transaction.category?.name ?? 'Transferencia'} - {transaction.account.name}
                        {transaction.destinationAccount && ` para ${transaction.destinationAccount.name}`}
                        {transaction.statementMonth && ` - fatura ${transaction.statementMonth}`}
                      </p>
                    </div>

                    <div className="text-right">
                      <p
                        className={`tabular text-sm font-medium ${
                          transaction.type === 'income'
                            ? 'text-income'
                            : transaction.type === 'transfer'
                              ? 'text-ink-muted'
                              : 'text-expense'
                        }`}
                      >
                        {transaction.type === 'transfer'
                          ? formatMoney(transaction.amount)
                          : formatSigned(transaction.amount, transaction.type)}
                      </p>
                      <p className="text-xs text-ink-faint">{formatDate(transaction.date)}</p>
                    </div>

                    <button
                      onClick={() => remove(transaction.id)}
                      className="grid size-8 shrink-0 place-items-center rounded-lg text-ink-faint opacity-0 transition hover:bg-expense/10 hover:text-expense group-hover:opacity-100"
                      title="Excluir"
                    >
                      <Trash2 className="size-4" />
                    </button>
                  </li>
                ))}
              </ul>
            )}

            {list.data.pagination.total > list.data.items.length && (
              <p className="mt-4 text-center text-xs text-ink-faint">
                Mostrando {list.data.items.length} de {list.data.pagination.total} lancamentos.
              </p>
            )}
          </Card>
        </>
      )}

      {modalOpen && accounts.status === 'ready' && categories.status === 'ready' && (
        <TransactionModal
          accounts={accounts.data.accounts}
          categories={categories.data.categories}
          onClose={() => setModalOpen(false)}
          onSaved={() => {
            list.reload()
            accounts.reload()
          }}
        />
      )}
    </div>
  )
}

export { inputClass }
