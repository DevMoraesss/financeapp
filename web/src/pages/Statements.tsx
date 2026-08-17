import { useEffect, useState } from 'react'
import { Button, Card, EmptyState, ErrorState, Field, Loading, Modal, inputClass } from '../components/ui'
import { ApiError, endpoints, type Statement, type StatementStatus } from '../lib/api'
import { formatDate, formatMoney, formatMonth } from '../lib/format'
import { useApi } from '../lib/useApi'

const STATUS_LABEL: Record<StatementStatus, string> = {
  open: 'aberta',
  closed: 'fechada',
  paid: 'paga',
}

/**
 * Faturas do cartao. Tela que o prototipo do Lovable nao tinha: nasceu da decisao de modelar
 * cartao de credito com ciclo de fatura (SPEC D5 e secao 5.3).
 */
export function Statements() {
  const accounts = useApi(() => endpoints.accounts(), [])
  const [cardId, setCardId] = useState<string | null>(null)

  const cards =
    accounts.status === 'ready'
      ? accounts.data.accounts.filter((account) => account.type === 'credit_card' && !account.archived)
      : []

  useEffect(() => {
    if (!cardId && cards.length > 0) setCardId(cards[0].id)
  }, [cardId, cards])

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-3xl font-semibold text-ink">Faturas</h1>
        <p className="mt-1 text-sm text-ink-faint">Compras do cartao agrupadas pelo ciclo de fechamento</p>
      </div>

      {accounts.status === 'loading' && <Loading />}
      {accounts.status === 'error' && <ErrorState message={accounts.message} />}

      {accounts.status === 'ready' && cards.length === 0 && (
        <Card>
          <EmptyState
            title="Nenhum cartao de credito cadastrado"
            description="Cadastre uma conta do tipo cartao de credito em Configuracoes, informando o dia de fechamento e o de vencimento."
          />
        </Card>
      )}

      {cards.length > 0 && (
        <>
          {cards.length > 1 && (
            <div className="flex gap-2">
              {cards.map((card) => (
                <button
                  key={card.id}
                  onClick={() => setCardId(card.id)}
                  className={`rounded-xl border px-4 py-2 text-sm transition ${
                    cardId === card.id
                      ? 'border-brand bg-brand/10 text-brand'
                      : 'border-line text-ink-muted hover:text-ink'
                  }`}
                >
                  {card.name}
                </button>
              ))}
            </div>
          )}

          {cardId && <StatementList cardId={cardId} />}
        </>
      )}
    </div>
  )
}

function StatementList({ cardId }: { cardId: string }) {
  const list = useApi(() => endpoints.statements(cardId), [cardId])
  const [paying, setPaying] = useState<Statement | null>(null)

  return (
    <>
      {list.status === 'loading' && <Loading />}
      {list.status === 'error' && <ErrorState message={list.message} />}

      {list.status === 'ready' && (
        <div className="space-y-3">
          {list.data.statements.map((statement) => (
            <StatementCard
              key={statement.month}
              cardId={cardId}
              statement={statement}
              onPay={() => setPaying(statement)}
            />
          ))}
        </div>
      )}

      {paying && (
        <PayModal
          cardId={cardId}
          statement={paying}
          onClose={() => setPaying(null)}
          onPaid={() => list.reload()}
        />
      )}
    </>
  )
}

