import { useState } from "react";
import {
  DataView as CollectionDataView,
  type DataViewColumn,
  type DataViewSort,
} from "@/shared/ui/data-view";
import { PaperAccordion, PaperButton, PaperSelect } from "@/shared/ui/paper";
import {
  clientDirectoryExerciseComplete,
  type DemoClient,
  type DemoClientQuery,
  selectDemoClients,
} from "../model/clientDirectoryDemo";
import styles from "./client-directory-training.module.css";

export type ClientTrainingTopic = "search" | "sorting" | "card";
export const clientTrainingTitles: Record<ClientTrainingTopic, string> = {
  search: "Поиск и фильтры",
  sorting: "Сортировка таблицы",
  card: "Карточка клиента",
};
const initialQuery: DemoClientQuery = {
  search: "",
  source: "all",
  archive: "all",
  direction: "asc",
};
const columns: DataViewColumn<DemoClient>[] = [
  { key: "name", title: "ФИО", sortable: true, render: (client) => client.name },
  {
    key: "archive",
    title: "Статус",
    render: (client) => (client.archived ? "Архив" : ""),
  },
];

export function ClientDirectoryExercise({
  topic,
  onComplete,
  onStartTraining,
}: {
  topic: ClientTrainingTopic;
  onComplete: () => void;
  onStartTraining: (topic: ClientTrainingTopic) => void;
}) {
  const startingQuery: DemoClientQuery =
    topic === "sorting"
      ? { search: "Иванов Антон", source: "recommendation", archive: "active", direction: "asc" }
      : { ...initialQuery, direction: "desc" };
  const [query, setQuery] = useState<DemoClientQuery>(startingQuery);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [completed, setCompleted] = useState(false);
  const [attempted, setAttempted] = useState(false);
  const clients = selectDemoClients(query);

  function changeQuery(change: Partial<DemoClientQuery>) {
    setQuery((previous) => ({ ...previous, ...change }));
    setPage(1);
    setAttempted(false);
  }

  function checkResult() {
    if (clientDirectoryExerciseComplete(query, clients)) {
      setCompleted(true);
      onComplete();
    } else setAttempted(true);
  }

  const sort: DataViewSort[] = [{ key: "name", direction: query.direction }];

  if (topic === "card") return <ClientCardPractice />;

  return (
    <div className={styles.exercise}>
      <p className={styles.demoNote}>
        <span>Учебные клиенты</span> · Настоящие данные не меняются.
      </p>
      <p>
        {topic === "search"
          ? "Введите полное ФИО «Иванов Антон» и выберите источник «Рекомендация». Оставьте только тех, кто в работе. В результате должны остаться Иванов Артём и Иванов Антон: поиск допускает до трёх изменений в полном ФИО. Порядок от Я к А уже выбран."
          : "Поиск и фильтры уже настроены. Нажмите стрелку в заголовке ФИО и расположите клиентов от Я к А: сначала Иванов Артём, затем Иванов Антон."}
      </p>
      <CollectionDataView
        ariaLabel="Учебный список клиентов"
        columns={
          topic === "search" ? columns.map((column) => ({ ...column, sortable: false })) : columns
        }
        items={clients.slice((page - 1) * pageSize, page * pageSize)}
        rowKey={(client) => client.id}
        total={clients.length}
        page={page}
        pageSize={pageSize}
        onPageChange={(nextPage, nextSize) => {
          setPage(nextPage);
          setPageSize(nextSize);
        }}
        search={query.search}
        onSearchChange={(search) => changeQuery({ search })}
        sort={sort}
        onSortChange={(next) =>
          changeQuery({ direction: next.find((field) => field.key === "name")?.direction ?? "asc" })
        }
        activeFilterCount={Number(query.source !== "all") + Number(query.archive !== "all")}
        onResetFilters={() => {
          setQuery(startingQuery);
          setPage(1);
          setAttempted(false);
        }}
        filters={
          <div className={styles.filters}>
            <PaperSelect
              aria-label="Учебный источник"
              value={query.source}
              onValueChange={(source) =>
                changeQuery({ source: source as DemoClientQuery["source"] })
              }
              options={[
                { value: "all", label: "Все источники" },
                { value: "recommendation", label: "Рекомендация" },
                { value: "website", label: "Сайт" },
              ]}
            />
            <PaperSelect
              aria-label="Учебное состояние работы"
              value={query.archive}
              onValueChange={(archive) =>
                changeQuery({ archive: archive as DemoClientQuery["archive"] })
              }
              options={[
                { value: "all", label: "Все" },
                { value: "active", label: "В работе" },
                { value: "archived", label: "Архив" },
              ]}
            />
          </div>
        }
      />
      {attempted && (
        <p role="alert" className={styles.warning}>
          {topic === "search"
            ? "Проверьте поиск, источник и состояние работы."
            : "Проверьте порядок фамилий: от Я к А."}
        </p>
      )}
      {completed ? (
        <p role="status" className={styles.success}>
          Тренировка пройдена
        </p>
      ) : (
        <PaperButton variant="primary" onClick={checkResult}>
          Проверить результат
        </PaperButton>
      )}
      {completed && (
        <PaperButton
          onClick={() => {
            setCompleted(false);
            setQuery(startingQuery);
            setPage(1);
            setAttempted(false);
          }}
        >
          Повторить тренировку
        </PaperButton>
      )}
      {completed && topic === "search" && (
        <PaperButton variant="primary" onClick={() => onStartTraining("sorting")}>
          К сортировке
        </PaperButton>
      )}
      {completed && topic === "sorting" && (
        <PaperButton onClick={() => onStartTraining("search")}>К поиску и фильтрам</PaperButton>
      )}
    </div>
  );
}

