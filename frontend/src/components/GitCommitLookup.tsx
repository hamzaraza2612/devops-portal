import { useState } from 'react';
import { ApplicationsApi } from '../api/endpoints';
import { describeError } from '../api/client';
import { shortSha } from '../utils/format';

/**
 * On-demand "what's the latest commit" lookup (GitLab REST call, made only
 * when the user clicks — not eagerly for every row of a list) so a slow or
 * unreachable GitLab never blocks page loads. The backend call itself never
 * throws for expected failures (see IGitProviderClient); a `Success: false`
 * result is shown as an inline message, not an error banner.
 */
export function GitCommitLookup({
  applicationId,
  environmentDefinitionId,
  onPick,
}: {
  applicationId: string;
  environmentDefinitionId: string;
  onPick: (sha: string) => void;
}) {
  const [state, setState] = useState<
    | { status: 'idle' }
    | { status: 'loading' }
    | { status: 'found'; sha: string; message: string }
    | { status: 'unavailable'; reason: string }
    | { status: 'error'; message: string }
  >({ status: 'idle' });

  async function lookup() {
    setState({ status: 'loading' });
    try {
      const result = await ApplicationsApi.latestCommit(applicationId, environmentDefinitionId);
      if (result.success && result.data) {
        setState({ status: 'found', sha: result.data.sha, message: result.data.message });
      } else {
        setState({ status: 'unavailable', reason: result.errorMessage ?? 'No commit information available.' });
      }
    } catch (err) {
      setState({ status: 'error', message: describeError(err) });
    }
  }

  return (
    <div className="text-xs">
      {state.status === 'idle' && (
        <button type="button" onClick={() => void lookup()} className="text-blue-600 hover:underline">
          Look up latest available commit
        </button>
      )}
      {state.status === 'loading' && <span className="text-slate-400">Looking up latest commit…</span>}
      {state.status === 'unavailable' && <span className="text-slate-400">{state.reason}</span>}
      {state.status === 'error' && <span className="text-red-600">{state.message}</span>}
      {state.status === 'found' && (
        <div className="flex items-center justify-between gap-2 rounded-md bg-slate-50 px-2 py-1">
          <span className="truncate">
            <span className="font-mono">{shortSha(state.sha)}</span> — {state.message}
          </span>
          <button type="button" onClick={() => onPick(state.sha)} className="shrink-0 font-medium text-blue-600 hover:underline">
            Use this
          </button>
        </div>
      )}
    </div>
  );
}
