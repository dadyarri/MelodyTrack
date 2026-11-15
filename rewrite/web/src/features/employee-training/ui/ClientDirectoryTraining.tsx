import { Alert, Button, Select, Space, Typography } from "antd";
import { useState } from "react";
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
    </div>
  );
}

export function ClientDirectoryArticle() {
  return (
    <div className="employee-training-article">
      <Title level={4}>Список клиентов</Title>
      <Paragraph>
        Поиск находит клиентов по полному ФИО, допуская до трёх вставок, удалений или замен
        символов. Он сравнивает ФИО целиком, а не ищет отдельное слово. Регистр и лишние пробелы не
        учитываются. Фильтры состояния работы и источника можно сочетать.
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
    </div>
  );
}
