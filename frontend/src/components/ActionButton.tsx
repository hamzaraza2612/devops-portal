import { useState, type ReactNode } from 'react';
import { describeError } from '../api/client';

type Variant = 'primary' | 'danger' | 'secondary';

const variantClasses: Record<Variant, string> = {
  primary: 'bg-slate-900 text-white hover:bg-slate-700 disabled:bg-slate-300',
  danger: 'bg-red-600 text-white hover:bg-red-700 disabled:bg-red-300',
  secondary: 'bg-white text-slate-700 border border-slate-300 hover:bg-slate-50 disabled:text-slate-400',
};

/**
 * A button that performs an async API action, shows a busy state while it's
 * in flight, an inline error if it fails, and calls `onSuccess` if it
 * succeeds. Optionally requires a confirm click first (for destructive/
 * high-consequence actions like Deploy to Production or Rollback).
 *
 * This component only decides what to *show* — permission gating is the
 * caller's job (wrap with `<Can permission=...>`), and the backend is the
 * actual authority: an action that somehow reaches the API without
 * permission still comes back as a 403, surfaced here like any other error.
 */
export function ActionButton({
  label,
  confirmLabel,
  variant = 'primary',
  disabled,
  disabledReason,
  onAction,
  onSuccess,
}: {
  label: string;
  confirmLabel?: string;
  variant?: Variant;
  disabled?: boolean;
  disabledReason?: string;
  onAction: () => Promise<unknown>;
  onSuccess?: () => void;
}) {
  const [isBusy, setIsBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [needsConfirm, setNeedsConfirm] = useState(false);

  async function run() {
    setError(null);
    setIsBusy(true);
    try {
      await onAction();
      setNeedsConfirm(false);
      onSuccess?.();
    } catch (err) {
      setError(describeError(err));
    } finally {
      setIsBusy(false);
    }
  }

  function handleClick() {
    if (confirmLabel && !needsConfirm) {
      setNeedsConfirm(true);
      return;
    }
    void run();
  }

  const showConfirm = Boolean(confirmLabel) && needsConfirm;

  return (
    <div className="inline-flex flex-col items-start gap-1">
      <div className="inline-flex items-center gap-2">
        <button
          type="button"
          onClick={handleClick}
          disabled={disabled || isBusy}
          title={disabled ? disabledReason : undefined}
          className={`rounded-md px-3 py-1.5 text-sm font-medium transition-colors disabled:cursor-not-allowed ${variantClasses[showConfirm ? 'danger' : variant]}`}
        >
          {isBusy ? 'Working…' : showConfirm ? (confirmLabel as ReactNode) : label}
        </button>
        {showConfirm && (
          <button
            type="button"
            onClick={() => setNeedsConfirm(false)}
            className="text-sm text-slate-500 hover:text-slate-700"
          >
            Cancel
          </button>
        )}
      </div>
      {error && <span className="text-xs text-red-600">{error}</span>}
    </div>
  );
}
