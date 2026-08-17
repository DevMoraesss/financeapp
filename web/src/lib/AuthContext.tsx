import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { auth as authApi, setAccessToken, setSessionLostHandler, type User } from './api'

type AuthState =
  /** Ainda tentando renovar a sessao pelo cookie. Evita piscar a tela de login em cada F5. */
  | { status: 'checking' }
  | { status: 'authenticated'; user: User }
  | { status: 'anonymous' }

type AuthContextValue = AuthState & {
  login: (email: string, password: string) => Promise<void>
  register: (name: string, email: string, password: string) => Promise<void>
  logout: () => Promise<void>
}

const AuthContext = createContext<AuthContextValue | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState>({ status: 'checking' })

  // Ao recarregar a pagina o access token (que so vive em memoria) some. O refresh token esta
  // no cookie httpOnly, entao tentamos renovar em silencio antes de decidir que o usuario saiu.
  useEffect(() => {
    let cancelled = false

    authApi
      .refresh()
      .then((response) => {
        if (cancelled) return
        setAccessToken(response.accessToken)
        setState({ status: 'authenticated', user: response.user })
      })
      .catch(() => {
        if (cancelled) return
        setAccessToken(null)
        setState({ status: 'anonymous' })
      })

    return () => {
      cancelled = true
    }
  }, [])

  // Quando o refresh falha no meio de uma chamada qualquer, o cliente HTTP avisa aqui.
  useEffect(() => {
    setSessionLostHandler(() => {
      setAccessToken(null)
      setState({ status: 'anonymous' })
    })

    return () => setSessionLostHandler(null)
  }, [])

  const login = useCallback(async (email: string, password: string) => {
    const response = await authApi.login(email, password)
    setAccessToken(response.accessToken)
    setState({ status: 'authenticated', user: response.user })
  }, [])

  const register = useCallback(
    async (name: string, email: string, password: string) => {
      await authApi.register(name, email, password)
      await login(email, password)
    },
    [login],
  )

  const logout = useCallback(async () => {
    try {
      await authApi.logout()
    } finally {
      setAccessToken(null)
      setState({ status: 'anonymous' })
    }
  }, [])

  const value = useMemo<AuthContextValue>(
    () => ({ ...state, login, register, logout }),
    [state, login, register, logout],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext)
  if (!context) throw new Error('useAuth precisa estar dentro de <AuthProvider>.')
  return context
}
