import { Button } from "antd";
import { useState } from "react";
import { StaffSignInForm } from "@/features/staff-access";
import type { Appearance } from "@/shared/lib/appearance";
import { Brand } from "@/shared/ui/Brand";
import { Icon } from "@/shared/ui/Icon";

export function SignInPage({
  appearance,
  onToggleAppearance,
}: {
  appearance: Appearance;
  onToggleAppearance: () => void;
}) {
  const [showHelp, setShowHelp] = useState(false);

  return (
    <main className="sign-in-layout">
      <section className="sign-in-panel" aria-labelledby="sign-in-heading">
        <header className="sign-in-topbar">
          <Brand />
          <Button
            type="text"
            className="appearance-toggle"
            icon={<Icon name={appearance === "light" ? "moon" : "sun"} />}
            onClick={onToggleAppearance}
            aria-label={appearance === "light" ? "Включить тёмную тему" : "Включить светлую тему"}
            title={appearance === "light" ? "Тёмная тема" : "Светлая тема"}
          />
        </header>

        <div className="sign-in-content">
          <div className="sign-in-intro">
            <h1 id="sign-in-heading">Вход</h1>
          </div>

          <StaffSignInForm />

          <div className="sign-in-help">
            <button
              type="button"
              className="text-action"
              aria-expanded={showHelp}
              aria-controls="sign-in-help-content"
              onClick={() => setShowHelp((shown) => !shown)}
            >
              Не получается войти?
            </button>
            {showHelp && (
              <div id="sign-in-help-content" className="help-copy">
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
