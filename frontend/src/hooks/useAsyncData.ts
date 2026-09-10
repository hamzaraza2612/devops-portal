import { useCallback, useEffect, useState } from 'react';
import { describeError } from '../api/client';

interface AsyncState<T> {
  data: T | null;
  isLoading: boolean;
  error: string | null;
}

/** Loads `fetcher()` on mount and whenever `deps` change; exposes `reload`
 * for manual refresh (used for the deployment-logs auto-refresh loop and
 * "retry" buttons on error). Aborts the in-flight request on unmount/refetch
 * so a slow response never overwrites state from a newer one. */
export function useAsyncData<T>(fetcher: (signal: AbortSignal) => Promise<T>, deps: readonly unknown[]): AsyncState<T> & { reload: () => void } {
  const [state, setState] = useState<AsyncState<T>>({ data: null, isLoading: true, error: null });
  const [reloadToken, setReloadToken] = useState(0);

  const reload = useCallback(() => setReloadToken((n) => n + 1), []);

  useEffect(() => {
    const controller = new AbortController();
    setState((prev) => ({ ...prev, isLoading: true, error: null }));
    fetcher(controller.signal)
      .then((data) => {
        if (!controller.signal.aborted) setState({ data, isLoading: false, error: null });
      })
      .catch((err) => {
        if (!controller.signal.aborted) setState({ data: null, isLoading: false, error: describeError(err) });
      });
    return () => controller.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, reloadToken]);

  return { ...state, reload };
}
