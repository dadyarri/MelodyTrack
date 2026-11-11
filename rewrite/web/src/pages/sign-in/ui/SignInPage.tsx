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
      <section className="sign-in-story" aria-label="MelodyTrack — музыкальная школа">
        <Brand />
        <div className="story-content">
          <span className="eyebrow">В РИТМЕ ВАШЕЙ ШКОЛЫ</span>
          <h1>
            Больше музыки.
            <br />
            Меньше рутины.
          </h1>
          <p>Рабочее пространство, в котором всё начинается с человека.</p>
          <div className="staff-lines" aria-hidden="true">
            <span />
            <span />
            <span />
            <span />
            <span />
            <i className="note note-one" />
            <i className="note note-two" />
            <i className="note note-three" />
          </div>
        </div>
        <div className="story-footer">
          <span className="small-dot" /> Для тех, кто учит и учится
        </div>
      </section>

      <section className="sign-in-panel" aria-labelledby="sign-in-heading">
        <header className="sign-in-topbar">
          <span className="mobile-brand">
            <Brand />
          </span>
          <Button
            type="text"
            className="appearance-toggle"
            icon={<Icon name={appearance === "light" ? "moon" : "sun"} />}
            onClick={onToggleAppearance}
            aria-label={appearance === "light" ? "Включить тёмную тему" : "Включить светлую тему"}
          />
        </header>

        <div className="sign-in-content">
          <div className="sign-in-intro">
            <span className="eyebrow">РАБОЧЕЕ ПРОСТРАНСТВО</span>
            <h2 id="sign-in-heading">С возвращением</h2>
            <p>Войдите в свой аккаунт сотрудника школы.</p>
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
              <Icon name="book" /> Нужна помощь со входом?
            </button>
            {showHelp && (
              <div id="sign-in-help-content" className="help-copy">
                <p>
                  Используйте логин, который выбрали при регистрации. Если настроен второй фактор,
                  после пароля потребуется подтверждение.
                </p>
                <p>
                  При потере доступа обратитесь к суперпользователю школы. Никому не передавайте
                  пароль и коды подтверждения.
                </p>
              </div>
            )}
          </div>
        </div>

        <footer className="sign-in-footer">
          MelodyTrack <span>•</span> Музыкальная школа
        </footer>
      </section>
    </main>
  );
}
