/**
 * Cliente HTTP do FinanceMove.
 *
 * Os tipos abaixo sao a traducao literal de docs/api.md. Se a API mudar, este arquivo muda junto.
 * Campos em ingles (contrato), textos de erro em pt-BR (vao para a tela).
 */

export type ProblemDetails = {
  type?: string
  title: string
  status: number
  detail?: string
  instance?: string
  traceId?: string
  errors?: Record<string, string[]>
}

/** Erro de API ja no formato ProblemDetails. Todo erro do backend chega assim. */
export class ApiError extends Error {
  // Campo declarado e atribuido explicitamente: o TypeScript deste projeto roda com
  // erasableSyntaxOnly, que proibe atalhos como constructor(readonly problem: ...).
  readonly problem: ProblemDetails

  constructor(problem: ProblemDetails) {
    super(problem.title)
    this.name = 'ApiError'
    this.problem = problem
  }

  get status(): number {
    return this.problem.status
  }

  /** Mensagem pronta para exibir: junta os erros de campo quando houver. */
  get displayMessage(): string {
    const fieldErrors = Object.values(this.problem.errors ?? {}).flat()
    if (fieldErrors.length > 0) return fieldErrors.join(' ')
    return this.problem.detail ?? this.problem.title
  }
}

const BASE_URL = '/api/v1'

/**
 * Access token guardado APENAS em memoria.
 *
 * Nada de localStorage: qualquer XSS na pagina leria o token de la. O refresh token vive num
 * cookie httpOnly, que o JavaScript nao enxerga (ADR-001, Decisao 5). Ao recarregar a pagina o
 * token some, e o app pede um novo em /auth/refresh usando o cookie.
 */
let accessToken: string | null = null
let onSessionLost: (() => void) | null = null

export function setAccessToken(token: string | null): void {
  accessToken = token
}

export function setSessionLostHandler(handler: (() => void) | null): void {
  onSessionLost = handler
}

async function rawRequest(path: string, init: RequestInit): Promise<Response> {
  return fetch(`${BASE_URL}${path}`, {
    ...init,
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
      ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
      ...init.headers,
    },
  })
}

async function request<T>(path: string, init: RequestInit = {}, allowRetry = true): Promise<T> {
  let response = await rawRequest(path, init)

  // Access token expirado (15 min): tenta uma renovacao silenciosa pelo cookie antes de
  // devolver o usuario para a tela de login.
  if (response.status === 401 && allowRetry && !path.startsWith('/auth/')) {
    const renewed = await tryRefresh()

    if (renewed) {
      response = await rawRequest(path, init)
    } else {
      onSessionLost?.()
    }
  }

  if (response.status === 204) return undefined as T

  const isJson = response.headers.get('content-type')?.includes('application/json') ?? false
  const body = isJson ? await response.json() : null

  if (!response.ok) {
    throw new ApiError(
      body ?? { title: 'Nao foi possivel completar a operacao.', status: response.status },
    )
  }

  return body as T
}

async function tryRefresh(): Promise<boolean> {
  try {
    const response = await fetch(`${BASE_URL}/auth/refresh`, {
      method: 'POST',
      credentials: 'include',
    })

    if (!response.ok) return false

    const data = (await response.json()) as LoginResponse
    accessToken = data.accessToken
    return true
  } catch {
    return false
  }
}

const api = {
  get: <T>(path: string) => request<T>(path),
  post: <T>(path: string, body?: unknown) =>
    request<T>(path, { method: 'POST', body: body === undefined ? undefined : JSON.stringify(body) }),
  put: <T>(path: string, body?: unknown) =>
    request<T>(path, { method: 'PUT', body: body === undefined ? undefined : JSON.stringify(body) }),
  del: <T>(path: string, body?: unknown) =>
    request<T>(path, { method: 'DELETE', body: body === undefined ? undefined : JSON.stringify(body) }),
}

// ---------------------------------------------------------------------------
// Tipos do contrato (docs/api.md)
// ---------------------------------------------------------------------------

export type AccountType = 'checking' | 'savings' | 'cash' | 'credit_card'
export type TransactionType = 'income' | 'expense' | 'transfer'
export type TransactionStatus = 'pending' | 'confirmed'
export type CategoryKind = 'income' | 'expense'
export type BudgetStatus = 'normal' | 'warning' | 'over'
export type StatementStatus = 'open' | 'closed' | 'paid'
export type RecurrenceFrequency = 'weekly' | 'monthly' | 'yearly'

export type User = { id: string; name: string; email: string }

export type LoginResponse = { accessToken: string; expiresAt: string; user: User }

export type Account = {
  id: string
  name: string
  type: AccountType
  initialBalance: number
  /** Ja calculado pelo servidor (SPEC secao 5.1). A UI nunca soma transacoes. */
  currentBalance: number
  closingDay?: number | null
  dueDay?: number | null
  archived: boolean
}

export type Category = {
  id: string
  name: string
  type: CategoryKind
  color: string
  icon: string
  system: boolean
  archived: boolean
}

export type Transaction = {
  id: string
  type: TransactionType
  amount: number
  date: string
  description: string
  status: TransactionStatus
  account: { id: string; name: string }
  destinationAccount: { id: string; name: string } | null
  category: { id: string; name: string; color: string; icon: string } | null
  installment: { number: number; total: number } | null
  recurring: boolean
  statementMonth: string | null
}

