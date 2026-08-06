import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const projectDirectory = dirname(fileURLToPath(import.meta.url));

export default defineConfig(({ mode }) => {
  const isApi = mode === 'api';
  const isBackground = mode === 'background';

  if (isApi) {
    return {
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
    };
  }

  return {
    plugins: isBackground ? [] : [react()],
    define: {
      'process.env.NODE_ENV': JSON.stringify('production'),
    },
    publicDir: isBackground ? false : 'public',
    build: {
      outDir: 'dist',
      emptyOutDir: !isBackground,
      sourcemap: false,
      minify: true,
      lib: {
        entry: resolve(
          projectDirectory,
          isBackground ? 'src/background/index.ts' : 'src/content/index.tsx',
        ),
        name: isBackground ? 'ShowWhereBackground' : 'ShowWhereContent',
        formats: ['iife'],
        fileName: () => (isBackground ? 'background.js' : 'content.js'),
      },
      rollupOptions: {
        output: {
          inlineDynamicImports: true,
        },
      },
    },
  };
});
