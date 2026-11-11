import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import "antd/dist/reset.css";
import { App } from "./app/App";
import { AppErrorBoundary } from "./app/AppErrorBoundary";
import "./app/styles.css";

const root = document.getElementById("root");
if (!root) throw new Error("Не найден корневой элемент приложения.");

createRoot(root).render(
  <StrictMode>
    <AppErrorBoundary>
      <App />
    </AppErrorBoundary>
  </StrictMode>,
);
