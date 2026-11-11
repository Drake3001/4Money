import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { defineConfig, loadEnv } from 'vite'

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  const proxy = {
    '/api': { target: env.API_PROXY_TARGET || 'http://localhost:5283', changeOrigin: true },
  }
  return {
    plugins: [react(), tailwindcss()],
    server: {
      proxy,
      watch: {
        usePolling: env.FRONTEND_USE_POLLING === 'true',
        interval: 500,
      },
    },
    preview: { proxy },
  }
})
