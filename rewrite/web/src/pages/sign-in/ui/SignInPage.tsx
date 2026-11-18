import { useState } from "react";
import { StaffSignInForm } from "@/features/staff-access";
import type { Appearance } from "@/shared/lib/appearance";
import { Brand } from "@/shared/ui/Brand";
import { Icon } from "@/shared/ui/Icon";
import { PaperButton } from "@/shared/ui/paper";
import styles from "./SignInPage.module.css";

export function SignInPage({
  appearance,
  onToggleAppearance,
}: {
  appearance: Appearance;
  onToggleAppearance: () => void;
}) {
  const [showHelp, setShowHelp] = useState(false);

  return (
    <main className={styles.layout}>
      <section className={styles.panel} aria-labelledby="sign-in-heading">
        <header className={styles.topbar}>
          <Brand />
          <PaperButton
            className={styles.appearanceToggle}
            icon={<Icon name={appearance === "light" ? "moon" : "sun"} />}
            onClick={onToggleAppearance}
            aria-label={appearance === "light" ? "Включить тёмную тему" : "Включить светлую тему"}
            title={appearance === "light" ? "Тёмная тема" : "Светлая тема"}
          />
        </header>
        <div className={styles.content}>
          <div className={styles.intro}>
            <h1 id="sign-in-heading">Вход</h1>
          </div>
          <StaffSignInForm />
          <div className={styles.help}>
            <button
              type="button"
              className={styles.textAction}
              aria-expanded={showHelp}
              aria-controls="sign-in-help-content"
              onClick={() => setShowHelp((shown) => !shown)}
            >
              Не получается войти?
            </button>
            {showHelp && (
              <div id="sign-in-help-content" className={styles.helpCopy}>
                <p>
                  За приглашением или восстановлением доступа обратитесь к суперпользователю школы.
                </p>
              </div>
            )}
          </div>
        </div>
      </section>
    </main>
  );
}
