import { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router";
import { useStaffSession } from "@/entities/session";
import { StaffInvitationForm } from "@/features/accept-staff-invitation";
import type { Appearance } from "@/shared/lib/appearance";
import { Brand } from "@/shared/ui/Brand";
import { Icon } from "@/shared/ui/Icon";
import { useErrorNotification } from "@/shared/ui/notifications";
import { PaperButton } from "@/shared/ui/paper";
import styles from "./StaffRegistrationPage.module.css";

export function StaffRegistrationPage({
  invitation,
  appearance,
  onToggleAppearance,
}: {
  invitation: string | null;
  appearance: Appearance;
  onToggleAppearance: () => void;
}) {
  const navigate = useNavigate();
  const { staff, signOut, status, error } = useStaffSession();
  const [mustSignOut, setMustSignOut] = useState(Boolean(staff));
  useErrorNotification(error?.message, { title: "Не удалось завершить текущую сессию" });
  useEffect(() => {
    if (!staff) setMustSignOut(false);
  }, [staff]);

  return (
    <main className={styles.layout}>
      <section className={styles.panel} aria-labelledby="registration-heading">
        <header className={styles.topbar}>
          <Brand />
          <PaperButton
            className={styles.appearanceToggle}
            icon={<Icon name={appearance === "light" ? "moon" : "sun"} />}
            onClick={onToggleAppearance}
            aria-label={appearance === "light" ? "Включить тёмную тему" : "Включить светлую тему"}
          />
        </header>
        <div className={styles.content}>
          <div className={styles.intro}>
            <h1 id="registration-heading">Принять приглашение</h1>
          </div>
          {!invitation ? (
            <p className={styles.warning} role="alert">
              <strong>Нужна ссылка приглашения</strong>
              <span>Откройте исходную ссылку, полученную от суперпользователя.</span>
            </p>
          ) : mustSignOut && staff ? (
            <div className={styles.signOut}>
              <p>Для регистрации другого аккаунта сначала выйдите из текущего.</p>
              <PaperButton onClick={() => void signOut()} loading={status === "authenticating"}>
                Выйти из текущего аккаунта
              </PaperButton>
            </div>
          ) : (
            <StaffInvitationForm
              invitation={invitation}
              onComplete={() => navigate("/sign-in", { replace: true })}
            />
          )}
          <div className={styles.help}>
            <Link to="/sign-in">{staff ? "Вернуться к работе" : "Ко входу"}</Link>
          </div>
        </div>
      </section>
    </main>
  );
}
