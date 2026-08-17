import { ArrowDownRight, ArrowUpRight, Check, Wallet, X } from 'lucide-react'
import { Cell, Pie, PieChart, ResponsiveContainer } from 'recharts'
import { Card, EmptyState, ErrorState, Loading, StatCard } from '../components/ui'
import { endpoints } from '../lib/api'
import { formatDate, formatMoney, formatPercent, formatSigned } from '../lib/format'
import { useApi } from '../lib/useApi'
import { useMonth } from '../lib/MonthContext'

export function Dashboard() {
  const { month } = useMonth()
  const state = useApi(() => endpoints.dashboard(month), [month])

  async function confirm(id: string) {
    await endpoints.confirmTransaction(id)
    state.reload()
  }

  async function discard(id: string) {
    await endpoints.discardTransaction(id)
    state.reload()
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-3xl font-semibold text-ink">Dashboard</h1>
        <p className="mt-1 text-sm text-ink-faint">Resumo das suas financas neste mes</p>
      </div>

      {state.status === 'loading' && <Loading />}
      {state.status === 'error' && <ErrorState message={state.message} />}

      {state.status === 'ready' && (
        <>
          <div className="grid gap-4 md:grid-cols-3">
            <StatCard
              label="Saldo total"
              value={formatMoney(state.data.totalBalance)}
              hint="Soma das suas contas"
              icon={<Wallet className="size-4 text-brand" />}
            />
            <StatCard
              label="Receitas do mes"
              value={formatMoney(state.data.month.income)}
              tone="income"
              icon={<ArrowUpRight className="size-4 text-income" />}
            />
            <StatCard
              label="Despesas do mes"
              value={formatMoney(state.data.month.expenses)}
              hint={`Saldo do mes: ${formatMoney(state.data.month.balance)}`}
              tone="expense"
              icon={<ArrowDownRight className="size-4 text-expense" />}
            />
          </div>

          <div className="grid gap-4 lg:grid-cols-2">
            <Card title="Despesas por categoria" subtitle="Mes atual">
              {state.data.expensesByCategory.length === 0 ? (
                <EmptyState
                  title="Nenhuma despesa neste mes"
                  description="Assim que voce lancar um gasto, ele aparece aqui separado por categoria."
                />
              ) : (
                <div className="flex flex-col items-center gap-6 sm:flex-row">
                  <ResponsiveContainer width={170} height={170}>
                    <PieChart>
                      <Pie
                        data={state.data.expensesByCategory}
                        dataKey="amount"
                        innerRadius={52}
                        outerRadius={82}
                        paddingAngle={2}
                        stroke="none"
                      >
                        {state.data.expensesByCategory.map((slice) => (
                          <Cell key={slice.categoryId} fill={slice.color} />
                        ))}
                      </Pie>
                    </PieChart>
                  </ResponsiveContainer>

                  <ul className="min-w-0 flex-1 space-y-2.5">
                    {state.data.expensesByCategory.map((slice) => (
                      <li key={slice.categoryId} className="flex items-center gap-3 text-sm">
                        <span
                          className="size-2.5 shrink-0 rounded-full"
                          style={{ backgroundColor: slice.color }}
                        />
                        <span className="min-w-0 flex-1 truncate text-ink-muted">{slice.category}</span>
                        <span className="tabular text-xs text-ink-faint">{formatPercent(slice.percent)}</span>
                        <span className="tabular font-medium text-ink">{formatMoney(slice.amount)}</span>
                      </li>
                    ))}
                  </ul>
                </div>
              )}
            </Card>

            <Card title="A confirmar" subtitle="Lancamentos recorrentes que venceram">
              {state.data.pending.length === 0 ? (
                <EmptyState
                  title="Nada pendente"
                  description="Contas recorrentes aparecem aqui no dia do vencimento, para voce confirmar com um clique."
                />
              ) : (
                <ul className="space-y-2">
                  {state.data.pending.map((item) => (
                    <li
                      key={item.id}
                      className="flex items-center justify-between gap-3 rounded-xl border border-line px-3 py-2.5 text-sm"
                    >
                      <div className="min-w-0">
                        <p className="truncate text-ink">{item.description}</p>
                        <p className="text-xs text-ink-faint">
                          {formatDate(item.date)} - {item.account.name}
                        </p>
                      </div>

                      <div className="flex shrink-0 items-center gap-2">
                        <span className="tabular font-medium text-ink">{formatMoney(item.amount)}</span>
                        <button
                          onClick={() => confirm(item.id)}
                          className="grid size-7 place-items-center rounded-lg border border-line text-brand transition hover:bg-brand/10"
                          title="Confirmar"
                        >
                          <Check className="size-3.5" />
                        </button>
                        <button
                          onClick={() => discard(item.id)}
                          className="grid size-7 place-items-center rounded-lg border border-line text-ink-faint transition hover:text-expense"
                          title="Descartar"
                        >
                          <X className="size-3.5" />
                        </button>
                      </div>
                    </li>
                  ))}
                </ul>
              )}
            </Card>
          </div>

          <Card title="Ultimas transacoes">
            {state.data.recentTransactions.length === 0 ? (
              <EmptyState
                title="Nenhuma transacao ainda"
                description="Lance sua primeira receita ou despesa na aba Transacoes."
              />
            ) : (
              <ul className="divide-y divide-line">
                {state.data.recentTransactions.map((transaction) => (
                  <li key={transaction.id} className="flex items-center gap-4 py-3">
                    <span
                      className="size-9 shrink-0 rounded-xl"
                      style={{ backgroundColor: `${transaction.category?.color ?? '#5b666d'}22` }}
                    />
                    <div className="min-w-0 flex-1">
                      <p className="truncate text-sm text-ink">{transaction.description}</p>
                      <p className="text-xs text-ink-faint">
                        {transaction.category?.name ?? 'Transferencia'} - {transaction.account.name} -{' '}
                        {formatDate(transaction.date)}
                      </p>
                    </div>
                    <span
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
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </Card>
        </>
      )}
    </div>
  )
}
