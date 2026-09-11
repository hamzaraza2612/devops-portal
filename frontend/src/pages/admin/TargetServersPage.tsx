import { useState } from 'react';
import { TargetServersApi } from '../../api/endpoints';
import { ActionButton } from '../../components/ActionButton';
import { Card, EmptyState, ErrorBanner, LoadingSpinner, PageHeader } from '../../components/Common';
import { useAsyncData } from '../../hooks/useAsyncData';
import { Permissions } from '../../auth/permissions';
import { useAuth } from '../../auth/AuthContext';
import { describeError } from '../../api/client';
import { SshAuthMethod, type TargetServerConnectionTestResultDto, type TargetServerDto } from '../../types/api';

const authMethodLabel: Record<SshAuthMethod, string> = {
  [SshAuthMethod.PrivateKey]: 'SSH private key',
  [SshAuthMethod.Password]: 'Password',
};

export function TargetServersPage() {
  const { data, isLoading, error, reload } = useAsyncData(TargetServersApi.list, []);
  const { can } = useAuth();
  const [showCreate, setShowCreate] = useState(false);
  const [expandedId, setExpandedId] = useState<string | null>(null);

  if (isLoading) return <LoadingSpinner label="Loading servers…" />;
  if (error) return <ErrorBanner message={error} onDismiss={reload} />;
  if (!data) return null;

  return (
    <div>
      <PageHeader
        title="Servers"
        subtitle="Target servers the portal reaches over SSH — separate machines from the portal itself — that run application containers via Docker Compose."
        actions={
          can(Permissions.TargetServersManage) && (
            <button
              type="button"
              onClick={() => setShowCreate((v) => !v)}
              className="rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white hover:bg-slate-700"
            >
              {showCreate ? 'Cancel' : 'New Server'}
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
            <TargetServerCard
              key={server.id}
              server={server}
              canManage={can(Permissions.TargetServersManage)}
              expanded={expandedId === server.id}
              onToggle={() => setExpandedId((id) => (id === server.id ? null : server.id))}
              onChanged={reload}
            />
          ))}
        </div>
      )}
    </div>
  );
}

