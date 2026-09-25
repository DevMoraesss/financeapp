import { useState } from 'react'
import { Archive, Plus, Scale } from 'lucide-react'
import {
  Button,
  Card,
  EmptyState,
  ErrorState,
  Field,
  Loading,
  Modal,
  inputClass,
} from '../components/ui'
import { ApiError, endpoints, type Account, type AccountType } from '../lib/api'
import { accountTypeLabel, formatMoney } from '../lib/format'
import { useApi } from '../lib/useApi'
import { useAuth } from '../lib/AuthContext'

export function Settings() {
  const accounts = useApi(() => endpoints.accounts(), [])
  const categories = useApi(() => endpoints.categories(), [])
  const [newAccount, setNewAccount] = useState(false)
  const [adjusting, setAdjusting] = useState<Account | null>(null)

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-3xl font-semibold text-ink">Configuracoes</h1>
        <p className="mt-1 text-sm text-ink-faint">Contas, categorias e seus dados</p>
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card
          title="Minhas contas"
          action={
            <Button variant="ghost" onClick={() => setNewAccount(true)}>
              <Plus className="size-4" />
              Nova conta
            </Button>
          }
        >
          {accounts.status === 'loading' && <Loading />}
          {accounts.status === 'error' && <ErrorState message={accounts.message} />}
          {accounts.status === 'ready' &&
            (accounts.data.accounts.length === 0 ? (
              <EmptyState
                title="Nenhuma conta cadastrada"
                description="Cadastre sua conta principal para comecar a lancar transacoes."
              />
            ) : (
              <ul className="space-y-2">
                {accounts.data.accounts.map((account) => (
                  <li
                    key={account.id}
                    className="group flex items-center justify-between gap-3 rounded-xl border border-line px-4 py-3"
                  >
                    <div className="min-w-0">
                      <p className="truncate text-sm font-medium text-ink">{account.name}</p>
                      <p className="text-xs text-ink-faint">
                        {accountTypeLabel(account.type)}
                        {account.type === 'credit_card' &&
                          account.closingDay &&
                          account.dueDay &&
                          ` - fecha dia ${account.closingDay}, vence dia ${account.dueDay}`}
                      </p>
                    </div>

                    <div className="flex shrink-0 items-center gap-2">
                      <span
                        className={`tabular text-sm font-medium ${
                          account.currentBalance < 0 ? 'text-expense' : 'text-ink'
                        }`}
                      >
                        {formatMoney(account.currentBalance)}
                      </span>

                      <button
                        onClick={() => setAdjusting(account)}
                        className="grid size-7 place-items-center rounded-lg text-ink-faint opacity-0 transition hover:text-brand group-hover:opacity-100"
                        title="Ajustar saldo"
                      >
                        <Scale className="size-3.5" />
                      </button>

                      <button
                        onClick={async () => {
                          await endpoints.archiveAccount(account.id)
                          accounts.reload()
                        }}
                        className="grid size-7 place-items-center rounded-lg text-ink-faint opacity-0 transition hover:text-expense group-hover:opacity-100"
                        title="Arquivar"
                      >
                        <Archive className="size-3.5" />
                      </button>
                    </div>
                  </li>
                ))}
              </ul>
            ))}
        </Card>

        <Card title="Categorias" subtitle="Criadas automaticamente no seu cadastro">
          {categories.status === 'loading' && <Loading />}
          {categories.status === 'error' && <ErrorState message={categories.message} />}
          {categories.status === 'ready' && (
            <div className="flex flex-wrap gap-2">
              {categories.data.categories.map((category) => (
                <span
                  key={`${category.id}`}
                  className="rounded-full border px-3 py-1.5 text-sm text-ink-muted"
                  style={{ borderColor: `${category.color}55` }}
                  title={category.type === 'income' ? 'Receita' : 'Despesa'}
                >
                  {category.name}
                  {category.system && <span className="ml-1.5 text-xs text-ink-faint">sistema</span>}
                </span>
              ))}
            </div>
          )}
        </Card>
      </div>

      <DataSection />

      {newAccount && (
        <NewAccountModal
          onClose={() => setNewAccount(false)}
          onSaved={() => accounts.reload()}
        />
      )}

      {adjusting && (
        <AdjustBalanceModal
          account={adjusting}
          onClose={() => setAdjusting(null)}
          onSaved={() => accounts.reload()}
        />
      )}
    </div>
  )
}

