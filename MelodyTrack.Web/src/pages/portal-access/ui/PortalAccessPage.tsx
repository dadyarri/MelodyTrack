import { Button, Result } from "antd";
import { Link, useSearchParams } from "react-router";

export function PortalAccessPage() {
  const [searchParams] = useSearchParams();
  const reason = searchParams.get("reason");
  const title = reason === "session" ? "Войдите по личной ссылке" : "Не удалось открыть кабинет";
  const description =
    reason === "rate-limit"
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
          <Link to="/login">
            <Button>Вход для сотрудников</Button>
          </Link>
        }
      />
    </main>
  );
}
