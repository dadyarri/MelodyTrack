import { Button } from "antd";
import { Link } from "react-router";
import { useEmployeeTraining } from "@/features/employee-training";
import { Icon } from "@/shared/ui/Icon";

export function WorkspacePage() {
  const { progress, openTraining, openArticle } = useEmployeeTraining();
  const completed = progress?.demoStep === "completed";

  return (
    <div className="workspace-page">
      <div className="page-heading">
        <span className="eyebrow">НАЧНЁМ С ГЛАВНОГО</span>
        <h1>Добро пожаловать в школу</h1>
        <p>Небольшое знакомство поможет уверенно чувствовать себя в рабочем пространстве.</p>
      </div>

      <section className="welcome-banner" aria-labelledby="welcome-banner-title">
        <div>
          <span className="section-kicker">СВОЙ РИТМ РАБОТЫ</span>
          <h2 id="welcome-banner-title">
            Всё начинается
            <br />с уверенного первого шага.
          </h2>
          <p>
            Узнайте, как устроен вход, потренируйтесь завершать сессию и возвращайтесь к инструкции
            в любой момент.
          </p>
          <Button type="primary" size="large" onClick={openTraining}>
            {completed ? "Повторить тренировку" : "Открыть тренировку"}
            <Icon name="arrow" />
          </Button>
        </div>
        <div className="welcome-illustration" aria-hidden="true">
          <span />
          <span />
          <span />
          <i>♪</i>
        </div>
      </section>

      <div className="workspace-card-grid">
        <section className="surface-card">
          <div className="card-icon">
            <Icon name={completed ? "check" : "book"} />
          </div>
          <span className="card-overline">ПЕРВАЯ ТРЕНИРОВКА</span>
          <h2>Вход и рабочая сессия</h2>
          <p>
            {completed
              ? "Вы прошли знакомство. Практику можно повторить, когда понадобится."
              : "Попробуйте вход и выход на учебном примере. Реальные операции школы не затрагиваются."}
          </p>
          <span className={completed ? "completion-label complete" : "completion-label"}>
            {completed ? "Пройдено" : progress?.deferred ? "Отложено" : "Знакомство с системой"}
          </span>
        </section>

        <section className="surface-card">
          <div className="card-icon muted">
            <Icon name="book" />
          </div>
          <span className="card-overline">ПОМОЩЬ ВСЕГДА РЯДОМ</span>
          <h2>Инструкция под рукой</h2>
          <p>Короткие объяснения про вход, второй фактор и завершение сессии. В своём темпе.</p>
          <button className="text-action" type="button" onClick={openArticle}>
            Прочитать инструкцию <Icon name="arrow" />
          </button>
        </section>
      </div>

      <div className="workspace-note">
        <Icon name="book" />
        <p>
          Статьи и тренировки собраны в <Link to="/learning">журнале обучения</Link>. К ним можно
          вернуться в любой момент.
        </p>
      </div>
    </div>
  );
}
