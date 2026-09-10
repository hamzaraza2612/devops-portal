import { render, screen } from '@testing-library/react';
import type { ReactNode } from 'react';
import { describe, expect, it } from 'vitest';
import { Can } from './Common';
import { AuthContext, type AuthContextValue } from '../auth/AuthContext';
import { Permissions } from '../auth/permissions';

function renderWithPermissions(permissions: string[], children: ReactNode) {
  const value: AuthContextValue = {
    user: { id: '1', username: 'u', email: 'u@example.local', fullName: 'U', isActive: true, createdAt: '', lastLoginAt: null, roles: [], permissions },
    isLoading: false,
    error: null,
    login: async () => {},
    logout: () => {},
    can: (p: string) => permissions.includes(p),
  };
  return render(<AuthContext.Provider value={value}>{children}</AuthContext.Provider>);
}

describe('Can', () => {
  it('renders children when the user holds the permission', () => {
    renderWithPermissions([Permissions.DeploymentsDeployDev], <Can permission={Permissions.DeploymentsDeployDev}>Deploy button</Can>);
    expect(screen.getByText('Deploy button')).toBeInTheDocument();
  });

  it('renders nothing when the user lacks the permission', () => {
    renderWithPermissions([], <Can permission={Permissions.DeploymentsDeployProduction}>Deploy to Production</Can>);
    expect(screen.queryByText('Deploy to Production')).not.toBeInTheDocument();
  });
});
