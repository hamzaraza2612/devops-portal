import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { TargetServersPage } from './TargetServersPage';
import { TargetServersApi } from '../../api/endpoints';
import { AuthContext, type AuthContextValue } from '../../auth/AuthContext';
import { Permissions } from '../../auth/permissions';
import { SshAuthMethod, type TargetServerDto } from '../../types/api';

vi.mock('../../api/endpoints', () => ({
  TargetServersApi: { list: vi.fn() },
}));

function authValue(): AuthContextValue {
  return {
    user: {
      id: '1', username: 'u', email: 'u@example.local', fullName: 'U', isActive: true, isAdmin: false,
      canApproveProduction: false, environmentAccess: [], createdAt: '', lastLoginAt: null, roles: [],
      permissions: [Permissions.TargetServersManage],
    },
    isLoading: false,
    error: null,
    login: async () => {},
    logout: () => {},
    can: (p: string) => p === Permissions.TargetServersManage,
  };
}

const server: TargetServerDto = {
  id: 'server-dev', name: 'dev', description: null, hostname: '192.168.10.43', sshPort: 22,
  sshUsername: 'root', sshAuthMethod: SshAuthMethod.Password, hasSshCredential: true, hasSshPassphrase: false,
  isActive: true, createdAt: '2025-01-01T00:00:00Z', allowedDeploymentRoots: [],
};

describe('TargetServersPage — Allowed deployment roots discoverability', () => {
  it('shows "Add allowed root" when clicking Edit, without requiring a separate click on the server name', async () => {
    // Regression test: "Allowed deployment roots" used to be gated behind a
    // completely separate toggle (clicking the server's own name) from the
    // "Edit" button — a real admin who only clicked Edit (the obvious way to
    // configure a server) never saw it at all, reported live.
    vi.mocked(TargetServersApi.list).mockResolvedValue([server]);
    const user = userEvent.setup();
    render(
      <AuthContext.Provider value={authValue()}>
        <TargetServersPage />
      </AuthContext.Provider>,
    );

    expect(screen.queryByText('Allowed deployment roots')).not.toBeInTheDocument();

    await user.click(await screen.findByRole('button', { name: 'Edit' }));

    expect(await screen.findByText('Allowed deployment roots')).toBeInTheDocument();
    expect(screen.getByPlaceholderText('/mnt/data/apps')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Add allowed root' })).toBeInTheDocument();
  });
});
