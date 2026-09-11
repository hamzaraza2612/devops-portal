import { useState } from 'react';
import { TargetServersApi } from '../../api/endpoints';
import { ActionButton } from '../../components/ActionButton';
import { Card, EmptyState, ErrorBanner, LoadingSpinner, PageHeader } from '../../components/Common';
import { useAsyncData } from '../../hooks/useAsyncData';
import { Permissions } from '../../auth/permissions';
import { useAuth } from '../../auth/AuthContext';

export function TargetServersPage() {
  const { data, isLoading, error, reload } = useAsyncData(TargetServersApi.list, []);
  const { can } = useAuth();
  const [showCreate, setShowCreate] = useState(false);
  const [expandedId, setExpandedId] = useState<string | null>(null);

  if (isLoading) return <LoadingSpinner label="Loading deployment targets…" />;
  if (error) return <ErrorBanner message={error} onDismiss={reload} />;
  if (!data) return null;

  return (
    <div>
      <PageHeader
        title="Deployment Targets"
        subtitle="Hosts that run application containers, and the base directories on each one applications are allowed to deploy into."
        actions={
          can(Permissions.TargetServersManage) && (
            <button
              type="button"
              onClick={() => setShowCreate((v) => !v)}
              className="rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white hover:bg-slate-700"
            >
              {showCreate ? 'Cancel' : 'New Target Server'}
            </button>
          )
        }
      />

      {showCreate && (
        <TargetServerForm
          onSubmitted={() => {
            setShowCreate(false);
            reload();
          }}
        />
      )}

      {data.length === 0 ? (
        <EmptyState title="No target servers configured yet." />
      ) : (
        <div className="space-y-3">
          {data.map((server) => (
            <Card key={server.id}>
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div>
                  <button type="button" onClick={() => setExpandedId((id) => (id === server.id ? null : server.id))} className="text-sm font-semibold text-slate-900 hover:underline">
                    {server.name}
                  </button>
                  <p className="mt-0.5 text-xs text-slate-500">
                    {server.hostname ?? 'No hostname configured'} {server.description && `· ${server.description}`}
                  </p>
                </div>
                <div className="flex items-center gap-2">
                  <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${server.isActive ? 'bg-emerald-100 text-emerald-700' : 'bg-slate-200 text-slate-600'}`}>
                    {server.isActive ? 'Active' : 'Inactive'}
                  </span>
                  {can(Permissions.TargetServersManage) && (
                    <ActionButton
                      label={server.isActive ? 'Deactivate' : 'Activate'}
                      variant={server.isActive ? 'danger' : 'secondary'}
                      confirmLabel={server.isActive ? 'Confirm deactivate' : undefined}
                      onAction={() =>
                        TargetServersApi.update(server.id, {
                          name: server.name,
                          description: server.description,
                          hostname: server.hostname,
                          isActive: !server.isActive,
                        })
                      }
                      onSuccess={reload}
                    />
                  )}
                </div>
              </div>

              {expandedId === server.id && (
                <div className="mt-3 border-t border-slate-100 pt-3">
                  <p className="text-xs font-medium text-slate-600">Allowed deployment roots</p>
                  {server.allowedDeploymentRoots.length === 0 ? (
                    <p className="mt-1 text-xs text-slate-400">
                      No allowed deployment roots configured yet — every legacy-mode application environment's deployment path on this server must resolve
                      under one of these.
                    </p>
                  ) : (
                    <ul className="mt-1 space-y-1">
                      {server.allowedDeploymentRoots.map((root) => (
                        <li key={root.id} className="flex items-center gap-2 text-xs">
                          <span className={`font-mono ${root.isActive ? 'text-slate-700' : 'text-slate-400 line-through'}`}>{root.rootPath}</span>
                          {root.description && <span className="text-slate-400">— {root.description}</span>}
                        </li>
                      ))}
                    </ul>
                  )}
                  {can(Permissions.TargetServersManage) && <AddAllowedRootForm targetServerId={server.id} onAdded={reload} />}
                </div>
              )}
            </Card>
          ))}
        </div>
      )}
    </div>
  );
}

function AddAllowedRootForm({ targetServerId, onAdded }: { targetServerId: string; onAdded: () => void }) {
  const [rootPath, setRootPath] = useState('');

  return (
    <div className="mt-2 flex flex-wrap items-center gap-2">
      <input
        value={rootPath}
        onChange={(e) => setRootPath(e.target.value)}
        placeholder="/mnt/data/apps"
        className="w-64 rounded-md border border-slate-300 px-2 py-1 text-xs focus:border-slate-500 focus:outline-none"
      />
      <ActionButton
        label="Add allowed root"
        disabled={!rootPath.trim()}
        onAction={() => TargetServersApi.addAllowedRoot(targetServerId, { rootPath: rootPath.trim(), description: null })}
        onSuccess={() => {
          setRootPath('');
          onAdded();
        }}
      />
    </div>
  );
}

function TargetServerForm({ onSubmitted }: { onSubmitted: () => void }) {
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [hostname, setHostname] = useState('');

  return (
    <Card className="mb-4">
      <h2 className="text-sm font-semibold text-slate-900">New target server</h2>
      <div className="mt-3 grid gap-3 sm:grid-cols-2">
        <label className="block text-xs font-medium text-slate-600">
          Name
          <input value={name} onChange={(e) => setName(e.target.value)} className={inputClass} />
        </label>
        <label className="block text-xs font-medium text-slate-600">
          Hostname
          <input value={hostname} onChange={(e) => setHostname(e.target.value)} className={inputClass} placeholder="Optional" />
        </label>
        <label className="block text-xs font-medium text-slate-600 sm:col-span-2">
          Description
          <input value={description} onChange={(e) => setDescription(e.target.value)} className={inputClass} placeholder="Optional" />
        </label>
      </div>
      <div className="mt-3">
        <ActionButton
          label="Create target server"
          disabled={!name.trim()}
          disabledReason="Name is required."
          onAction={() =>
            TargetServersApi.create({ name: name.trim(), description: description.trim() || null, hostname: hostname.trim() || null })
          }
          onSuccess={onSubmitted}
        />
      </div>
    </Card>
  );
}

const inputClass = 'mt-1 w-full rounded-md border border-slate-300 px-2.5 py-1.5 text-sm focus:border-slate-500 focus:outline-none';
