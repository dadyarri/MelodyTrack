import {
  Alert,
  Button,
  Card,
  Collapse,
  Form,
  Input,
  Modal,
  Space,
  Spin,
  Steps,
  Tabs,
  Tag,
  Typography,
} from "antd";
import { createContext, type ReactNode, useContext, useEffect, useRef, useState } from "react";
import {
  attemptDemoSignIn,
  createProgress,
  type DemoStep,
  finishDemoSession,
  recordDemoStep,
  type TrainingProgress,
} from "../model/progress";
import { loadTrainingProgress, saveTrainingProgress } from "../model/storage";
import "./employee-training.css";

const { Paragraph, Text, Title } = Typography;

interface TrainingContextValue {
  progress: TrainingProgress | null;
  demoStep: DemoStep | null;
  ready: boolean;
  storageAvailable: boolean;
  openTraining: () => void;
  openArticle: () => void;
  markArticleRead: () => void;
  attemptSignIn: (login: string, password: string) => boolean;
  finishSession: () => void;
  restartDemo: () => void;
}

const TrainingContext = createContext<TrainingContextValue | null>(null);

export function useEmployeeTraining(): TrainingContextValue {
  const value = useContext(TrainingContext);
  if (!value) throw new Error("EmployeeTrainingProvider is required.");
  return value;
}

/** Only mount for an authenticated staff identity; never for a client identity. */
export function EmployeeTrainingProvider({
  staffId,
  children,
}: {
  staffId: string;
  children: ReactNode;
}) {
  return (
    <AccountTraining key={staffId} staffId={staffId}>
      {children}
    </AccountTraining>
  );
}

function AccountTraining({ staffId, children }: { staffId: string; children: ReactNode }) {
  const [progress, setProgress] = useState<TrainingProgress | null>(null);
  const [storageAvailable, setStorageAvailable] = useState(true);
  const [modalOpen, setModalOpen] = useState(false);
  const [tab, setTab] = useState("exercise");
  const [replayStep, setReplayStep] = useState<DemoStep | null>(null);
  const replay = useRef<DemoStep | null>(null);
  const current = useRef<TrainingProgress | null>(null);
  const lifetime = useRef<AbortController | null>(null);
  const canPersist = useRef(true);
  const writes = useRef<Promise<void>>(Promise.resolve());

  useEffect(() => {
    const controller = new AbortController();
    lifetime.current = controller;
    loadTrainingProgress(staffId, controller.signal)
      .then((loaded) => {
        if (controller.signal.aborted) return;
        current.current = loaded;
        setProgress(loaded);
        setModalOpen(loaded.demoStep !== "completed" && !loaded.deferred);
      })
      .catch(() => {
        if (controller.signal.aborted) return;
        // Do not overwrite unavailable, corrupt or future-version persisted data.
        canPersist.current = false;
        setStorageAvailable(false);
        const fallback = createProgress(staffId);
        current.current = fallback;
        setProgress(fallback);
        setModalOpen(true);
      });
    return () => controller.abort();
  }, [staffId]);

  function update(change: (previous: TrainingProgress) => TrainingProgress) {
    const previous = current.current;
    const controller = lifetime.current;
    if (!previous || !controller || controller.signal.aborted) return;
    const next = change(previous);
    current.current = next;
    setProgress(next);
    if (!canPersist.current) return;
    writes.current = writes.current
      .then(async () => {
        if (controller.signal.aborted) return;
        await saveTrainingProgress(next, controller.signal);
        if (!controller.signal.aborted) setStorageAvailable(true);
      })
      .catch(() => {
        if (!controller.signal.aborted) setStorageAvailable(false);
      });
  }

  function markArticleRead() {
    if (!current.current?.articleRead) update((previous) => ({ ...previous, articleRead: true }));
  }

  function updateReplay(step: DemoStep | null) {
    replay.current = step;
    setReplayStep(step);
  }

  const value: TrainingContextValue = {
    progress,
    demoStep: replayStep ?? progress?.demoStep ?? null,
    ready: progress !== null,
    storageAvailable,
    openTraining: () => {
      updateReplay(current.current?.demoStep === "completed" ? "signed-out" : null);
      setTab("exercise");
      setModalOpen(true);
    },
    openArticle: () => {
      markArticleRead();
      setTab("article");
      setModalOpen(true);
    },
    markArticleRead,
    attemptSignIn: (login, password) => {
      const previous = current.current;
      if (!previous) return false;
      const previousStep = replay.current ?? previous.demoStep;
      const nextStep = attemptDemoSignIn(previousStep, login, password);
      if (nextStep === previousStep) return false;
      if (replay.current !== null) updateReplay(nextStep);
      else update((record) => recordDemoStep(record, nextStep));
      return true;
    },
    finishSession: () => {
      if (replay.current !== null) updateReplay(finishDemoSession(replay.current));
      else update((record) => recordDemoStep(record, finishDemoSession(record.demoStep)));
    },
    restartDemo: () => {
      if (current.current?.demoStep === "completed") updateReplay("signed-out");
      else update((record) => recordDemoStep(record, "signed-out"));
    },
  };

  function closeTraining() {
    if (progress?.demoStep !== "completed") {
      update((record) => ({ ...record, deferred: true }));
    }
    setModalOpen(false);
    updateReplay(null);
  }

  return (
    <TrainingContext.Provider value={value}>
      {progress ? (
        <div inert={modalOpen && progress.demoStep !== "completed"}>{children}</div>
      ) : (
        <div className="employee-training-loading" role="status" aria-live="polite">
          <Spin />
          <span>Загружаем ваше обучение…</span>
        </div>
      )}
      <Modal
        className="employee-training-modal"
        style={{ maxWidth: "100vw" }}
        title="Первое знакомство: вход и сессия"
        open={Boolean(progress) && modalOpen}
        closable={false}
        mask={{ closable: false }}
        keyboard={false}
        width={760}
        footer={[
          <Button key="defer" onClick={closeTraining}>
            {progress?.demoStep === "completed" ? "К работе" : "Отложить"}
          </Button>,
        ]}
      >
        <Paragraph>
          Попробуйте рабочий сценарий на вымышленном аккаунте. Его можно пройти сейчас или отложить
          и вернуться через кнопку обучения.
        </Paragraph>
        {!storageAvailable && <StorageNotice />}
        <Tabs
          activeKey={tab}
          onChange={(key) => {
            setTab(key);
            if (key === "article") markArticleRead();
          }}
          items={[
            { key: "exercise", label: "Тренировка", children: <SessionExercise /> },
            { key: "article", label: "Как это работает", children: <AccessArticle /> },
          ]}
        />
      </Modal>
      {progress?.deferred && progress.demoStep !== "completed" && !modalOpen && (
        <div className="employee-training-resume">
          <Button type="primary" size="large" onClick={value.openTraining}>
            Продолжить обучение
          </Button>
        </div>
      )}
    </TrainingContext.Provider>
  );
}

