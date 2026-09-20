import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";

export default defineConfig({
  plugins: [vue()],
  server: {
    port: 3000,
    proxy: {
      "/api": process.env.VITE_API_TARGET || "http://127.0.0.1:8010",
      "/v1": process.env.VITE_API_TARGET || "http://127.0.0.1:8010"
    }
  }
});
