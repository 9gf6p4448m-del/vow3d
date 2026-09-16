import { defineConfig } from 'vite';

export default defineConfig({
  base: './',
  build: {
    assetsDir: 'game-assets'
  },
  server: {
    host: '0.0.0.0',
    port: 5173
  }
});