function StorageNotice() {
  return (
    <Alert
      type="warning"
      showIcon
      title="Прогресс пока доступен только в этой вкладке"
      description="Сохранение на устройстве недоступно. Вы можете продолжить обучение, но после закрытия страницы его может понадобиться пройти снова."
    />
  );
}

function SessionExercise() {
  const { progress, demoStep, attemptSignIn, finishSession, restartDemo } = useEmployeeTraining();
  const [error, setError] = useState(false);
  const [form] = Form.useForm<{ login: string; password: string }>();
  if (!progress) return null;
  const completed = demoStep === "completed";
  return (
    <div className="employee-training-exercise">
      <Alert
        type="info"
        showIcon
        title="Учебная ситуация"
        description="Здесь нет настоящих аккаунтов. Не вводите свой пароль: действия изменяют только демонстрацию."
      />
      <Steps
        size="small"
        current={completed ? 2 : demoStep === "signed-in" ? 1 : 0}
        items={[{ title: "Войти" }, { title: "Выйти" }]}
      />
      {demoStep === "signed-out" && (
        <Card size="small" title="Войдите в учебный аккаунт">
          <Paragraph>
            Введите логин <Text code>teacher.demo</Text> и учебный пароль{" "}
            <Text code>demo-lesson</Text>.
          </Paragraph>
          <Form
            form={form}
            layout="vertical"
            autoComplete="off"
            onFinish={(values) => {
              const accepted = attemptSignIn(values.login, values.password);
              setError(!accepted);
              form.resetFields(["password"]);
            }}
          >
            <Form.Item
              name="login"
              label="Учебный логин"
              rules={[{ required: true, message: "Введите учебный логин" }]}
            >
              <Input autoComplete="off" spellCheck={false} autoCapitalize="none" />
            </Form.Item>
            <Form.Item
              name="password"
              label="Учебный пароль"
              rules={[{ required: true, message: "Введите учебный пароль" }]}
            >
              <Input.Password autoComplete="new-password" />
            </Form.Item>
            {error && (
              <Paragraph type="danger" role="alert">
                Используйте вымышленные данные из задания выше.
              </Paragraph>
            )}
            <Button type="primary" htmlType="submit">
              Войти в демонстрацию
            </Button>
          </Form>
        </Card>
      )}
      {demoStep === "signed-in" && (
        <Card size="small" title="Учебная сессия открыта">
          <Paragraph>
            Вы вошли как <Text strong>Учебный преподаватель</Text>. Завершите эту сессию кнопкой
            выхода.
          </Paragraph>
          <Space wrap>
            <Button type="primary" onClick={finishSession}>
              Выйти из демонстрации
            </Button>
            <Button
              onClick={() => {
                setError(false);
                restartDemo();
              }}
            >
              Начать заново
            </Button>
          </Space>
        </Card>
      )}
      {completed && (
        <div className="employee-training-exercise">
          <Alert
            type="success"
            showIcon
            title="Тренировка пройдена"
            description="Вы открыли и завершили учебную сессию. Настоящий аккаунт сотрудника и его сессии не изменились."
          />
          <Button
            onClick={() => {
              setError(false);
              restartDemo();
            }}
          >
            Повторить тренировку
          </Button>
        </div>
      )}
    </div>
  );
}

