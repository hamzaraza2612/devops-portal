import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApplicationsListPage } from './ApplicationsListPage';
import { ApplicationsApi, DeploymentsApi, RepositoriesApi } from '../api/endpoints';
import { AuthContext, type AuthContextValue } from '../auth/AuthContext';
import { Permissions } from '../auth/permissions';
import { DeploymentMode, RepositoryProvider, type RepositoryDto } from '../types/api';

vi.mock('../api/endpoints', () => ({
  ApplicationsApi: { list: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn() },
  DeploymentsApi: { list: vi.fn() },
  RepositoriesApi: { list: vi.fn() },
}));

function authValue(permissions: string[]): AuthContextValue {
  return {
    user: {
      id: '1', username: 'u', email: 'u@example.local', fullName: 'U', isActive: true, isAdmin: false,
      canApproveProduction: false, environmentAccess: [], createdAt: '', lastLoginAt: null, roles: [], permissions,
    },
    isLoading: false,
    error: null,
    login: async () => {},
    logout: () => {},
    can: (p: string) => permissions.includes(p),
  };
}

function renderPage(permissions: string[]) {
  return render(
    <MemoryRouter>
      <AuthContext.Provider value={authValue(permissions)}>
        <ApplicationsListPage />
      </AuthContext.Provider>
    </MemoryRouter>,
  );
}

const repository: RepositoryDto = {
  id: 'repo-1', name: 'loop', url: 'https://gitlab.techbey.pk/release-management/application_releases/loop.git', provider: RepositoryProvider.GitLab,
  description: null, defaultBranch: 'main', username: null, hasAccessToken: true, accessTokenEnvVarName: null,
  isActive: true, createdAt: '2026-01-01T00:00:00Z',
};

describe('ApplicationsListPage — New application', () => {
  beforeEach(() => {
    vi.mocked(ApplicationsApi.list).mockResolvedValue([]);
    vi.mocked(DeploymentsApi.list).mockResolvedValue([]);
    vi.mocked(RepositoriesApi.list).mockResolvedValue([repository]);
  });

  /** Regression test: the portal used to require a separate "Discover from
   * repository" scan (and a per-folder "not deployable" verdict) before an
   * application could be created — real feedback was that the developer's
   * actual workflow is one dedicated Git repository per application (a repo
   * link handed off per app, e.g. .../application_releases/loop.git), so
   * that scan step was removed. Creating an application is now a single,
   * always-visible form: just Name/Slug/Repository. */
  it('creates an application directly, with no discovery step in the way', async () => {
    const user = userEvent.setup();
    renderPage([Permissions.ApplicationsManage]);

    expect(screen.queryByRole('button', { name: 'Discover from repository' })).not.toBeInTheDocument();

    await user.click(await screen.findByRole('button', { name: 'New Application' }));
    await user.type(screen.getByLabelText('Name'), 'Loop');
    await user.type(screen.getByLabelText('Slug'), 'loop');
    await user.selectOptions(screen.getByLabelText('Repository'), 'repo-1');
    await user.click(screen.getByRole('button', { name: 'Create application' }));

    expect(ApplicationsApi.create).toHaveBeenCalledWith(
      expect.objectContaining({ name: 'Loop', slug: 'loop', repositoryId: 'repo-1', deploymentMode: DeploymentMode.LegacyFilesystem }),
    );
  });
});
