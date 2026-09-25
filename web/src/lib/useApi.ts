import { useCallback, useEffect, useState } from 'react'
import { ApiError, NETWORK_ERROR_MESSAGE } from './api'

type State<T> =
  | { status: 'loading' }
  | { status: 'ready'; data: T }
  | { status: 'error'; message: string }

/**
 * Carrega dados da API tratando os tres estados que toda tela precisa: carregando, pronto e erro.
 * Devolve tambem um reload() para as telas se atualizarem depois de uma escrita.
 */
export function useApi<T>(
  load: () => Promise<T>,
  deps: unknown[] = [],
): State<T> & { reload: () => void } {
  const [state, setState] = useState<State<T>>({ status: 'loading' })
  const [nonce, setNonce] = useState(0)

  // eslint-disable-next-line react-hooks/exhaustive-deps
  const run = useCallback(load, deps)

  useEffect(() => {
    let cancelled = false
    setState({ status: 'loading' })

    run()
      .then((data) => {
        if (!cancelled) setState({ status: 'ready', data })
      })
      .catch((error: unknown) => {
        if (cancelled) return

        setState({
          status: 'error',
          message: error instanceof ApiError ? error.displayMessage : NETWORK_ERROR_MESSAGE,
        })
      })

    return () => {
      cancelled = true
    }
  }, [run, nonce])

  const reload = useCallback(() => setNonce((value) => value + 1), [])

  return { ...state, reload }
}
