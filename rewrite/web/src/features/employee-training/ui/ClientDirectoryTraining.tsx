import { Alert, Button, Collapse, Input, Radio, Select, Space, Typography } from "antd";
import { useState } from "react";
import "./clientDirectoryTraining.css";
import {
  DataView as CollectionDataView,
  type DataViewColumn,
  type DataViewSort,
} from "@/shared/ui/data-view";
import {
  clientDirectoryExerciseComplete,
  type DemoClient,
  type DemoClientQuery,
  selectDemoClients,
} from "../model/clientDirectoryDemo";

const { Paragraph, Title } = Typography;
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
    <div className="employee-training-exercise">
      <p className="employee-training-demo-note">
        <span>Учебные клиенты</span> · Настоящие данные не меняются.
      </p>
      <Paragraph>
        {topic === "search"
          ? "Введите полное ФИО «Иванов Антон» и выберите источник «Рекомендация». Оставьте только тех, кто в работе. В результате должны остаться Иванов Артём и Иванов Антон: поиск допускает до трёх изменений в полном ФИО. Порядок от Я к А уже выбран."
          : "Поиск и фильтры уже настроены. Нажмите стрелку в заголовке ФИО и расположите клиентов от Я к А: сначала Иванов Артём, затем Иванов Антон."}
      </Paragraph>
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
          <Space wrap>
            <Select
              aria-label="Учебный источник"
              value={query.source}
              onChange={(source: DemoClientQuery["source"]) => changeQuery({ source })}
              options={[
                { value: "all", label: "Все источники" },
                { value: "recommendation", label: "Рекомендация" },
                { value: "website", label: "Сайт" },
              ]}
            />
            <Select
              aria-label="Учебное состояние работы"
              value={query.archive}
              onChange={(archive: DemoClientQuery["archive"]) => changeQuery({ archive })}
              options={[
                { value: "all", label: "Все" },
                { value: "active", label: "В работе" },
                { value: "archived", label: "Архив" },
              ]}
            />
          </Space>
        }
      />
      {attempted && (
        <Alert
          type="warning"
          title={
            topic === "search"
              ? "Проверьте поиск, источник и состояние работы."
              : "Проверьте порядок фамилий: от Я к А."
          }
        />
      )}
      {completed ? (
        <Alert type="success" title="Тренировка пройдена" />
      ) : (
        <Button type="primary" onClick={checkResult}>
          Проверить результат
        </Button>
      )}
      {completed && (
        <Button
          onClick={() => {
            setCompleted(false);
            setQuery(startingQuery);
            setPage(1);
            setAttempted(false);
          }}
        >
          Повторить тренировку
        </Button>
      )}
      {completed && topic === "search" && (
        <Button type="primary" onClick={() => onStartTraining("sorting")}>
          К сортировке
        </Button>
      )}
      {completed && topic === "sorting" && (
        <Button onClick={() => onStartTraining("search")}>К поиску и фильтрам</Button>
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
    <section
      className="employee-training-card-practice"
      aria-labelledby="client-card-practice-heading"
    >
      <Title level={5} id="client-card-practice-heading">
        Учебная карточка
      </Title>
      <p className="employee-training-demo-note">
        Учебная карточка · Настоящие данные не меняются.
      </p>
      <Paragraph>
        Заполните имя, фамилию и источник. При добавлении телефона отметьте его основным, затем
        сохраните учебную карточку.
      </Paragraph>
      <Space orientation="vertical" size={12} className="wide">
        <div className="employee-training-field">
          <span>
            Фамилия
            <span className="field-required" aria-hidden="true">
              *
            </span>
          </span>
          <Input
            aria-label="Учебная фамилия"
            value={lastName}
            onChange={(event) => {
              setLastName(event.target.value);
              setSaved(false);
            }}
          />
        </div>
        <div className="employee-training-field">
          <span>
            Имя
            <span className="field-required" aria-hidden="true">
              *
            </span>
          </span>
          <Input
            aria-label="Учебное имя"
            value={firstName}
            onChange={(event) => {
              setFirstName(event.target.value);
              setSaved(false);
            }}
          />
        </div>
        <div className="employee-training-field">
          <span>
            Источник
            <span className="field-required" aria-hidden="true">
              *
            </span>
          </span>
          <Select
            aria-label="Учебный источник карточки"
            value={source || null}
            placeholder="Выберите источник"
            onChange={(next: string) => {
              setSource(next);
              setSaved(false);
            }}
            options={[
              { value: "recommendation", label: "Рекомендация" },
              { value: "website", label: "Сайт" },
            ]}
          />
        </div>
        <div className="employee-training-field">
          <span>Телефон</span>
          <Input
            aria-label="Учебный телефон"
            value={contact}
            onChange={(event) => {
              setContact(event.target.value);
              setSaved(false);
            }}
          />
        </div>
        {contact.trim() ? (
          <Radio
            checked={primary}
            onChange={() => {
              setPrimary(true);
              setSaved(false);
            }}
          >
            Основной контакт
          </Radio>
        ) : null}
        {issue ? (
          <Alert
            type="warning"
            title="Укажите имя, фамилию и источник; добавленный контакт отметьте основным."
          />
        ) : null}
        {saved ? <Alert type="success" title="Учебная карточка сохранена." /> : null}
        <Button type="primary" onClick={saveDemoCard}>
          Сохранить учебную карточку
        </Button>
        <Alert
          type="warning"
          title="Архивирование временно недоступно"
          description="Оно будет включено после переноса зависимостей расписания и финансов."
        />
      </Space>
    </section>
  );
}

