import {
  Alert,
  Button,
  Card,
  Collapse,
  Modal,
  Space,
  Spin,
  Steps,
  Tabs,
  Tag,
  Typography,
} from "antd";
import { createContext, type ReactNode, useContext, useEffect, useRef, useState } from "react";
import { useErrorNotification } from "@/shared/ui/notifications";
import {
  createProgress,
  type DemoStep,
  finishDemoSession,
  recordDemoStep,
  startDemoSession,
  type TrainingProgress,
} from "../model/progress";
import { loadTrainingProgress, saveTrainingProgress } from "../model/storage";
import { ClientDirectoryArticle, ClientDirectoryExercise } from "./ClientDirectoryTraining";
import "./employee-training.css";

const { Paragraph, Text, Title } = Typography;

interface TrainingContextValue {
  progress: TrainingProgress | null;
  demoStep: DemoStep | null;
  ready: boolean;
  active: boolean;
  storageAvailable: boolean;
  canUseClientDirectory: boolean;
  openTraining: () => void;
  openArticle: () => void;
  markArticleRead: () => void;
  startDemo: () => void;
  finishSession: () => void;
  restartDemo: () => void;
  openClientTraining: () => void;
  completeClientTraining: () => void;
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
  canUseClientDirectory = false,
  children,
}: {
  staffId: string;
  canUseClientDirectory?: boolean;
  children: ReactNode;
}) {
  return (
    <AccountTraining key={staffId} staffId={staffId} canUseClientDirectory={canUseClientDirectory}>
      {children}
    </AccountTraining>
  );
}

