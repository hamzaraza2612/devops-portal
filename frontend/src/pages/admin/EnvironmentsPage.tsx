import { useState } from 'react';
import { EnvironmentsApi, TargetServersApi } from '../../api/endpoints';
import { ActionButton } from '../../components/ActionButton';
import { Card, ErrorBanner, LoadingSpinner, PageHeader } from '../../components/Common';
import { useAsyncData } from '../../hooks/useAsyncData';
import { Permissions } from '../../auth/permissions';
import { useAuth } from '../../auth/AuthContext';
import type { EnvironmentDefinitionDto, TargetServerDto } from '../../types/api';

async function loadEnvironmentsAndServers() {
  const [environments, targetServers] = await Promise.all([EnvironmentsApi.list(), TargetServersApi.list()]);
  return { environments, targetServers };
}

/** The four pipeline stages (DEV/QA/UAT/PRODUCTION) are fixed by name and
 * order — see IEnvironmentDefinitionService's doc comment for why a full
 * Create/hard-Delete isn't offered: permissions, deployment-mode dictionaries,
 * and workflow gating are all keyed by these exact names both server-side and
 * in this app. What IS safe and useful to edit from here: whether a tier is
 * flagged production-like (drives the CTO-approval gate), whether it's
 * active, and which TargetServer the Environment Infrastructure Dashboard
 * SSHes to for real Docker discovery. */
export function EnvironmentAdminPage() {
  const { data, isLoading, error, reload } = useAsyncData(loadEnvironmentsAndServers, []);
  const { can } = useAuth();

  if (isLoading) return <LoadingSpinner label="Loading environments…" />;
  if (error) return <ErrorBanner message={error} onDismiss={reload} />;
  if (!data) return null;

  return (
    <div>
      <PageHeader
        title="Environments"
        subtitle="The DEV → QA → UAT → PRODUCTION pipeline stages. Names and order are fixed by the workflow; you can mark a stage production-like (requires CTO approval), deactivate it, or assign the target server its infrastructure dashboard discovers real Docker containers from."
      />

      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        {data.environments
          .slice()
          .sort((a, b) => a.sortOrder - b.sortOrder)
          .map((env) => (
            <EnvironmentCard
              key={env.id}
              env={env}
              targetServers={data.targetServers}
              canManage={can(Permissions.EnvironmentsManage)}
              onChanged={reload}
            />
          ))}
      </div>
    </div>
  );
}

function EnvironmentCard({
  env,
  targetServers,
  canManage,
  onChanged,
}: {
  env: EnvironmentDefinitionDto;
  targetServers: TargetServerDto[];
  canManage: boolean;
  onChanged: () => void;
}) {
  const [showEdit, setShowEdit] = useState(false);
  const [isProductionLike, setIsProductionLike] = useState(env.isProductionLike);
  const [primaryTargetServerId, setPrimaryTargetServerId] = useState(env.primaryTargetServerId ?? '');

  return (
    <Card className={env.isActive ? '' : 'opacity-60'}>
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-semibold text-slate-900">{env.name}</h3>
        <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${env.isActive ? 'bg-emerald-100 text-emerald-700' : 'bg-slate-200 text-slate-600'}`}>
          {env.isActive ? 'Active' : 'Inactive'}
        </span>
      </div>
      <p className="mt-2 text-xs text-slate-500">Order: {env.sortOrder}</p>
      {env.isProductionLike && (
        <span className="mt-2 inline-block rounded-full bg-purple-100 px-2 py-0.5 text-xs font-medium text-purple-700">
          Production-like (CTO approval required)
        </span>
      )}
      <p className="mt-2 text-xs text-slate-500">
        Infrastructure server: {env.primaryTargetServerName ?? <span className="text-slate-400">not assigned</span>}
      </p>

      {canManage && (
        <div className="mt-3 flex flex-wrap gap-2 border-t border-slate-100 pt-3">
          <button
            type="button"
            onClick={() => {
              setShowEdit((v) => !v);
              setIsProductionLike(env.isProductionLike);
              setPrimaryTargetServerId(env.primaryTargetServerId ?? '');
            }}
            className="text-xs font-medium text-slate-500 hover:text-slate-700"
          >
            {showEdit ? 'Cancel' : 'Edit'}
          </button>
          <ActionButton
            label={env.isActive ? 'Deactivate' : 'Activate'}
            variant={env.isActive ? 'danger' : 'secondary'}
            confirmLabel={env.isActive ? 'Confirm deactivate' : undefined}
            onAction={() =>
              EnvironmentsApi.update(env.id, {
                isProductionLike: env.isProductionLike,
                isActive: !env.isActive,
                primaryTargetServerId: env.primaryTargetServerId,
              })
            }
            onSuccess={onChanged}
          />
        </div>
      )}

      {showEdit && canManage && (
        <div className="mt-3 space-y-2 border-t border-slate-100 pt-3">
          <label className="flex items-center gap-1.5 text-xs font-medium text-slate-700">
            <input type="checkbox" checked={isProductionLike} onChange={(e) => setIsProductionLike(e.target.checked)} />
            Production-like (requires CTO approval to promote into)
          </label>
          <label className="block text-xs font-medium text-slate-600">
            Infrastructure server (for real Docker discovery)
            <select
              value={primaryTargetServerId}
              onChange={(e) => setPrimaryTargetServerId(e.target.value)}
              className="mt-1 w-full rounded-md border border-slate-300 px-2.5 py-1.5 text-sm focus:border-slate-500 focus:outline-none"
            >
              <option value="">Not assigned</option>
              {targetServers.map((server) => (
                <option key={server.id} value={server.id}>{server.name}</option>
              ))}
            </select>
          </label>
          <ActionButton
            label="Save changes"
            onAction={() =>
              EnvironmentsApi.update(env.id, {
                isProductionLike,
                isActive: env.isActive,
                primaryTargetServerId: primaryTargetServerId || null,
              })
            }
            onSuccess={() => {
              setShowEdit(false);
              onChanged();
            }}
          />
        </div>
      )}
    </Card>
  );
}