export function ClientDirectoryArticle({
  onStartTraining,
}: {
  onStartTraining: (topic: ClientTrainingTopic) => void;
}) {
  return (
    <div className="employee-training-article">
      <Collapse
        items={[
          {
            key: "search",
            label: "Поиск и фильтры",
            children: (
              <div className="employee-training-topic">
                <Paragraph>
                  Поиск находит клиентов по любой части ФИО с допуском до трёх вставок, удалений или
                  замен символов. Например, «багд» находит «Богданова Полина». Регистр и лишние
                  пробелы не учитываются. Для коротких запросов действует тот же допуск, поэтому
                  запросы до трёх символов совпадают с любым ФИО. Фильтры состояния работы и
                  источника можно сочетать.
                </Paragraph>
                <Paragraph>
                  Изменение поиска и фильтров возвращает на первую страницу. Пустой результат
                  означает, что условиям никто не соответствует; при ошибке загрузки используйте
                  повтор.
                </Paragraph>
                <Button type="primary" onClick={() => onStartTraining("search")}>
                  Тренировка поиска и фильтров
                </Button>
              </div>
            ),
          },
          {
            key: "sorting",
            label: "Сортировка таблицы",
            children: (
              <div className="employee-training-topic">
                <Paragraph>
                  Нажмите стрелку в заголовке колонки. Повторное нажатие меняет направление и затем
                  убирает поле из сортировки. Можно добавить несколько полей; номера у стрелок
                  обозначают их приоритет. Изменение сортировки возвращает на первую страницу.
                  Таблица по умолчанию показывает 10 строк; на телефоне она прокручивается по
                  горизонтали.
                </Paragraph>
                <Button type="primary" onClick={() => onStartTraining("sorting")}>
                  Тренировка сортировки
                </Button>
              </div>
            ),
          },
          {
            key: "card",
            label: "Карточка клиента",
            children: (
              <div className="employee-training-topic">
                <Paragraph>
                  Нажатие на клиента открывает правую панель с контактами и остальными данными. В
                  боковой панели видны профиль и список контактов. Если у сотрудника есть
                  эффективное право на запись карточек, он может создать или изменить ФИО, дату
                  рождения, источник и контакты. Для каждого используемого вида контакта требуется
                  один основной контакт; при удалении основного контакта выберите замену явно.
                  Источник обязателен, список контактов может быть пустым. Совпадение телефона или
                  почты показывает предупреждение и не блокирует сохранение.
                </Paragraph>
                <Button type="primary" onClick={() => onStartTraining("card")}>
                  Тренировка карточки
                </Button>
              </div>
            ),
          },
          {
            key: "status",
            label: "Состояние работы и заметки",
            children: (
              <Paragraph>
                «В работе» и «Архив» описывают состояние работы. Переключение фильтра ничего не
                архивирует и не восстанавливает. Заметки и их историю видят сотрудники с
                соответствующими эффективными правами. Заметки сохраняются с проверкой актуального
                состояния: при конфликте сначала загрузите серверную версию. Восстановление доступно
                для архивных карточек без возврата отозванного доступа; архивирование пока отключено
                до переноса проверок зависимостей расписания и финансов.
              </Paragraph>
            ),
          },
        ]}
      />
    </div>
  );
}
