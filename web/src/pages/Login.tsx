import { useState, type FormEvent } from 'react'
import { Wallet } from 'lucide-react'
import { ApiError, NETWORK_ERROR_MESSAGE } from '../lib/api'
import { useAuth } from '../lib/AuthContext'
import { Button, ErrorState } from '../components/ui'

export function Login() {
  const { login, register } = useAuth()
  const [mode, setMode] = useState<'login' | 'register'>('login')
  const [name, setName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [inviteCode, setInviteCode] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    setBusy(true)

    try {
      if (mode === 'login') {
        await login(email, password)
      } else {
        await register(name, email, password, inviteCode)
      }
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.displayMessage : NETWORK_ERROR_MESSAGE)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="flex min-h-full items-center justify-center px-4">
      <div className="w-full max-w-sm">
        <div className="mb-8 flex items-center gap-3">
          <span className="grid size-11 place-items-center rounded-full bg-brand text-brand-ink">
            <Wallet className="size-5" />
          </span>
          <div>
            <p className="text-xl font-semibold text-ink">FinanceMove</p>
            <p className="text-sm text-ink-faint">Financas pessoais</p>
          </div>
        </div>

        <div className="rounded-[--radius-card] border border-line bg-surface p-6">
          <h1 className="text-lg font-semibold text-ink">
            {mode === 'login' ? 'Entrar na sua conta' : 'Criar sua conta'}
          </h1>
          <p className="mt-1 text-sm text-ink-faint">
            {mode === 'login'
              ? 'Use o e-mail e a senha que voce cadastrou.'
              : 'O FinanceMove e por convite. Suas categorias padrao sao criadas junto com a conta.'}
          </p>

          <form onSubmit={handleSubmit} className="mt-5 space-y-3">
            {mode === 'register' && (
              <Field label="Nome" value={name} onChange={setName} autoComplete="name" required />
            )}

            <Field
              label="E-mail"
              type="email"
              value={email}
              onChange={setEmail}
              autoComplete="email"
              required
            />

            <Field
              label="Senha"
              type="password"
              value={password}
              onChange={setPassword}
              autoComplete={mode === 'login' ? 'current-password' : 'new-password'}
              hint={
                mode === 'register'
                  ? 'Minimo de 12 caracteres. Uma frase facil de lembrar vale mais que simbolos.'
                  : undefined
              }
              minLength={mode === 'register' ? 12 : undefined}
              maxLength={128}
              required
            />

            {mode === 'register' && (
              <Field
                label="Codigo de convite"
                value={inviteCode}
                onChange={setInviteCode}
                autoComplete="off"
                hint="Peca o codigo a quem te convidou."
                required
              />
            )}

            {error && <ErrorState message={error} />}

            <Button type="submit" disabled={busy} className="w-full justify-center">
              {busy ? 'Aguarde...' : mode === 'login' ? 'Entrar' : 'Criar conta'}
            </Button>
          </form>

          <button
            type="button"
            onClick={() => {
              setMode(mode === 'login' ? 'register' : 'login')
              setError(null)
            }}
            className="mt-4 w-full text-center text-sm text-ink-muted transition hover:text-brand"
          >
            {mode === 'login' ? 'Nao tem conta? Criar agora' : 'Ja tem conta? Entrar'}
          </button>
        </div>
      </div>
    </div>
  )
}

function Field({
  label,
  value,
  onChange,
  type = 'text',
  hint,
  ...props
}: {
  label: string
  value: string
  onChange: (value: string) => void
  type?: string
  hint?: string
} & Omit<React.InputHTMLAttributes<HTMLInputElement>, 'value' | 'onChange' | 'type'>) {
  return (
    <label className="block">
      <span className="mb-1.5 block text-sm text-ink-muted">{label}</span>
      <input
        {...props}
        type={type}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className="w-full rounded-xl border border-line bg-canvas px-3 py-2.5 text-sm text-ink outline-none transition focus:border-brand"
      />
      {hint && <span className="mt-1 block text-xs text-ink-faint">{hint}</span>}
    </label>
  )
}
