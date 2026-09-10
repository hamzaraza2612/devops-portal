import { useMemo, useState } from 'react';
import { PromotionsApi } from '../api/endpoints';
import { EmptyState, ErrorBanner, LoadingSpinner, PageHeader } from '../components/Common';
import { PromotionCard } from '../components/PromotionCard';
import { useAsyncData } from '../hooks/useAsyncData';
import { useAuth } from '../auth/AuthContext';
import { hasEnvironmentAccess, type EnvironmentTier } from '../auth/permissions';

const promotableTiers: EnvironmentTier[] = ['QA', 'UAT', 'PRODUCTION'];

/**
 * One environment-specific view per tab, exactly per the master
 * requirements' example format: application, commit, requester, and status
 * are always shown together — never a generic undifferentiated list.
 */
export function PendingRequestsPage() {
  const { data, isLoading, error, reload } = useAsyncData(() => PromotionsApi.listPending({ includeApprovedAwaitingDeploy: true }), []);
  const { user } = useAuth();
  const [activeTier, setActiveTier] = useState<EnvironmentTier>('QA');

  const visibleTiers = useMemo(
    () => promotableTiers.filter((tier) => hasEnvironmentAccess(user?.permissions ?? [], tier)),
    [user],
  );

  if (isLoading) return <LoadingSpinner label="Loading pending requests…" />;
  if (error) return <ErrorBanner message={error} onDismiss={reload} />;
  if (!data) return null;

  const currentTier = visibleTiers.includes(activeTier) ? activeTier : visibleTiers[0];
  const forTier = data.filter((p) => p.toEnvironmentName === currentTier);

  return (
    <div>
      <PageHeader title="Pending Requests" subtitle="Promotion requests awaiting approval or deployment, grouped by target environment." />

      {visibleTiers.length === 0 ? (
        <EmptyState title="You don't have access to any environment's pending requests." />
      ) : (
        <>
          <div className="mb-4 flex gap-2 border-b border-slate-200">
            {visibleTiers.map((tier) => {
              const count = data.filter((p) => p.toEnvironmentName === tier).length;
              return (
                <button
                  key={tier}
                  type="button"
                  onClick={() => setActiveTier(tier)}
                  className={`border-b-2 px-3 py-2 text-sm font-medium ${
                    currentTier === tier ? 'border-slate-900 text-slate-900' : 'border-transparent text-slate-500 hover:text-slate-700'
                  }`}
                >
                  {tier} {count > 0 && <span className="ml-1 rounded-full bg-amber-100 px-1.5 py-0.5 text-xs text-amber-700">{count}</span>}
                </button>
              );
            })}
          </div>

          {forTier.length === 0 ? (
            <EmptyState title={`No pending ${currentTier} requests.`} />
          ) : (
            <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
              {forTier.map((promotion) => (
                <PromotionCard key={promotion.id} promotion={promotion} onChanged={reload} />
              ))}
            </div>
          )}
        </>
      )}
    </div>
  );
}
