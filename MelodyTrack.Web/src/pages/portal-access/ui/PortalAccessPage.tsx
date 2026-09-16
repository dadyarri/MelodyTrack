import { Button, Result } from "antd";
import { Link, useSearchParams } from "react-router";

export function PortalAccessPage({ signedOut = false }: { signedOut?: boolean }) {
  const [searchParams] = useSearchParams();
  const reason = searchParams.get("reason");
  const title = signedOut ? "Вы вышли из кабинета" : reason === "session" ? "Войдите по личной ссылке" : "Не удалось открыть кабинет";
  const description = signedOut
    ? "Спасибо за визит. Чтобы вернуться, снова откройте личную ссылку, которую прислал преподаватель."
    : reason === "rate-limit"
      ? "Слишком много попыток входа. Подождите минуту и снова откройте личную ссылку."
      : reason === "session"
        ? "Откройте личную ссылку, которую вам прислал администратор. Если ссылка потерялась, попросите администратора создать новую."
        : "Ссылка недействительна или была отключена. Попросите администратора прислать новую личную ссылку. Если сбой временный, попробуйте открыть вашу ссылку позже.";

  return (
    <main>
      <Result
        status="info"
        title={title}
        subTitle={description}
        extra={
          signedOut ? undefined : (
            <Link to="/login">
              <Button>Вход для сотрудников</Button>
            </Link>
          )
        }
      />
    </main>
  );
}
