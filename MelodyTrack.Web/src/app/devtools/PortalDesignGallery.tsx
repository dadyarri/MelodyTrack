import { useState } from "react";

import { PortalCourseCard } from "@/entities/course";
import { PortalHomeworkCard } from "@/entities/homework";
import { PortalLeaderboard } from "@/entities/leaderboard";
import { PortalTrainerCard } from "@/entities/trainer";
import { AppError } from "@/shared/api";
import {
  RpgApiError,
  RpgBadge,
  RpgButton,
  RpgDialog,
  RpgDivider,
  RpgFrame,
  RpgIcon,
  RpgLevelProgress,
  RpgNavigation,
  RpgNavItem,
  RpgPanel,
  RpgProgress,
  RpgState,
  RpgTheme,
} from "@/shared/ui/rpg";
import { PortalHeader } from "@/widgets/portal-header";

import styles from "./PortalDesignGallery.module.css";

const sampleError = new AppError("Не удалось загрузить свиток. Попробуйте ещё раз.", {
  kind: "http",
  status: 503,
  problem: {
    type: "about:blank",
    title: "Свиток временно недоступен",
    status: 503,
    instance: "",
    code: "",
    errors: [],
    traceId: "00000000000000000000000000000022",
  },
});

const navigation = [
  { href: "#portal-content", label: "Главная", icon: "flower" },
  { href: "#courses", label: "Курсы", icon: "scroll" },
  { href: "#homework", label: "Домашняя работа", icon: "quill" },
  { href: "#trainers", label: "Тренажёры", icon: "guitar" },
  { href: "#leaderboard", label: "Зал славы", icon: "laurel" },
] as const;

