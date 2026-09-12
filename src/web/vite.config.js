var _a;
import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
export default defineConfig({
    plugins: [react()],
    server: {
        port: 5173,
        host: "0.0.0.0",
        proxy: { "/api": (_a = process.env.VITE_API_PROXY) !== null && _a !== void 0 ? _a : "http://localhost:8080" }
    },
    test: { environment: "jsdom", setupFiles: "./src/test-setup.ts" }
});
