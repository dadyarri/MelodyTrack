import { Alert, Button, Input, Radio, Select, Space, Typography } from "antd";
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

export function ClientDirectoryExercise({ onComplete }: { onComplete: () => void }) {
  const [query, setQuery] = useState<DemoClientQuery>(initialQuery);
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

  return (
    <div className="employee-training-exercise">
      <Alert
        type="info"
        title="Учебные клиенты"
        description="Вымышленные данные. Настоящий список не меняется."
      />
      <Paragraph>
        Введите полное ФИО «Иванов Антон» и выберите источник «Рекомендация». Оставьте только тех,
        кто в работе, и расположите ФИО от Я к А. В результате должны остаться Иванов Артём и Иванов
        Антон: поиск допускает до трёх изменений в полном ФИО.
      </Paragraph>
      <CollectionDataView
        ariaLabel="Учебный список клиентов"
        columns={columns}
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
          setQuery(initialQuery);
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
          title="Проверьте поиск, источник, состояние работы и порядок фамилий."
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
            setQuery(initialQuery);
            setPage(1);
            setAttempted(false);
          }}
        >
          Повторить тренировку
        </Button>
      )}
      <ClientCardPractice />
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
      <Alert
        type="info"
        title="Демонстрационные данные"
        description="Изменения сохраняются только в этой тренировке и не затрагивают карточки клиентов."
      />
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

export function ClientDirectoryArticle() {
  return (
    <div className="employee-training-article">
      <Title level={4}>Список клиентов</Title>
      <Paragraph>
        Поиск находит клиентов по любой части ФИО. Для полного ФИО допускается до трёх вставок,
        удалений или замен символов. Регистр и лишние пробелы не учитываются. Фильтры состояния
        работы и источника можно сочетать.
      </Paragraph>
      <Paragraph>
        Нажмите стрелку в заголовке колонки. Повторное нажатие меняет направление и затем убирает
        поле из сортировки. Можно добавить несколько полей; номера у стрелок обозначают их
        приоритет. Изменение поиска, фильтров и сортировки возвращает на первую страницу.
      </Paragraph>
      <Paragraph>
        Таблица компактная, по умолчанию показывает 10 строк. Нажатие на клиента открывает правую
        панель с контактами и остальными данными. На телефоне таблицу можно прокручивать по
        горизонтали. Пустой результат означает, что условиям никто не соответствует; при ошибке
        загрузки используйте повтор.
      </Paragraph>
      <Paragraph>
        «В работе» и «Архив» описывают состояние работы. Переключение фильтра ничего не архивирует и
        не восстанавливает.
      </Paragraph>
      <Paragraph>
        В боковой панели видны профиль и список контактов. Если у сотрудника есть эффективное право
        на запись карточек, он может создать или изменить ФИО, дату рождения, источник и контакты.
        Для каждого используемого вида контакта требуется один основной контакт; если удалить
        основной, выберите замену явно. Источник обязателен, список контактов может быть пустым.
      </Paragraph>
      <Paragraph>
        Совпадение телефона или почты только показывает предупреждение и не блокирует сохранение.
        Заметки и их историю видят сотрудники с соответствующими эффективными правами. Заметки
        сохраняются с проверкой актуального состояния, поэтому при конфликте сначала загрузите
        серверную версию. Восстановление доступно для архивных карточек без возврата отозванного
        доступа; архивирование пока отключено, пока не перенесены проверки зависимостей расписания и
        финансов.
      </Paragraph>
    </div>
  );
}