function ClientCardPractice() {
  const [lastName, setLastName] = useState("Иванова");
  const [firstName, setFirstName] = useState("Анна");
  const [source, setSource] = useState("");
  const [contact, setContact] = useState("");
  const [primary, setPrimary] = useState(false);
  const [saved, setSaved] = useState(false);
  const [issue, setIssue] = useState(false);

  function saveDemoCard() {
    const valid = Boolean(
      lastName.trim() && firstName.trim() && source && (!contact.trim() || primary),
    );
    setIssue(!valid);
    setSaved(valid);
  }

  return (
    <section className={styles.cardPractice} aria-labelledby="client-card-practice-heading">
      <h3 id="client-card-practice-heading">Учебная карточка</h3>
      <p className={styles.demoNote}>Учебная карточка · Настоящие данные не меняются.</p>
      <p>
        Заполните имя, фамилию и источник. При добавлении телефона отметьте его основным, затем
        сохраните учебную карточку.
      </p>
      <div className={styles.cardFields}>
        <div className={styles.field}>
          <span>
            Фамилия
            <span className={styles.required} aria-hidden="true">
              *
            </span>
          </span>
          <input
            aria-label="Учебная фамилия"
            value={lastName}
            onChange={(event) => {
              setLastName(event.target.value);
              setSaved(false);
            }}
          />
        </div>
        <div className={styles.field}>
          <span>
            Имя
            <span className={styles.required} aria-hidden="true">
              *
            </span>
          </span>
          <input
            aria-label="Учебное имя"
            value={firstName}
            onChange={(event) => {
              setFirstName(event.target.value);
              setSaved(false);
            }}
          />
        </div>
        <div className={styles.field}>
          <span>
            Источник
            <span className={styles.required} aria-hidden="true">
              *
            </span>
          </span>
          <PaperSelect
            aria-label="Учебный источник карточки"
            value={source || null}
            placeholder="Выберите источник"
            onValueChange={(next) => {
              setSource(next);
              setSaved(false);
            }}
            options={[
              { value: "recommendation", label: "Рекомендация" },
              { value: "website", label: "Сайт" },
            ]}
          />
        </div>
        <div className={styles.field}>
          <span>Телефон</span>
          <input
            aria-label="Учебный телефон"
            value={contact}
            onChange={(event) => {
              setContact(event.target.value);
              setSaved(false);
            }}
          />
        </div>
        {contact.trim() ? (
          <label className={styles.radio}>
            <input
              type="radio"
              checked={primary}
              onChange={() => {
                setPrimary(true);
                setSaved(false);
              }}
            />
            Основной контакт
          </label>
        ) : null}
        {issue ? (
          <p role="alert" className={styles.warning}>
            Укажите имя, фамилию и источник; добавленный контакт отметьте основным.
          </p>
        ) : null}
        {saved ? (
          <p role="status" className={styles.success}>
            Учебная карточка сохранена.
          </p>
        ) : null}
        <PaperButton variant="primary" onClick={saveDemoCard}>
          Сохранить учебную карточку
        </PaperButton>
        <p className={styles.warning}>
          <strong>Архивирование временно недоступно</strong>
          <br />
          Оно будет включено после переноса зависимостей расписания и финансов.
        </p>
      </div>
    </section>
  );
}

