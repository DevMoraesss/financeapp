import { BrowserRouter, Route, Routes } from 'react-router-dom'
import { AppLayout } from './components/AppLayout'
import { Loading } from './components/ui'
import { AuthProvider, useAuth } from './lib/AuthContext'
import { MonthProvider } from './lib/MonthContext'
import { Budget } from './pages/Budget'
import { Dashboard } from './pages/Dashboard'
import { Login } from './pages/Login'
import { Settings } from './pages/Settings'
import { Statements } from './pages/Statements'
import { Transactions } from './pages/Transactions'

export default function App() {
  return (
    <AuthProvider>
      <Shell />
    </AuthProvider>
  )
}

/**
 * Porteiro do app: sem sessao, so existe a tela de login.
 *
 * O estado "checking" evita o piscar classico: ao dar F5, o access token (que vive so em
 * memoria) some, e o app tenta renovar pelo cookie antes de decidir que o usuario saiu.
 */
function Shell() {
  const auth = useAuth()

  if (auth.status === 'checking') {
    return (
      <div className="grid min-h-full place-items-center">
        <Loading label="Restaurando sua sessao..." />
      </div>
    )
  }

  if (auth.status === 'anonymous') {
    return <Login />
  }

  return (
    <BrowserRouter>
      <MonthProvider>
        <Routes>
          <Route element={<AppLayout />}>
            <Route index element={<Dashboard />} />
            <Route path="transacoes" element={<Transactions />} />
            <Route path="faturas" element={<Statements />} />
            <Route path="orcamento" element={<Budget />} />
            <Route path="configuracoes" element={<Settings />} />
          </Route>
        </Routes>
      </MonthProvider>
    </BrowserRouter>
  )
}
