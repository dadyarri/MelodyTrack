import "dayjs/locale/ru";
import "@/app/styles/index.css";
import "@/app/styles/mobile-compatibility.css";

import dayjs from "dayjs";
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";

import { App } from "@/app/entrypoint";
import { ThemeProvider } from "@/shared/config";

dayjs.locale("ru");

const rootElement = document.getElementById("root");

if (!rootElement) {
  throw new Error("Root element '#root' was not found.");
}

createRoot(rootElement).render(
  <StrictMode>
    <ThemeProvider>
      <App />
    </ThemeProvider>
  </StrictMode>,
);
