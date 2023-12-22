import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

const proxyPort = process.env.SERVER_PROXY_PORT || "5000";
const proxyTarget = "http://localhost:" + proxyPort;

// https://vitejs.dev/config/
export default defineConfig({
    plugins: [react()],
    build: {
        outDir: "../../deploy/public",
    },
    server: {
        port: 8080,
        proxy: {
            // redirect requests that start with /api/ to the server on port 5000
            "/api/": {
                target: proxyTarget,
                changeOrigin: true,
            }
        }
    },
    //aliases added to resolve issue with Feliz.AgGrid using old ag-grid-community package
    //which is not compatible with React 18. In newer version of ag-grid-community the styles
    //have been moved (see 1) and you may need to create the files for (see 1)
    resolve: {
        alias: [
            //1
            {
                find: "ag-grid-community/dist/styles/ag-theme-alpine-dark.css",
                replacement: "ag-grid-community/custom/ag-theme-alpine-dark.css"
            },
            //1
            {
                find: "ag-grid-community/dist/styles/ag-theme-balham-dark.css",
                replacement: "ag-grid-community/custom/ag-theme-balham-dark.css"
            },
            //3
            {
                find: "ag-grid-community/dist/styles/",
                replacement: "ag-grid-community/styles/"
            },
        ]
    }
});