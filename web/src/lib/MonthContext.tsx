import { createContext, useContext, useMemo, useState, type ReactNode } from 'react'
import { currentMonth } from './format'

type MonthContextValue = {
  /** Mês selecionado no formato "AAAA-MM". */
  month: string
  setMonth: (month: string) => void
}

const MonthContext = createContext<MonthContextValue | null>(null)

/**
 * O navegador de mês do topo é um filtro GLOBAL: Dashboard, Transações, Faturas e Orçamento
 * respondem todos a ele. Por isso o estado vive aqui, e não dentro de cada tela.
 */
export function MonthProvider({ children }: { children: ReactNode }) {
  const [month, setMonth] = useState(currentMonth)
  const value = useMemo(() => ({ month, setMonth }), [month])

  return <MonthContext.Provider value={value}>{children}</MonthContext.Provider>
}

export function useMonth(): MonthContextValue {
  const context = useContext(MonthContext)
  if (!context) throw new Error('useMonth precisa estar dentro de <MonthProvider>.')
  return context
}