function AccountTraining({
  staffId,
  canUseClientDirectory,
  children,
}: {
  staffId: string;
  canUseClientDirectory: boolean;
  children: ReactNode;
}) {
  const [progress, setProgress] = useState<TrainingProgress | null>(null);
  const [storageAvailable, setStorageAvailable] = useState(true);
  const [storageError, setStorageError] = useState<"load" | "save" | null>(null);
  const [modalOpen, setModalOpen] = useState(false);
  const [tab, setTab] = useState("exercise");
  const [module, setModule] = useState<"access" | "clients">("access");
  const [clientReplay, setClientReplay] = useState(0);
  const [replayStep, setReplayStep] = useState<DemoStep | null>(null);
  const replay = useRef<DemoStep | null>(null);
  const current = useRef<TrainingProgress | null>(null);
  const lifetime = useRef<AbortController | null>(null);
  const canPersist = useRef(true);
  const writes = useRef<Promise<void>>(Promise.resolve());
  const visibleModal = modalOpen && (module !== "clients" || canUseClientDirectory);

  useErrorNotification(
    storageError
      ? storageError === "load"
        ? "Не удалось загрузить сохранённый прогресс обучения."
        : "Не удалось сохранить прогресс обучения."
      : null,
    { title: "Ошибка локального хранилища" },
  );

  useEffect(() => {
    if (module === "clients" && !canUseClientDirectory) setModalOpen(false);
  }, [module, canUseClientDirectory]);

  useEffect(() => {
    const controller = new AbortController();
    lifetime.current = controller;
    loadTrainingProgress(staffId, controller.signal)
      .then((loaded) => {
        if (controller.signal.aborted) return;
        current.current = loaded;
        setProgress(loaded);
        setStorageError(null);
        setStorageAvailable(true);
        setModalOpen(loaded.demoStep !== "completed" && !loaded.deferred);
      })
      .catch(() => {
        if (controller.signal.aborted) return;
        // Do not overwrite unavailable, corrupt or future-version persisted data.
        canPersist.current = false;
        setStorageAvailable(false);
        setStorageError("load");
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
        if (!controller.signal.aborted) {
          setStorageAvailable(true);
          setStorageError(null);
        }
      })
      .catch(() => {
        if (!controller.signal.aborted) {
          setStorageAvailable(false);
          setStorageError("save");
        }
      });
  }

  function markArticleRead() {
    if (!current.current?.articleRead) update((previous) => ({ ...previous, articleRead: true }));
  }

  function updateReplay(step: DemoStep | null) {
    replay.current = step;
    setReplayStep(step);
  }

  function startTrainingSession() {
    const previous = current.current;
    if (!previous) return;
    if (previous.demoStep === "completed") updateReplay("signed-in");
    else update((record) => recordDemoStep(record, startDemoSession(record.demoStep)));
  }

  const value: TrainingContextValue = {
    progress,
    demoStep: replayStep ?? progress?.demoStep ?? null,
    ready: progress !== null,
    active: visibleModal,
    storageAvailable,
    canUseClientDirectory,
    openTraining: () => {
      setModule("access");
      startTrainingSession();
      setTab("exercise");
      setModalOpen(true);
    },
    openArticle: () => {
      setModule("access");
      markArticleRead();
      setTab("article");
      setModalOpen(true);
    },
    markArticleRead,
    startDemo: startTrainingSession,
    finishSession: () => {
      if (replay.current !== null) updateReplay(finishDemoSession(replay.current));
      else update((record) => recordDemoStep(record, finishDemoSession(record.demoStep)));
    },
    restartDemo: startTrainingSession,
    openClientTraining: () => {
      if (!canUseClientDirectory) return;
      setModule("clients");
      setTab("exercise");
      setClientReplay((value) => value + 1);
      setModalOpen(true);
    },
    completeClientTraining: () => {
      if (!canUseClientDirectory) return;
      if (!current.current?.clientDirectory.completed) {
        update((record) => ({
          ...record,
          clientDirectory: { ...record.clientDirectory, completed: true },
        }));
      }
    },
  };

  function closeTraining() {
    if (module === "clients" && !progress?.clientDirectory.completed) {
      update((record) => ({
        ...record,
        clientDirectory: { ...record.clientDirectory, deferred: true },
      }));
    } else if (module === "access" && progress?.demoStep !== "completed") {
      update((record) => ({ ...record, deferred: true }));
    }
    setModalOpen(false);
    updateReplay(null);
  }

  return (
    <TrainingContext.Provider value={value}>
      {progress ? (
        <div
          inert={
            visibleModal &&
            (module === "clients"
              ? !progress.clientDirectory.completed
              : progress.demoStep !== "completed")
          }
        >
          {children}
        </div>
      ) : (
        <div className="employee-training-loading" role="status" aria-live="polite">
          <Spin />
          <span>Загрузка…</span>
        </div>
      )}
      {(module !== "clients" || canUseClientDirectory) && (
        <Modal
          className="employee-training-modal"
          style={{ maxWidth: "100vw" }}
          title={module === "clients" ? "Список клиентов" : "Вход и сессия"}
          open={Boolean(progress) && visibleModal}
          closable={false}
          mask={{ closable: false }}
          keyboard={false}
          width={module === "clients" ? 1040 : 760}
          footer={[
            <Button key="defer" onClick={closeTraining}>
              {(
                module === "clients"
                  ? progress?.clientDirectory.completed
                  : progress?.demoStep === "completed"
              )
                ? "К работе"
                : "Отложить"}
            </Button>,
          ]}
        >
          <Tabs
            activeKey={tab}
            onChange={(key) => {
              setTab(key);
              if (key === "article" && module === "access") markArticleRead();
            }}
            items={[
              {
                key: "exercise",
                label: "Тренировка",
                children:
                  module === "clients" ? (
                    canUseClientDirectory ? (
                      <ClientDirectoryExercise
                        key={clientReplay}
                        onComplete={value.completeClientTraining}
                      />
                    ) : null
                  ) : (
                    <SessionExercise />
                  ),
              },
              {
                key: "article",
                label: "Инструкция",
                children: module === "clients" ? <ClientDirectoryArticle /> : <AccessArticle />,
              },
            ]}
          />
        </Modal>
      )}
      {progress &&
        !visibleModal &&
        ((progress.deferred && progress.demoStep !== "completed") ||
          (canUseClientDirectory &&
            progress.clientDirectory.deferred &&
            !progress.clientDirectory.completed)) && (
          <div className="employee-training-resume">
            <Button
              type="primary"
              size="large"
              onClick={
                progress.deferred && progress.demoStep !== "completed"
                  ? value.openTraining
                  : value.openClientTraining
              }
            >
              Продолжить обучение
            </Button>
          </div>
        )}
    </TrainingContext.Provider>
  );
}

function SessionExercise() {
  const { progress, demoStep, startDemo, finishSession, restartDemo } = useEmployeeTraining();
  if (!progress) return null;
  const completed = demoStep === "completed";
  return (
    <div className="employee-training-exercise">
      <Alert
        type="info"
        showIcon
        title="Учебный аккаунт"
        description="Действия в тренировке не меняют ваш настоящий аккаунт."
      />
      <Steps
        size="small"
        current={completed ? 2 : demoStep === "signed-in" ? 1 : 0}
        items={[{ title: "Начать" }, { title: "Выйти" }]}
      />
      {demoStep === "signed-out" && (
        <Button type="primary" onClick={startDemo}>
          Начать обучение
        </Button>
      )}
      {demoStep === "signed-in" && (
        <Card size="small" title="Учебная сессия открыта">
          <Paragraph>
            Вы вошли как <Text strong>Учебный преподаватель</Text>. Теперь выйдите из аккаунта.
          </Paragraph>
          <Space wrap>
            <Button type="primary" onClick={finishSession}>
              Выйти из демонстрации
            </Button>
            <Button onClick={restartDemo}>Начать заново</Button>
          </Space>
        </Card>
      )}
      {completed && (
        <div className="employee-training-exercise">
          <Alert type="success" showIcon title="Тренировка пройдена" />
          <Button onClick={restartDemo}>Повторить тренировку</Button>
        </div>
      )}
    </div>
  );
}

function AccessArticle() {
  return (
    <div className="employee-training-article">
      <Title level={4}>Вход и выход</Title>
      <Paragraph>
        Войдите по логину и паролю или с ключом доступа. Если запрошен второй фактор, подтвердите
        вход кодом из аутентификатора или ключом доступа.
      </Paragraph>
      <Collapse
        items={[
          {
            key: "invitation",
            label: "Первый вход по приглашению",
            children: (
              <Paragraph>
                Откройте полученную ссылку и пройдите шаги: пароль, аутентификатор, ключ доступа.
                Кнопка пропуска доступна там, где выбранные средства входа и роль это допускают. У
                суперпользователя и администратора второй фактор обязателен.
              </Paragraph>
            ),
          },
          {
            key: "identity",
            label: "Проверьте, что вход завершён",
            children: (
              <Paragraph>
                После входа открывается главная страница. Если нужный раздел недоступен, обратитесь
                к суперпользователю школы.
              </Paragraph>
            ),
          },
          {
            key: "factor",
            label: "Не получается подтвердить второй фактор",
            children: (
              <Paragraph>
                Проверьте код в приложении-аутентификаторе и время устройства. Для ключа доступа
                выполните запрос браузера. Если ключ или аутентификатор потерян, обратитесь к
                суперпользователю. Не передавайте пароли и коды другим людям.
              </Paragraph>
            ),
          },
          {
            key: "logout",
            label: "Завершите сессию на общем устройстве",
            children: (
              <Paragraph>
                Откройте меню под приветствием в верхней панели и нажмите «Выйти». Закрытие вкладки
                не завершает сессию на сервере. Выход на этом устройстве не затрагивает другие
                устройства.
              </Paragraph>
            ),
          },
          {
            key: "connection",
            label: "Что происходит без связи",
            children: (
              <Paragraph>
                Для входа и выхода нужна связь с сервером. Если появилась ошибка, восстановите
                соединение и повторите действие. Загруженная учебная тренировка доступна без сети.
              </Paragraph>
            ),
          },
        ]}
      />
    </div>
  );
}

export function TrainingWorkspace() {
  const { progress, canUseClientDirectory, openTraining, openClientTraining, markArticleRead } =
    useEmployeeTraining();
  return (
    <div className="employee-training-workspace">
      <div>
        <Title level={2}>Обучение</Title>
      </div>
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
        <Space wrap>
          <Button type="primary" onClick={openTraining}>
            {progress?.demoStep === "completed"
              ? "Повторить тренировку"
              : progress?.deferred
                ? "Продолжить обучение"
                : "Начать обучение"}
          </Button>
        </Space>
      </Card>
      <Collapse
        onChange={(keys) => {
          if (Array.isArray(keys) && keys.includes("access")) markArticleRead();
        }}
        items={[
          {
            key: "access",
            label: "Инструкция: вход и выход",
            children: <AccessArticle />,
          },
        ]}
      />
      {canUseClientDirectory && (
        <Card
          title="Список клиентов"
          extra={
            <Tag color={progress?.clientDirectory.completed ? "success" : "processing"}>
              {progress?.clientDirectory.completed
                ? "Пройдено"
                : progress?.clientDirectory.deferred
                  ? "Отложено"
                  : "К прохождению"}
            </Tag>
          }
        >
          <Button type="primary" onClick={openClientTraining}>
            {progress?.clientDirectory.completed
              ? "Повторить тренировку"
              : progress?.clientDirectory.deferred
                ? "Продолжить обучение"
                : "Начать обучение"}
          </Button>
        </Card>
      )}
      {canUseClientDirectory && (
        <Collapse
          items={[
            {
              key: "clients",
              label: "Инструкция: список клиентов",
              children: <ClientDirectoryArticle />,
            },
          ]}
        />
      )}
    </div>
  );
}
