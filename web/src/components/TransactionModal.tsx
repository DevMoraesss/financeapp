import { useMemo, useState, type FormEvent } from 'react'
import { ApiError, endpoints, type Account, type Category, type TransactionType } from '../lib/api'
import { Button, ErrorState, Field, inputClass, Modal } from './ui'
import { todayIso } from '../lib/format'

type Tab = TransactionType

/**
 * Modal de nova transacao: receita, despesa ou transferencia, com parcelamento quando a conta
 * escolhida for cartao de credito.
 *
 * O valor e digitado no formato brasileiro (150,00) e convertido para numero na hora de enviar.
 * Nenhuma outra conta e feita aqui: totais e saldos vem prontos da API (ADR-001, Decisao 3).
 */
export function TransactionModal({
  accounts,
  categories,
  onClose,
  onSaved,
}: {
  accounts: Account[]
  categories: Category[]
  onClose: () => void
  onSaved: () => void
}) {
  const usable = useMemo(() => accounts.filter((account) => !account.archived), [accounts])

  const [tab, setTab] = useState<Tab>('expense')
  const [amount, setAmount] = useState('')
  const [date, setDate] = useState(todayIso())
  const [description, setDescription] = useState('')
  const [accountId, setAccountId] = useState(usable[0]?.id ?? '')
  const [destinationId, setDestinationId] = useState(usable[1]?.id ?? '')
  const [categoryId, setCategoryId] = useState('')
  const [installments, setInstallments] = useState(1)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const selectedAccount = usable.find((account) => account.id === accountId)
  const isCard = selectedAccount?.type === 'credit_card'
  const availableCategories = categories.filter(
    (category) => !category.archived && category.type === (tab === 'income' ? 'income' : 'expense'),
  )

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setError(null)

    const parsed = parseAmount(amount)

    if (parsed === null || parsed <= 0) {
      setError('Informe um valor maior que zero.')
      return
    }

    setBusy(true)

    try {
      if (tab === 'expense' && isCard && installments > 1) {
        await endpoints.createInstallments({
          description,
          totalAmount: parsed,
          installments,
          date,
          cardId: accountId,
          categoryId,
        })
      } else {
        await endpoints.createTransaction({
          type: tab,
          amount: parsed,
          date,
          description,
          accountId,
          destinationAccountId: tab === 'transfer' ? destinationId : null,
          categoryId: tab === 'transfer' ? null : categoryId,
        })
      }

      onSaved()
      onClose()
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.displayMessage : 'Nao foi possivel salvar.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal title="Nova transacao" onClose={onClose}>
      <div className="mb-5 grid grid-cols-3 gap-1 rounded-xl border border-line bg-canvas p-1">
        {(
          [
            ['expense', 'Despesa'],
            ['income', 'Receita'],
            ['transfer', 'Transferencia'],
          ] as Array<[Tab, string]>
        ).map(([value, label]) => (
          <button
            key={value}
            type="button"
            onClick={() => {
              setTab(value)
              setCategoryId('')
              setInstallments(1)
            }}
            className={`rounded-lg px-3 py-2 text-sm transition ${
              tab === value ? 'bg-brand text-brand-ink font-medium' : 'text-ink-muted hover:text-ink'
            }`}
          >
            {label}
          </button>
        ))}
      </div>

      <form onSubmit={handleSubmit} className="space-y-3">
        <div className="grid grid-cols-2 gap-3">
          <Field label="Valor" hint="Use virgula para os centavos.">
            <input
              className={inputClass}
              inputMode="decimal"
              placeholder="150,00"
              value={amount}
              onChange={(event) => setAmount(event.target.value)}
              required
            />
          </Field>

          <Field label="Data">
            <input
              className={inputClass}
              type="date"
              value={date}
              onChange={(event) => setDate(event.target.value)}
              required
            />
          </Field>
        </div>

        <Field label="Descricao">
          <input
            className={inputClass}
            placeholder={tab === 'transfer' ? 'Guardando para a reserva' : 'Mercado Assai'}
            value={description}
            onChange={(event) => setDescription(event.target.value)}
            maxLength={120}
            required
          />
        </Field>

        <Field label={tab === 'transfer' ? 'Conta de origem' : 'Conta'}>
          <select
            className={inputClass}
            value={accountId}
            onChange={(event) => setAccountId(event.target.value)}
            required
          >
            {usable.map((account) => (
              <option key={account.id} value={account.id}>
                {account.name}
              </option>
            ))}
          </select>
        </Field>

        {tab === 'transfer' ? (
          <Field label="Conta de destino" hint="Transferencia nao conta como despesa nos relatorios.">
            <select
              className={inputClass}
              value={destinationId}
              onChange={(event) => setDestinationId(event.target.value)}
              required
            >
              {usable
                .filter((account) => account.id !== accountId)
                .map((account) => (
                  <option key={account.id} value={account.id}>
                    {account.name}
                  </option>
                ))}
            </select>
          </Field>
        ) : (
          <Field label="Categoria">
            <select
              className={inputClass}
              value={categoryId}
              onChange={(event) => setCategoryId(event.target.value)}
              required
            >
              <option value="">Escolha uma categoria</option>
              {availableCategories.map((category) => (
                <option key={category.id} value={category.id}>
                  {category.name}
                </option>
              ))}
            </select>
          </Field>
        )}

        {tab === 'expense' && isCard && (
          <Field
            label="Parcelas"
            hint={
              installments > 1
                ? `Cada parcela cai na fatura do mes correspondente. A soma fecha exatamente com o total.`
                : 'A vista. Escolha mais de uma parcela para dividir a compra.'
            }
          >
            <select
              className={inputClass}
              value={installments}
              onChange={(event) => setInstallments(Number(event.target.value))}
            >
              {Array.from({ length: 24 }, (_, index) => index + 1).map((count) => (
                <option key={count} value={count}>
                  {count === 1 ? 'A vista' : `${count}x`}
                </option>
              ))}
            </select>
          </Field>
        )}

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

/** "1.234,56" e "1234.56" viram 1234.56. Devolve null se nao der para entender. */
function parseAmount(input: string): number | null {
  const normalized = input.trim().replace(/\./g, '').replace(',', '.')
  const value = Number(normalized)
  return Number.isFinite(value) ? value : null
}
