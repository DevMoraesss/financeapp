import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    proxy: {
      // Em desenvolvimento a SPA chama /api/v1/... e o Vite encaminha para a API .NET.
      // Assim o navegador vê uma origem só e o CORS não atrapalha enquanto você codifica.
      // Em produção são domínios diferentes de verdade - aí vale o CORS de origem única
      // configurado no Program.cs (ADR-001, Decisão 4).
      '/api': {
        target: 'http://localhost:5080',
        changeOrigin: true,
      },
    },
  },
})