function StatementCard({
  cardId,
  statement,
  onPay,
}: {
  cardId: string
  statement: Statement
  onPay: () => void
}) {
  const [open, setOpen] = useState(false)
  const detail = useApi(
    () => (open ? endpoints.statement(cardId, statement.month) : Promise.resolve(null)),
    [open, cardId, statement.month],
  )

  const tone =
    statement.status === 'paid'
      ? 'bg-brand/15 text-brand'
      : statement.status === 'closed'
        ? 'bg-warning/15 text-warning'
        : 'bg-surface-hover text-ink-muted'

  return (
    <Card>
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <div className="flex items-center gap-2.5">
            <p className="font-medium text-ink">{formatMonth(statement.month)}</p>
            <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${tone}`}>
              {STATUS_LABEL[statement.status]}
            </span>
          </div>
          <p className="mt-1 text-xs text-ink-faint">
            fecha {formatDate(statement.closingDate)} - vence {formatDate(statement.dueDate)} - compras de{' '}
            {formatDate(statement.periodStart)} a {formatDate(statement.periodEnd)}
          </p>
        </div>

        <div className="flex items-center gap-3">
          <span className="tabular text-xl font-semibold text-ink">{formatMoney(statement.total)}</span>

          {statement.status === 'closed' && statement.total > 0 && (
            <Button onClick={onPay}>Pagar fatura</Button>
          )}

          <button
            onClick={() => setOpen(!open)}
            className="text-sm text-ink-muted transition hover:text-brand"
          >
            {open ? 'ocultar' : 'ver compras'}
          </button>
        </div>
      </div>

      {open && (
        <div className="mt-4 border-t border-line pt-4">
          {detail.status === 'loading' && <Loading />}
          {detail.status === 'error' && <ErrorState message={detail.message} />}
          {detail.status === 'ready' &&
            detail.data &&
            (detail.data.purchases.length === 0 ? (
              <p className="py-4 text-center text-sm text-ink-faint">Nenhuma compra nesta fatura.</p>
            ) : (
              <ul className="divide-y divide-line">
                {detail.data.purchases.map((purchase) => (
                  <li key={purchase.id} className="flex items-center justify-between gap-4 py-2.5">
                    <div className="min-w-0">
                      <p className="truncate text-sm text-ink">{purchase.description}</p>
                      <p className="text-xs text-ink-faint">
                        {purchase.category?.name} - {formatDate(purchase.date)}
                      </p>
                    </div>
                    <span className="tabular text-sm text-ink">{formatMoney(purchase.amount)}</span>
                  </li>
                ))}
              </ul>
            ))}
        </div>
      )}
    </Card>
  )
}

function PayModal({
  cardId,
  statement,
  onClose,
  onPaid,
}: {
  cardId: string
  statement: Statement
  onClose: () => void
  onPaid: () => void
}) {
  const accounts = useApi(() => endpoints.accounts(), [])
  const [sourceId, setSourceId] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const sources =
    accounts.status === 'ready'
      ? accounts.data.accounts.filter(
          (account) => account.type !== 'credit_card' && !account.archived,
        )
      : []

  useEffect(() => {
    if (!sourceId && sources.length > 0) setSourceId(sources[0].id)
  }, [sourceId, sources])

  async function pay(event: React.FormEvent) {
    event.preventDefault()
    setError(null)
    setBusy(true)

    try {
      await endpoints.payStatement(cardId, statement.month, sourceId)
      onPaid()
      onClose()
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.displayMessage : 'Nao foi possivel pagar.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      title={`Pagar a fatura de ${formatMonth(statement.month)}`}
      subtitle="Vira uma transferencia, nao uma despesa: o gasto ja contou na data da compra."
      onClose={onClose}
    >
      <form onSubmit={pay} className="space-y-3">
        <p className="rounded-xl border border-line bg-canvas px-3 py-2.5 text-sm text-ink-muted">
          Valor total:{' '}
          <span className="tabular font-medium text-ink">{formatMoney(statement.total)}</span>
        </p>

        <Field label="Pagar com qual conta">
          <select
            className={inputClass}
            value={sourceId}
            onChange={(event) => setSourceId(event.target.value)}
            required
          >
            {sources.map((account) => (
              <option key={account.id} value={account.id}>
                {account.name} ({formatMoney(account.currentBalance)})
              </option>
            ))}
          </select>
        </Field>

        {error && <ErrorState message={error} />}

        <div className="flex justify-end gap-2 pt-2">
          <Button type="button" variant="ghost" onClick={onClose}>
            Cancelar
          </Button>
          <Button type="submit" disabled={busy || !sourceId}>
            {busy ? 'Pagando...' : 'Confirmar pagamento'}
          </Button>
        </div>
      </form>
    </Modal>
  )
}