export function ClientDirectoryArticle({
  onStartTraining,
}: {
  onStartTraining: (topic: ClientTrainingTopic) => void;
}) {
  return (
    <div className={styles.article}>
      <PaperAccordion
        items={[
          {
            value: "search",
            title: "Поиск и фильтры",
            content: (
              <div className={styles.topic}>
                <p>
                  Поиск находит клиентов по любой части ФИО с допуском до трёх вставок, удалений или
                  замен символов. Например, «багд» находит «Богданова Полина». Регистр и лишние
                  пробелы не учитываются. Для коротких запросов действует тот же допуск, поэтому
                  запросы до трёх символов совпадают с любым ФИО. Фильтры состояния работы и
                  источника можно сочетать.
                </p>
                <p>
                  Изменение поиска и фильтров возвращает на первую страницу. Пустой результат
                  означает, что условиям никто не соответствует; при ошибке загрузки используйте
                  повтор.
                </p>
                <PaperButton variant="primary" onClick={() => onStartTraining("search")}>
                  Тренировка поиска и фильтров
                </PaperButton>
              </div>
            ),
          },
          {
            value: "sorting",
            title: "Сортировка таблицы",
            content: (
              <div className={styles.topic}>
                <p>
                  Нажмите стрелку в заголовке колонки. Повторное нажатие меняет направление и затем
                  убирает поле из сортировки. Можно добавить несколько полей; номера у стрелок
                  обозначают их приоритет. Изменение сортировки возвращает на первую страницу.
                  Таблица по умолчанию показывает 10 строк; на телефоне она прокручивается по
                  горизонтали.
                </p>
                <PaperButton variant="primary" onClick={() => onStartTraining("sorting")}>
                  Тренировка сортировки
                </PaperButton>
              </div>
            ),
          },
          {
            value: "card",
            title: "Карточка клиента",
            content: (
              <div className={styles.topic}>
                <p>
                  Нажатие на клиента открывает правую панель с контактами и остальными данными. В
                  боковой панели видны профиль и список контактов. Если у сотрудника есть
                  эффективное право на запись карточек, он может создать или изменить ФИО, дату
                  рождения, источник и контакты. Для каждого используемого вида контакта требуется
                  один основной контакт; при удалении основного контакта выберите замену явно.
                  Источник обязателен, список контактов может быть пустым. Совпадение телефона или
                  почты показывает предупреждение и не блокирует сохранение.
                </p>
                <PaperButton variant="primary" onClick={() => onStartTraining("card")}>
                  Тренировка карточки
                </PaperButton>
              </div>
            ),
          },
          {
            value: "status",
            title: "Состояние работы и заметки",
            content: (
              <p>
                «В работе» и «Архив» описывают состояние работы. Переключение фильтра ничего не
                архивирует и не восстанавливает. Заметки и их историю видят сотрудники с
                соответствующими эффективными правами. Заметки сохраняются с проверкой актуального
                состояния: при конфликте сначала загрузите серверную версию. Восстановление доступно
                для архивных карточек без возврата отозванного доступа; архивирование пока отключено
                до переноса проверок зависимостей расписания и финансов.
              </p>
            ),
          },
        ]}
      />
    </div>
  );
}
