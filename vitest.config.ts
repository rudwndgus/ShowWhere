import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    environment: 'node',
    include: ['src/**/*.test.ts', 'services/**/*.test.ts', 'learning/**/*.test.ts'],
    exclude: ['node_modules/**', 'dist/**', '.search-profile/**'],
    clearMocks: true,
    restoreMocks: true,
  },
});