function TargetServerCard({
  server,
  canManage,
  expanded,
  onToggle,
  onChanged,
}: {
  server: TargetServerDto;
  canManage: boolean;
  expanded: boolean;
  onToggle: () => void;
  onChanged: () => void;
}) {
  const [testResult, setTestResult] = useState<TargetServerConnectionTestResultDto | null>(null);
  const [testError, setTestError] = useState<string | null>(null);
  const [isTesting, setIsTesting] = useState(false);
  const [showCredentialForm, setShowCredentialForm] = useState(false);
  const [credential, setCredential] = useState('');
  const [passphrase, setPassphrase] = useState('');

  async function runTest() {
    setIsTesting(true);
    setTestError(null);
    try {
      setTestResult(await TargetServersApi.testConnection(server.id));
    } catch (err) {
      setTestError(describeError(err));
      setTestResult(null);
    } finally {
      setIsTesting(false);
    }
  }

  return (
    <Card>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <button type="button" onClick={onToggle} className="text-sm font-semibold text-slate-900 hover:underline">
            {server.name}
          </button>
          <p className="mt-0.5 text-xs text-slate-500">
            {server.hostname ? `${server.hostname}:${server.sshPort}` : 'No hostname configured'} {server.description && `· ${server.description}`}
          </p>
        </div>
        <div className="flex items-center gap-2">
          <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${server.isActive ? 'bg-emerald-100 text-emerald-700' : 'bg-slate-200 text-slate-600'}`}>
            {server.isActive ? 'Active' : 'Inactive'}
          </span>
          {canManage && (
            <ActionButton
              label={server.isActive ? 'Deactivate' : 'Activate'}
              variant={server.isActive ? 'danger' : 'secondary'}
              confirmLabel={server.isActive ? 'Confirm deactivate' : undefined}
              onAction={() =>
                TargetServersApi.update(server.id, {
                  name: server.name,
                  description: server.description,
                  hostname: server.hostname,
                  sshPort: server.sshPort,
                  sshUsername: server.sshUsername,
                  sshAuthMethod: server.sshAuthMethod,
                  isActive: !server.isActive,
                })
              }
              onSuccess={onChanged}
            />
          )}
        </div>
      </div>

      <dl className="mt-3 grid gap-1 text-xs sm:grid-cols-2">
        <Row label="SSH user" value={server.sshUsername ?? '—'} />
        <Row label="Auth method" value={authMethodLabel[server.sshAuthMethod]} />
        <Row label="Credential" value={server.hasSshCredential ? 'Configured' : 'Not configured'} />
        {server.sshAuthMethod === SshAuthMethod.PrivateKey && (
          <Row label="Key passphrase" value={server.hasSshPassphrase ? 'Configured' : 'None'} />
        )}
      </dl>

      <div className="mt-3 flex flex-wrap items-center gap-2 border-t border-slate-100 pt-3">
        <button
          type="button"
          onClick={() => void runTest()}
          disabled={isTesting}
          className="rounded-md border border-slate-300 px-3 py-1.5 text-xs font-medium text-slate-700 hover:bg-slate-50 disabled:opacity-60"
        >
          {isTesting ? 'Testing…' : 'Test Connection'}
        </button>
        {canManage && (
          <button
            type="button"
            onClick={() => setShowCredentialForm((v) => !v)}
            className="rounded-md border border-slate-300 px-3 py-1.5 text-xs font-medium text-slate-700 hover:bg-slate-50"
          >
            {showCredentialForm ? 'Cancel' : 'Set SSH credential'}
          </button>
        )}
      </div>

      {testError && <p className="mt-2 text-xs text-red-600">{testError}</p>}
      {testResult && (
        <div className="mt-2 space-y-0.5 text-xs">
          <p className={`font-medium ${testResult.sshConnected ? 'text-emerald-700' : 'text-red-600'}`}>
            {testResult.sshConnected ? `ONLINE / SSH OK${testResult.authenticatedUser ? ` (as ${testResult.authenticatedUser})` : ''}` : 'OFFLINE / SSH FAILED'}
          </p>
          {testResult.sshConnected && (
            <>
              <p className={testResult.dockerAvailable ? 'text-emerald-700' : 'text-red-600'}>
                Docker {testResult.dockerAvailable ? `OK (${testResult.dockerVersion})` : 'NOT AVAILABLE'}
              </p>
              <p className={testResult.composeAvailable ? 'text-emerald-700' : 'text-red-600'}>
                Compose {testResult.composeAvailable ? `OK (${testResult.composeVersion})` : 'NOT AVAILABLE'}
              </p>
              {testResult.osInfo && <p className="text-slate-500">{testResult.osInfo}</p>}
            </>
          )}
          {!testResult.sshConnected && testResult.errorMessage && <p className="text-red-600">{testResult.errorMessage}</p>}
        </div>
      )}

      {showCredentialForm && canManage && (
        <div className="mt-3 space-y-2 border-t border-slate-100 pt-3">
          <label className="block text-xs font-medium text-slate-600">
            {server.sshAuthMethod === SshAuthMethod.PrivateKey ? 'SSH private key (PEM)' : 'SSH password'}
            {server.sshAuthMethod === SshAuthMethod.PrivateKey ? (
              <textarea
                value={credential}
                onChange={(e) => setCredential(e.target.value)}
                rows={4}
                className={`${inputClass} font-mono`}
                placeholder="-----BEGIN OPENSSH PRIVATE KEY-----"
              />
            ) : (
              <input type="password" value={credential} onChange={(e) => setCredential(e.target.value)} className={inputClass} />
            )}
          </label>
          <ActionButton
            label="Save credential"
            disabled={!credential.trim()}
            onAction={() => TargetServersApi.setSshCredential(server.id, { value: credential })}
            onSuccess={() => {
              setCredential('');
              onChanged();
            }}
          />

          {server.sshAuthMethod === SshAuthMethod.PrivateKey && (
            <div className="flex flex-wrap items-end gap-2 pt-2">
              <label className="block text-xs font-medium text-slate-600">
                Key passphrase (optional)
                <input type="password" value={passphrase} onChange={(e) => setPassphrase(e.target.value)} className={inputClass} />
              </label>
              <ActionButton
                label="Save passphrase"
                variant="secondary"
                onAction={() => TargetServersApi.setSshPassphrase(server.id, { value: passphrase || null })}
                onSuccess={() => {
                  setPassphrase('');
                  onChanged();
                }}
              />
            </div>
          )}
        </div>
      )}

      {expanded && (
        <div className="mt-3 border-t border-slate-100 pt-3">
          <p className="text-xs font-medium text-slate-600">Allowed deployment roots</p>
          {server.allowedDeploymentRoots.length === 0 ? (
            <p className="mt-1 text-xs text-slate-400">
              No allowed deployment roots configured yet — every legacy-mode application environment&apos;s deployment path on this server must
              resolve under one of these.
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
          {canManage && <AddAllowedRootForm targetServerId={server.id} onAdded={onChanged} />}
        </div>
      )}
    </Card>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between gap-3">
      <dt className="text-slate-500">{label}</dt>
      <dd className="font-medium text-slate-800">{value}</dd>
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
  const [sshPort, setSshPort] = useState('22');
  const [sshUsername, setSshUsername] = useState('');
  const [sshAuthMethod, setSshAuthMethod] = useState<SshAuthMethod>(SshAuthMethod.PrivateKey);

  return (
    <Card className="mb-4">
      <h2 className="text-sm font-semibold text-slate-900">New target server</h2>
      <div className="mt-3 grid gap-3 sm:grid-cols-2">
        <label className="block text-xs font-medium text-slate-600">
          Name
          <input value={name} onChange={(e) => setName(e.target.value)} className={inputClass} />
        </label>
        <label className="block text-xs font-medium text-slate-600">
          Hostname / IP
          <input value={hostname} onChange={(e) => setHostname(e.target.value)} className={inputClass} placeholder="10.0.0.5" />
        </label>
        <label className="block text-xs font-medium text-slate-600">
          SSH port
          <input value={sshPort} onChange={(e) => setSshPort(e.target.value)} className={inputClass} placeholder="22" />
        </label>
        <label className="block text-xs font-medium text-slate-600">
          SSH username
          <input value={sshUsername} onChange={(e) => setSshUsername(e.target.value)} className={inputClass} />
        </label>
        <label className="block text-xs font-medium text-slate-600">
          Authentication method
          <select
            value={sshAuthMethod}
            onChange={(e) => setSshAuthMethod(Number(e.target.value) as SshAuthMethod)}
            className={inputClass}
          >
            <option value={SshAuthMethod.PrivateKey}>SSH private key</option>
            <option value={SshAuthMethod.Password}>Password</option>
          </select>
        </label>
        <label className="block text-xs font-medium text-slate-600 sm:col-span-2">
          Description
          <input value={description} onChange={(e) => setDescription(e.target.value)} className={inputClass} placeholder="Optional" />
        </label>
      </div>
      <p className="mt-2 text-xs text-slate-400">
        After creating this server, use &ldquo;Set SSH credential&rdquo; to store the private key or password securely, then &ldquo;Test
        Connection&rdquo; to verify SSH/Docker/Compose availability.
      </p>
      <div className="mt-3">
        <ActionButton
          label="Create target server"
          disabled={!name.trim()}
          disabledReason="Name is required."
          onAction={() =>
            TargetServersApi.create({
              name: name.trim(),
              description: description.trim() || null,
              hostname: hostname.trim() || null,
              sshPort: Number(sshPort) || 22,
              sshUsername: sshUsername.trim() || null,
              sshAuthMethod,
            })
          }
          onSuccess={onSubmitted}
        />
      </div>
    </Card>
  );
}

const inputClass = 'mt-1 w-full rounded-md border border-slate-300 px-2.5 py-1.5 text-sm focus:border-slate-500 focus:outline-none';