function AccessArticle() {
  return (
    <div className="employee-training-article">
      <Title level={4}>Вход и безопасное завершение работы</Title>
      <Paragraph>
        Войти можно по логину и паролю либо с помощью ключа доступа. Если для аккаунта нужен второй
        фактор, следуйте следующему шагу входа: подтвердите код аутентификатора или ключ доступа. До
        завершения проверки рабочая сессия не открыта.
      </Paragraph>
      <Collapse
        items={[
          {
            key: "identity",
            label: "Проверьте, что вход завершён",
            children: (
              <Paragraph>
                После завершения входа открывается рабочее пространство. Доступные действия зависят
                от ваших текущих прав. Если нужный раздел недоступен, обратитесь к сотруднику,
                который управляет доступом: обучение само по себе не выдаёт дополнительных
                разрешений.
              </Paragraph>
            ),
          },
          {
            key: "factor",
            label: "Не получается подтвердить второй фактор",
            children: (
              <Paragraph>
                Проверьте код в приложении-аутентификаторе и время устройства. При использовании
                ключа доступа выполните системный запрос браузера. Если средство входа потеряно,
                обратитесь за восстановлением доступа. Не передавайте пароли, коды и ссылки
                восстановления другим людям.
              </Paragraph>
            ),
          },
          {
            key: "logout",
            label: "Завершите сессию на общем устройстве",
            children: (
              <Paragraph>
                Используйте «Выйти», когда заканчиваете работу на общем компьютере. Закрытие вкладки
                не заменяет отзыв серверной сессии. Выход завершает текущую сессию и не затрагивает
                другие устройства.
              </Paragraph>
            ),
          },
          {
            key: "connection",
            label: "Что происходит без связи",
            children: (
              <Paragraph>
                Для настоящего входа и выхода требуется связь с сервером. При сетевой ошибке
                приложение не должно выдавать неподтверждённое действие за успешное. Уже загруженная
                учебная демонстрация работает без обращения к серверу.
              </Paragraph>
            ),
          },
        ]}
      />
      <Paragraph type="secondary">
        Этот начальный модуль сохраняет прогресс на текущем устройстве. Синхронизация обучения между
        устройствами появится отдельной возможностью.
      </Paragraph>
    </div>
  );
}

export function TrainingWorkspace() {
  const { progress, storageAvailable, openTraining, markArticleRead } = useEmployeeTraining();
  return (
    <div className="employee-training-workspace">
      <div>
        <Text type="secondary">ПОМОЩЬ В РАБОТЕ</Text>
        <Title level={2}>Журнал обучения</Title>
        <Paragraph>
          Короткие инструкции и безопасная практика. Материалы появляются вместе с рабочими
          возможностями приложения.
        </Paragraph>
      </div>
      {!storageAvailable && <StorageNotice />}
      <Card
        title="Вход и сессия"
        extra={
          <Tag color={progress?.demoStep === "completed" ? "success" : "processing"}>
            {progress?.demoStep === "completed"
              ? "Пройдено"
              : progress?.deferred
                ? "Отложено"
                : "К прохождению"}
          </Tag>
        }
      >
        <Paragraph>
          Как подтвердить вход, проверить свой аккаунт и завершить сессию на общем устройстве.
        </Paragraph>
        <Space wrap>
          <Button type="primary" onClick={openTraining}>
            {progress?.demoStep === "completed" ? "Открыть тренировку" : "Продолжить тренировку"}
          </Button>
          <Tag>{progress?.articleRead ? "Статья прочитана" : "Новая статья"}</Tag>
        </Space>
      </Card>
      <Collapse
        onChange={(keys) => {
          if (Array.isArray(keys) && keys.includes("access")) markArticleRead();
        }}
        items={[
          {
            key: "access",
            label: "Инструкция: вход и безопасное завершение работы",
            children: <AccessArticle />,
          },
        ]}
      />
      <Paragraph type="secondary">
        Прогресс на этом устройстве. Чтение статьи и прохождение тренировки учитываются отдельно.
      </Paragraph>
    </div>
  );
}
