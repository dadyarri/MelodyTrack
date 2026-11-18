import { Toaster } from "sonner";
import type { Appearance } from "@/shared/lib/appearance";
import styles from "./notifications.module.css";

export function Notifications({ appearance }: { appearance: Appearance }) {
  return (
    <Toaster
      className={styles.notifications ?? ""}
      theme={appearance}
      position="bottom-right"
      closeButton
      expand
      containerAriaLabel="Уведомления"
      toastOptions={{ closeButtonAriaLabel: "Закрыть уведомление" }}
      offset={{ bottom: "max(24px, env(safe-area-inset-bottom))", right: "24px" }}
      mobileOffset={{
        bottom: "max(16px, env(safe-area-inset-bottom))",
        left: "max(16px, env(safe-area-inset-left))",
        right: "max(16px, env(safe-area-inset-right))",
      }}
    />
  );
}
