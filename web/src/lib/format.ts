/**
 * Formatação pt-BR. Este arquivo só FORMATA - nunca calcula.
 *
 * A regra vem do ADR-001 (Decisão 3): todo valor monetário chega pronto da API. JavaScript
 * carrega número JSON como `double`, então somar aqui reintroduziria o erro de ponto flutuante
 * que o `decimal` do servidor eliminou (0.1 + 0.2 === 0.30000000000000004).
 *
 * Se você precisar de um total que a API não devolve, o certo é a API passar a devolvê-lo.
 */

const currency = new Intl.NumberFormat('pt-BR', {
  style: 'currency',
  currency: 'BRL',
})

const dateFormatter = new Intl.DateTimeFormat('pt-BR', {
  day: '2-digit',
  month: '2-digit',
  year: 'numeric',
  timeZone: 'America/Sao_Paulo',
})

const monthFormatter = new Intl.DateTimeFormat('pt-BR', {
  month: 'long',
  year: 'numeric',
  timeZone: 'America/Sao_Paulo',
})

/** 3730.5 -> "R$ 3.730,50" */
export function formatMoney(value: number): string {
  return currency.format(value)
}

/** 1259.15 -> "+R$ 1.259,15" / "-R$ 1.259,15" conforme o tipo do lançamento. */
export function formatSigned(value: number, kind: 'income' | 'expense'): string {
  const sign = kind === 'income' ? '+' : '-'
  return `${sign}${currency.format(Math.abs(value))}`
}

/** "2026-08-14" -> "14/08/2026" */
export function formatDate(isoDate: string): string {
  const [year, month, day] = isoDate.split('-').map(Number)
  return dateFormatter.format(new Date(year, month - 1, day))
}

/** "2026-08" -> "Agosto de 2026" */
export function formatMonth(isoMonth: string): string {
  const [year, month] = isoMonth.split('-').map(Number)
  const label = monthFormatter.format(new Date(year, month - 1, 1))
  return label.charAt(0).toUpperCase() + label.slice(1)
}

/** 114.5 -> "114,5%" */
export function formatPercent(value: number, digits = 1): string {
  return `${value.toLocaleString('pt-BR', {
    minimumFractionDigits: digits,
    maximumFractionDigits: digits,
  })}%`
}

/** Mês atual no formato AAAA-MM, no fuso do produto. */
export function currentMonth(): string {
  return todayIso().slice(0, 7)
}

/** Data de hoje em AAAA-MM-DD, no fuso do produto (o mesmo que o servidor usa). */
export function todayIso(): string {
  return new Date().toLocaleDateString('en-CA', { timeZone: 'America/Sao_Paulo' })
}

/** "2026-09" -> "09/2026" (fatura identificada pelo mes em que vence). */
export function formatStatementMonth(isoMonth: string): string {
  const [year, month] = isoMonth.split('-')
  return `${month}/${year}`
}

/** Rótulo do tipo de conta para a UI. */
export function accountTypeLabel(type: string): string {
  const labels: Record<string, string> = {
    checking: 'Conta corrente',
    savings: 'Poupança',
    cash: 'Dinheiro',
    credit_card: 'Cartão de crédito',
  }

  return labels[type] ?? type
}

/** Avança ou retrocede meses sobre "AAAA-MM". */
export function shiftMonth(isoMonth: string, delta: number): string {
  const [year, month] = isoMonth.split('-').map(Number)
  const date = new Date(year, month - 1 + delta, 1)
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`
}
