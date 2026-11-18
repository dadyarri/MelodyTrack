import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./app/App";
import { AppErrorBoundary } from "./app/AppErrorBoundary";
import { takeInvitationFromAddress } from "./shared/lib/invitation";
import "./app/styles.css";

const root = document.getElementById("root");
if (!root) throw new Error("Не найден корневой элемент приложения.");
const invitation = takeInvitationFromAddress();

createRoot(root).render(
  <StrictMode>
    <AppErrorBoundary>
      <App invitation={invitation} />
    </AppErrorBoundary>
  </StrictMode>,
);