export type MonthSummary = { income: number; expenses: number; balance: number }

export type Dashboard = {
  totalBalance: number
  accounts: Array<{ id: string; name: string; type: AccountType; currentBalance: number }>
  month: MonthSummary
  expensesByCategory: Array<{
    categoryId: string
    category: string
    color: string
    amount: number
    percent: number
  }>
  recentTransactions: Transaction[]
  pending: Transaction[]
}

export type TransactionList = {
  items: Transaction[]
  pagination: { page: number; size: number; total: number; totalPages: number }
  monthSummary: MonthSummary
}

export type BudgetOverview = {
  month: string
  summary: {
    totalSpent: number
    totalLimit: number
    categoriesNearLimit: number
    categoriesOverLimit: number
  }
  items: Array<{
    categoryId: string
    category: string
    color: string
    limit: number
    spent: number
    percent: number
    status: BudgetStatus
    difference: number
  }>
}

export type Statement = {
  month: string
  closingDate: string
  dueDate: string
  periodStart: string
  periodEnd: string
  status: StatementStatus
  total: number
}

export type StatementDetail = { summary: Statement; purchases: Transaction[] }

export type Recurrence = {
  id: string
  description: string
  amount: number
  type: CategoryKind
  frequency: RecurrenceFrequency
  referenceDay: number
  referenceMonth: number | null
  startsOn: string
  endsOn: string | null
  nextRunOn: string
  active: boolean
  account: { id: string; name: string }
  category: { id: string; name: string; color: string; icon: string }
}

export type TransactionWriteResult = {
  transaction: Transaction
  accountBalance: number
  destinationAccountBalance: number | null
}

// ---------------------------------------------------------------------------
// Endpoints
// ---------------------------------------------------------------------------

export const auth = {
  register: (name: string, email: string, password: string) =>
    api.post<User>('/auth/register', { name, email, password }),
  login: (email: string, password: string) => api.post<LoginResponse>('/auth/login', { email, password }),
  refresh: () => api.post<LoginResponse>('/auth/refresh'),
  logout: () => api.post<void>('/auth/logout'),
}

export const endpoints = {
  me: () => api.get<User>('/me'),
  deleteAccount: (confirmation: string, password: string) =>
    api.del<void>('/me', { confirmation, password }),

  dashboard: (month: string) => api.get<Dashboard>(`/dashboard?month=${month}`),

  accounts: () => api.get<{ accounts: Account[]; totalBalance: number }>('/accounts'),
  createAccount: (body: {
    name: string
    type: AccountType
    initialBalance: number
    closingDay?: number | null
    dueDay?: number | null
  }) => api.post<Account>('/accounts', body),
  archiveAccount: (id: string) => api.post<void>(`/accounts/${id}/archive`),
  adjustBalance: (id: string, realBalance: number) =>
    api.post<{ createdTransaction: Transaction; currentBalance: number }>(
      `/accounts/${id}/adjust-balance`,
      { realBalance },
    ),

  categories: (type?: CategoryKind) =>
    api.get<{ categories: Category[] }>(`/categories${type ? `?type=${type}` : ''}`),

  transactions: (query: string) => api.get<TransactionList>(`/transactions${query}`),
  createTransaction: (body: {
    type: TransactionType
    amount: number
    date: string
    description: string
    accountId: string
    destinationAccountId?: string | null
    categoryId?: string | null
  }) => api.post<TransactionWriteResult>('/transactions', body),
  createInstallments: (body: {
    description: string
    totalAmount: number
    installments: number
    date: string
    cardId: string
    categoryId: string
  }) => api.post<{ installmentGroupId: string; transactions: Transaction[] }>(
    '/transactions/installments',
    body,
  ),
  deleteTransaction: (id: string) => api.del<void>(`/transactions/${id}`),
  confirmTransaction: (id: string, amount?: number) =>
    api.post<TransactionWriteResult>(`/transactions/${id}/confirm`, { amount: amount ?? null }),
  discardTransaction: (id: string) => api.del<void>(`/transactions/${id}/discard`),

  statements: (cardId: string) => api.get<{ statements: Statement[] }>(`/cards/${cardId}/statements`),
  statement: (cardId: string, month: string) =>
    api.get<StatementDetail>(`/cards/${cardId}/statements/${month}`),
  payStatement: (cardId: string, month: string, sourceAccountId: string) =>
    api.post<{ status: StatementStatus; sourceAccountBalance: number; cardBalance: number }>(
      `/cards/${cardId}/statements/${month}/pay`,
      { sourceAccountId },
    ),

  budgets: (month: string) => api.get<BudgetOverview>(`/budgets?month=${month}`),
  setBudget: (categoryId: string, monthlyLimit: number) =>
    api.put<unknown>(`/budgets/${categoryId}`, { monthlyLimit }),
  removeBudget: (categoryId: string) => api.del<void>(`/budgets/${categoryId}`),

  recurrences: () => api.get<{ recurrences: Recurrence[] }>('/recurrences'),
  createRecurrence: (body: {
    description: string
    amount: number
    type: CategoryKind
    frequency: RecurrenceFrequency
    referenceDay: number
    referenceMonth?: number | null
    startsOn: string
    accountId: string
    categoryId: string
  }) => api.post<Recurrence>('/recurrences', body),
  deactivateRecurrence: (id: string) => api.post<void>(`/recurrences/${id}/deactivate`),
}
