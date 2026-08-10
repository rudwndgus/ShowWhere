import { describe, expect, it } from 'vitest';
import { repositoryRootEnvironmentPath } from './rootEnvironment';

describe('repository root environment policy', () => {
  it('resolves exactly one canonical root .env from source and bundled locations', () => {
    expect(repositoryRootEnvironmentPath('file:///C:/repo/services/api/src/server.ts').replaceAll('\\', '/'))
      .toBe('C:/repo/.env');
    expect(repositoryRootEnvironmentPath('file:///C:/repo/services/api/dist/server.js').replaceAll('\\', '/'))
      .toBe('C:/repo/.env');
  });
});
