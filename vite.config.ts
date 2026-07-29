import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const projectDirectory = dirname(fileURLToPath(import.meta.url));

export default defineConfig(({ mode }) => {
  const isBackground = mode === 'background';

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
