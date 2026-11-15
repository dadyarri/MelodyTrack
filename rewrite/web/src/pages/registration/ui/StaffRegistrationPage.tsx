import { Alert, Button } from "antd";
import { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router";
import { useStaffSession } from "@/entities/session";
import { StaffInvitationForm } from "@/features/accept-staff-invitation";
import type { Appearance } from "@/shared/lib/appearance";
import { Brand } from "@/shared/ui/Brand";
import { Icon } from "@/shared/ui/Icon";
import { useErrorNotification } from "@/shared/ui/notifications";

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
    <main className="sign-in-layout">
      <section className="sign-in-panel registration-panel" aria-labelledby="registration-heading">
        <header className="sign-in-topbar">
          <Brand />
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
            <h1 id="registration-heading">Принять приглашение</h1>
          </div>
          {!invitation ? (
            <Alert
              type="warning"
              showIcon
              title="Нужна ссылка приглашения"
              description="Откройте исходную ссылку, полученную от суперпользователя."
            />
          ) : mustSignOut && staff ? (
            <div className="registration-form">
              <p>Для регистрации другого аккаунта сначала выйдите из текущего.</p>
              <Button onClick={() => void signOut()} loading={status === "authenticating"}>
                Выйти из текущего аккаунта
              </Button>
            </div>
          ) : (
            <StaffInvitationForm
              invitation={invitation}
              onComplete={() => navigate("/sign-in", { replace: true })}
            />
          )}
          <div className="sign-in-help">
            <Link to="/sign-in">{staff ? "Вернуться к работе" : "Ко входу"}</Link>
          </div>
        </div>
      </section>
    </main>
  );
}
