import { defineConfig } from 'vite';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const projectDirectory = dirname(fileURLToPath(import.meta.url));

export default defineConfig({
  plugins: [],
  publicDir: false,
  build: {
    ssr: resolve(projectDirectory, 'services/api/src/server.ts'),
    target: 'node20',
    outDir: 'services/api/dist',
    emptyOutDir: true,
    sourcemap: false,
    minify: true,
    rollupOptions: {
      output: {
        entryFileNames: 'server.js',
        format: 'es',
      },
    },
  },
});