export function PortalDesignGallery() {
  const [active, setActive] = useState<string>("#portal-content");
  const [dialog, setDialog] = useState<string | null>(null);
  const [completed, setCompleted] = useState(false);
  const [showError, setShowError] = useState(true);
  const alias = "Сияющий Бард";

  return (
    <RpgTheme className={styles.page}>
      <div className={styles.galleryNotice}>Атлас компонентов · Тёплое фэнтези · демонстрационные данные</div>
      <RpgFrame className={styles.sheet}>
        <PortalHeader
          alias={alias}
          navigation={
            <RpgNavigation>
              {navigation.map((item) => (
                <a
                  key={item.href}
                  href={item.href}
                  aria-current={active === item.href ? "location" : undefined}
                  onClick={() => {
                    setActive(item.href);
                  }}
                >
                  <RpgNavItem icon={item.icon}>{item.label}</RpgNavItem>
                </a>
              ))}
            </RpgNavigation>
          }
        />

        <main id="portal-content" tabIndex={-1} className={styles.content}>
          <section className={styles.hero} aria-labelledby="gallery-title">
            <div>
              <p className={styles.eyebrow}>Твоя маленькая гильдия · Большое приключение</p>
              <h1 id="gallery-title">
                Твоя музыка.
                <br />
                Твоё приключение.
              </h1>
              <p className={styles.intro}>Открой новую мелодию, загляни в мастерскую и добавь немного магии в обычный день.</p>
              <div className={styles.actions}>
                <RpgButton
                  variant="primary"
                  shape="scroll"
                  onClick={() => {
                    setDialog("Добро пожаловать в гильдию");
                  }}
                >
                  <RpgIcon name="pick" />
                  Открыть свиток
                </RpgButton>
                <a href="#components">Изучить компоненты</a>
              </div>
            </div>
            <RpgPanel className={styles.heroNote} surface="parchment">
              <div className={styles.noteHeading}>
                <RpgIcon name="quill" />
                <RpgBadge>В своём ритме</RpgBadge>
              </div>
              <h3>Начни с одной мелодии</h3>
              <p>Пара знакомых аккордов, любимая гитара и немного времени для себя.</p>
              <div className={styles.noteFooter}>
                <RpgIcon name="flower" />
                <span>Маленькие шаги тоже ведут вперёд</span>
              </div>
            </RpgPanel>
            <div className={styles.heroDivider}>
              <RpgDivider />
            </div>
          </section>

          <section aria-labelledby="patterns-title" className={styles.section}>
            <div className={styles.sectionHeading}>
              <p className={styles.eyebrow}>I · Страницы пути</p>
              <h2 id="patterns-title">Знания обретают форму</h2>
            </div>
            <div className={styles.cards}>
              <div id="courses">
                <PortalCourseCard
                  title="Первые аккорды"
                  description="Знакомство с гитарой: от открытых струн до первой мелодии."
                  progress={{ completed: 3, total: 8, label: "Изучено тем" }}
                  action={
                    <RpgButton
                      onClick={() => {
                        setDialog("Книга знаний");
                      }}
                    >
                      Заглянуть в книгу
                    </RpgButton>
                  }
                />
              </div>
              <div id="homework">
                <PortalHomeworkCard
                  title="Мелодия для тихого вечера"
                  description="Соедините знакомые аккорды и найдите ровное, спокойное звучание."
                  status={completed ? "Практика завершена" : "Ждёт вашей практики"}
                  complete={completed}
                  action={
                    <RpgButton
                      variant="primary"
                      onClick={() => {
                        setCompleted(!completed);
                      }}
                    >
                      {completed ? "Повторить пример" : "Завершить пример"}
                    </RpgButton>
                  }
                />
              </div>
              <div id="trainers">
                <PortalTrainerCard
                  title="В поисках ритма"
                  description="Слушайте пульс и позвольте руке следовать за ним."
                  detail="Ритм · 4/4 · 60 BPM"
                  action={
                    <RpgButton
                      onClick={() => {
                        setDialog("Мастерская музыканта");
                      }}
                    >
                      Осмотреть мастерскую
                    </RpgButton>
                  }
                />
              </div>
            </div>
            {completed ? (
              <RpgState kind="success" title="Хорошая работа, музыкант">
                Состояние завершения остаётся понятным и без анимации.
              </RpgState>
            ) : null}
          </section>

          <section id="leaderboard" aria-labelledby="leaderboard-title" className={styles.section}>
            <div className={styles.sectionHeading}>
              <p className={styles.eyebrow}>II · Имена на пергаменте</p>
              <h2 id="leaderboard-title">Зал славы</h2>
            </div>
            <PortalLeaderboard
              title="Летопись гильдии"
              description="Пример оформления списка. Результаты вымышлены."
              entries={[
                { id: "example-1", rank: 1, alias: "Серебряный Грифон", result: "980 очков" },
                { id: "example-2", rank: 2, alias, result: "940 очков", isCurrentPlayer: true },
                { id: "example-3", rank: 3, alias: "Звёздный Менестрель", result: "910 очков" },
              ]}
            />
          </section>

          <section id="components" aria-labelledby="components-title" className={styles.section}>
            <div className={styles.sectionHeading}>
              <p className={styles.eyebrow}>III · Инструменты переплётчика</p>
              <h2 id="components-title">Детали и состояния</h2>
            </div>
            <RpgPanel>
              <RpgLevelProgress level={3} value={420} max={1000} />
            </RpgPanel>
            <div className={styles.twoColumns}>
              <RpgPanel className={styles.stack}>
                <h3>Кнопки и печати</h3>
                <div className={styles.actions}>
                  <RpgButton
                    variant="primary"
                    onClick={() => {
                      setDialog("Основное действие");
                    }}
                  >
                    Продолжить
                  </RpgButton>
                  <RpgButton
                    onClick={() => {
                      setDialog("Второстепенное действие");
                    }}
                  >
                    Подробнее
                  </RpgButton>
                  <RpgButton
                    variant="danger"
                    onClick={() => {
                      setDialog("Подтверждение действия");
                    }}
                  >
                    Отменить
                  </RpgButton>
                  <RpgButton disabled>Пока недоступно</RpgButton>
                  <RpgButton pending pendingLabel="Сохраняем…">
                    Сохранить
                  </RpgButton>
                  <RpgButton
                    shape="plaque"
                    onClick={() => {
                      setDialog("Табличка гильдии");
                    }}
                  >
                    Перейти
                  </RpgButton>
                  <RpgButton
                    shape="ribbon"
                    onClick={() => {
                      setDialog("Лента раздела");
                    }}
                  >
                    Уроки
                  </RpgButton>
                  <RpgButton
                    shape="scroll"
                    onClick={() => {
                      setDialog("Развёрнутый свиток");
                    }}
                  >
                    Развернуть свиток
                  </RpgButton>
                </div>
                <div className={styles.actions}>
                  <RpgBadge>Новая запись</RpgBadge>
                  <RpgBadge tone="success">Завершено</RpgBadge>
                  <RpgBadge tone="warning">Требует внимания</RpgBadge>
                </div>
              </RpgPanel>
              <RpgPanel className={styles.stack}>
                <h3>Прогресс без спешки</h3>
                <RpgProgress label="Первый шаг" value={0} />
                <RpgProgress label="На полпути" value={50} />
                <RpgProgress label="Всё готово" value={100} />
                <p>Движение следует системной настройке уменьшения анимации.</p>
              </RpgPanel>
              <RpgState kind="loading" title="Перелистываем страницы…">
                Загрузка обозначена текстом; мерцание лишь дополняет его.
              </RpgState>
              <RpgState kind="empty" title="Чистая страница">
                Здесь появятся новые задания преподавателя.
              </RpgState>
            </div>
            {showError ? (
              <RpgApiError
                error={sampleError}
                onRetry={() => {
                  setShowError(false);
                }}
              />
            ) : (
              <RpgState
                kind="success"
                title="Свиток снова доступен"
                action={
                  <RpgButton
                    onClick={() => {
                      setShowError(true);
                    }}
                  >
                    Показать ошибку снова
                  </RpgButton>
                }
              >
                Демонстрация повторной попытки без сетевого запроса.
              </RpgState>
            )}
          </section>

          <section aria-labelledby="palette-title" className={styles.section}>
            <div className={styles.sectionHeading}>
              <p className={styles.eyebrow}>IV · Палитра мастерской</p>
              <h2 id="palette-title">Тёплые краски приключения</h2>
            </div>
            <div className={styles.swatches}>
              {["Орех", "Кожа", "Сливки", "Шалфей", "Слива", "Бронза"].map((label, index) => (
                <div key={label}>
                  <span className={styles.swatch} data-colour={index} />
                  <span>{label}</span>
                </div>
              ))}
            </div>
          </section>
        </main>
        <footer className={styles.footer}>
          <RpgDivider />
          <p>MelodyTrack · Атлас интерфейса</p>
          <p>
            SVG: Lorc / <a href="https://game-icons.net/">Game-icons.net</a> ·{" "}
            <a href="https://creativecommons.org/licenses/by/3.0/">CC BY 3.0</a>. Файлы без изменений; цвет и размер заданы стилями.
          </p>
          <p>
            <a href="https://game-icons.net/1x1/lorc/guitar.html">Гитара</a> ·{" "}
            <a href="https://game-icons.net/1x1/lorc/flower-twirl.html">Орнамент</a> ·{" "}
            <a href="https://game-icons.net/1x1/lorc/scroll-unfurled.html">Свиток</a> ·{" "}
            <a href="https://game-icons.net/1x1/lorc/quill-ink.html">Перо</a> ·{" "}
            <a href="https://game-icons.net/1x1/lorc/laurel-crown.html">Венок</a>
          </p>
          <p>
            Вензель Corner Flourish 3: Arvin61r58 / Openclipart, через{" "}
            <a href="https://freepngimg.com/svg/color/800080/123706-corner-flourish-3">FreePNGimg</a> ·{" "}
            <a href="https://creativecommons.org/publicdomain/zero/1.0/">CC0</a>.
          </p>
          <p>
            Медиатор: <a href="https://github.com/tabler/tabler-icons/blob/main/icons/outline/guitar-pick.svg">Tabler Icons</a>, © 2020–2026
            Paweł Kuna · <a href="https://github.com/tabler/tabler-icons/blob/main/LICENSE">MIT</a>.
          </p>
          <p>
            Свиток Scroll UI Container: Chloe Wolfe / Cairo Creative Studios /{" "}
            <a href="https://opengameart.org/content/scroll-ui-container">OpenGameArt</a> ·{" "}
            <a href="https://creativecommons.org/publicdomain/zero/1.0/">CC0</a>.
          </p>
        </footer>
      </RpgFrame>
      <RpgDialog
        open={dialog !== null}
        title={dialog ?? "Свиток"}
        onRequestClose={() => {
          setDialog(null);
        }}
        footer={
          <RpgButton
            variant="primary"
            onClick={() => {
              setDialog(null);
            }}
          >
            Вернуться к атласу
          </RpgButton>
        }
      >
        <div className={styles.stack}>
          <RpgIcon name="scroll" large />
          <p>Это пример диалога: тёплая фактура, бронзовая окантовка, спокойная анимация и явное действие для возвращения.</p>
          <p>Используйте Tab для перехода между кнопками и Escape для закрытия. После закрытия фокус вернётся к исходной кнопке.</p>
        </div>
      </RpgDialog>
    </RpgTheme>
  );
}