function NewAccountModal({ onClose, onSaved }: { onClose: () => void; onSaved: () => void }) {
  const [name, setName] = useState('')
  const [type, setType] = useState<AccountType>('checking')
  const [initialBalance, setInitialBalance] = useState('0,00')
  const [closingDay, setClosingDay] = useState(25)
  const [dueDay, setDueDay] = useState(2)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const isCard = type === 'credit_card'

  async function save(event: React.FormEvent) {
    event.preventDefault()
    setError(null)
    setBusy(true)

    try {
      await endpoints.createAccount({
        name,
        type,
        initialBalance: Number(initialBalance.trim().replace(/\./g, '').replace(',', '.')) || 0,
        closingDay: isCard ? closingDay : null,
        dueDay: isCard ? dueDay : null,
      })

      onSaved()
      onClose()
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.displayMessage : 'Nao foi possivel salvar.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal title="Nova conta" onClose={onClose}>
      <form onSubmit={save} className="space-y-3">
        <Field label="Nome">
          <input
            className={inputClass}
            placeholder="Nubank"
            value={name}
            onChange={(event) => setName(event.target.value)}
            maxLength={60}
            required
          />
        </Field>

        <Field label="Tipo">
          <select
            className={inputClass}
            value={type}
            onChange={(event) => setType(event.target.value as AccountType)}
          >
            <option value="checking">Conta corrente</option>
            <option value="savings">Poupanca</option>
            <option value="cash">Dinheiro</option>
            <option value="credit_card">Cartao de credito</option>
          </select>
        </Field>

        <Field
          label="Saldo inicial"
          hint="Quanto voce tem nesta conta hoje. O saldo atual e sempre calculado a partir daqui."
        >
          <input
            className={inputClass}
            inputMode="decimal"
            value={initialBalance}
            onChange={(event) => setInitialBalance(event.target.value)}
          />
        </Field>

        {isCard && (
          <div className="grid grid-cols-2 gap-3">
            <Field label="Dia do fechamento" hint="1 a 28.">
              <input
                className={inputClass}
                type="number"
                min={1}
                max={28}
                value={closingDay}
                onChange={(event) => setClosingDay(Number(event.target.value))}
                required
              />
            </Field>
            <Field label="Dia do vencimento" hint="Se for menor que o fechamento, vence no mes seguinte.">
              <input
                className={inputClass}
                type="number"
                min={1}
                max={28}
                value={dueDay}
                onChange={(event) => setDueDay(Number(event.target.value))}
                required
              />
            </Field>
          </div>
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

function AdjustBalanceModal({
  account,
  onClose,
  onSaved,
}: {
  account: Account
  onClose: () => void
  onSaved: () => void
}) {
  const [realBalance, setRealBalance] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function save(event: React.FormEvent) {
    event.preventDefault()
    setError(null)
    setBusy(true)

    try {
      const value = Number(realBalance.trim().replace(/\./g, '').replace(',', '.'))
      await endpoints.adjustBalance(account.id, value)
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
      title={`Ajustar saldo de ${account.name}`}
      subtitle="A diferenca vira uma transacao na categoria Ajuste. Nenhum lancamento antigo e alterado."
      onClose={onClose}
    >
      <form onSubmit={save} className="space-y-3">
        <p className="rounded-xl border border-line bg-canvas px-3 py-2.5 text-sm text-ink-muted">
          O app calcula hoje:{' '}
          <span className="tabular font-medium text-ink">{formatMoney(account.currentBalance)}</span>
        </p>

        <Field label="Saldo real no banco">
          <input
            className={inputClass}
            inputMode="decimal"
            placeholder="1198,00"
            value={realBalance}
            onChange={(event) => setRealBalance(event.target.value)}
            required
          />
        </Field>

        {error && <ErrorState message={error} />}

        <div className="flex justify-end gap-2 pt-2">
          <Button type="button" variant="ghost" onClick={onClose}>
            Cancelar
          </Button>
          <Button type="submit" disabled={busy}>
            {busy ? 'Ajustando...' : 'Ajustar'}
          </Button>
        </div>
      </form>
    </Modal>
  )
}

function DataSection() {
  const { logout } = useAuth()
  const [exporting, setExporting] = useState(false)
  const [exportError, setExportError] = useState<string | null>(null)
  const [confirming, setConfirming] = useState(false)
  const [confirmation, setConfirmation] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function exportData() {
    setExportError(null)
    setExporting(true)

    try {
      await endpoints.exportCsv()
    } catch (caught) {
      setExportError(caught instanceof ApiError ? caught.displayMessage : 'Nao foi possivel exportar.')
    } finally {
      setExporting(false)
    }
  }

  async function destroy(event: React.FormEvent) {
    event.preventDefault()
    setError(null)
    setBusy(true)

    try {
      await endpoints.deleteAccount(confirmation, password)
      await logout()
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.displayMessage : 'Nao foi possivel excluir.')
      setBusy(false)
    }
  }

  return (
    <>
      <Card title="Seus dados" subtitle="Direitos garantidos pela LGPD (SPEC US-12 e US-13)">
        <div className="grid gap-3 sm:grid-cols-2">
          <div className="rounded-xl border border-line p-4">
            <p className="text-sm font-medium text-ink">Exportar meus dados</p>
            <p className="mt-1 text-xs text-ink-faint">
              Baixa suas transacoes em CSV. Serve de backup e cobre o direito de portabilidade.
            </p>
            <button
              onClick={exportData}
              disabled={exporting}
              className="mt-3 inline-block text-sm font-medium text-brand hover:underline disabled:opacity-60"
            >
              {exporting ? 'Gerando...' : 'Baixar CSV'}
            </button>
            {exportError && <p className="mt-2 text-xs text-expense">{exportError}</p>}
          </div>

          <div className="rounded-xl border border-expense/25 p-4">
            <p className="text-sm font-medium text-expense">Excluir minha conta</p>
            <p className="mt-1 text-xs text-ink-faint">
              Apaga permanentemente usuario e todos os dados financeiros. Um unico DELETE,
              garantido em cascata pelo banco.
            </p>
            <button
              onClick={() => setConfirming(true)}
              className="mt-3 text-sm font-medium text-expense hover:underline"
            >
              Excluir conta
            </button>
          </div>
        </div>
      </Card>

      {confirming && (
        <Modal
          title="Excluir sua conta"
          subtitle="Esta acao nao tem volta. Todos os seus dados serao apagados."
          onClose={() => setConfirming(false)}
        >
          <form onSubmit={destroy} className="space-y-3">
            <Field label="Digite EXCLUIR para confirmar">
              <input
                className={inputClass}
                value={confirmation}
                onChange={(event) => setConfirmation(event.target.value)}
                placeholder="EXCLUIR"
                required
              />
            </Field>

            <Field label="Sua senha">
              <input
                className={inputClass}
                type="password"
                value={password}
                onChange={(event) => setPassword(event.target.value)}
                required
              />
            </Field>

            {error && <ErrorState message={error} />}

            <div className="flex justify-end gap-2 pt-2">
              <Button type="button" variant="ghost" onClick={() => setConfirming(false)}>
                Cancelar
              </Button>
              <Button type="submit" variant="danger" disabled={busy}>
                {busy ? 'Excluindo...' : 'Excluir para sempre'}
              </Button>
            </div>
          </form>
        </Modal>
      )}
    </>
  )
}
