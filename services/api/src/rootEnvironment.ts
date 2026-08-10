import { config as loadDotenv } from 'dotenv';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export function repositoryRootEnvironmentPath(moduleUrl = import.meta.url): string {
  const moduleDirectory = path.dirname(fileURLToPath(moduleUrl));
  return path.resolve(moduleDirectory, '../../../.env');
}

export function loadRepositoryRootEnvironment(): void {
  loadDotenv({ path: repositoryRootEnvironmentPath(), override: false, quiet: true });
}
