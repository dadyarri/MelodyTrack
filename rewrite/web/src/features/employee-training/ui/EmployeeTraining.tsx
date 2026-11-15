import { Button, Collapse, Modal, Spin, Typography } from "antd";
import { createContext, type ReactNode, useContext, useEffect, useRef, useState } from "react";
import { useErrorNotification } from "@/shared/ui/notifications";
import { createProgress, type TrainingProgress } from "../model/progress";
import { loadTrainingProgress, saveTrainingProgress } from "../model/storage";
import {
  ClientDirectoryArticle,
  ClientDirectoryExercise,
  type ClientTrainingTopic,
  clientTrainingTitles,
} from "./ClientDirectoryTraining";
import "./employee-training.css";

const { Paragraph, Title } = Typography;

interface TrainingContextValue {
  progress: TrainingProgress | null;
  ready: boolean;
  active: boolean;
  storageAvailable: boolean;
  canUseClientDirectory: boolean;
  openClientTraining: () => void;
  openClientTrainingTopic: (topic: ClientTrainingTopic) => void;
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
  const [clientReplay, setClientReplay] = useState(0);
  const [clientTopic, setClientTopic] = useState<ClientTrainingTopic>("search");
  const completedTopics = useRef(new Set<ClientTrainingTopic>());
  const current = useRef<TrainingProgress | null>(null);
  const lifetime = useRef<AbortController | null>(null);
  const canPersist = useRef(true);
  const writes = useRef<Promise<void>>(Promise.resolve());
  const visibleModal = modalOpen && canUseClientDirectory;

  useErrorNotification(
    storageError
      ? storageError === "load"
        ? "Не удалось загрузить сохранённый прогресс обучения."
        : "Не удалось сохранить прогресс обучения."
      : null,
    { title: "Ошибка локального хранилища" },
  );

  useEffect(() => {
    if (!canUseClientDirectory) setModalOpen(false);
  }, [canUseClientDirectory]);

  useEffect(() => {
    if (
      progress &&
      canUseClientDirectory &&
      !progress.clientDirectory.completed &&
      !progress.clientDirectory.deferred
    ) {
      setModalOpen(true);
    }
  }, [canUseClientDirectory, progress]);

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

  function openClientTrainingTopic(topic: ClientTrainingTopic) {
    if (!canUseClientDirectory) return;
    setClientTopic(topic);
    setClientReplay((value) => value + 1);
    setModalOpen(true);
  }

  const value: TrainingContextValue = {
    progress,
    ready: progress !== null,
    active: visibleModal,
    storageAvailable,
    canUseClientDirectory,
    openClientTraining: () => openClientTrainingTopic("search"),
    openClientTrainingTopic,
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
    if (progress && !progress.clientDirectory.completed) {
      update((record) => ({
        ...record,
        clientDirectory: { ...record.clientDirectory, deferred: true },
      }));
    }
    setModalOpen(false);
  }

  return (
    <TrainingContext.Provider value={value}>
      {progress ? (
        <div inert={visibleModal && !progress.clientDirectory.completed}>{children}</div>
      ) : (
        <div className="employee-training-loading" role="status" aria-live="polite">
          <Spin />
          <span>Загрузка…</span>
        </div>
      )}
      {canUseClientDirectory && (
        <Modal
          className="employee-training-modal"
          style={{ maxWidth: "100vw" }}
          title={`Список клиентов · ${clientTrainingTitles[clientTopic]}`}
          open={Boolean(progress) && visibleModal}
          closable={false}
          mask={{ closable: false }}
          keyboard={false}
          width={1040}
          footer={[
            <Button key="defer" onClick={closeTraining}>
              {progress?.clientDirectory.completed ? "К работе" : "Отложить"}
            </Button>,
          ]}
        >
          <ClientDirectoryExercise
            key={clientReplay}
            topic={clientTopic}
            onStartTraining={openClientTrainingTopic}
            onComplete={() => {
              completedTopics.current.add(clientTopic);
              if (completedTopics.current.has("search") && completedTopics.current.has("sorting")) {
                value.completeClientTraining();
              }
            }}
          />
        </Modal>
      )}
      {progress &&
        canUseClientDirectory &&
        !visibleModal &&
        progress.clientDirectory.deferred &&
        !progress.clientDirectory.completed && (
          <div className="employee-training-resume">
            <Button type="primary" size="large" onClick={value.openClientTraining}>
              Продолжить обучение
            </Button>
          </div>
        )}
    </TrainingContext.Provider>
  );
}

export function TrainingWorkspace() {
  const { canUseClientDirectory, openClientTrainingTopic } = useEmployeeTraining();
  return (
    <div className="employee-training-workspace">
      <div>
        <Title level={2}>Обучение</Title>
      </div>
      {canUseClientDirectory && (
        <Collapse
          items={[
            {
              key: "clients",
              label: "Список клиентов",
              children: (
                <div className="employee-training-topic">
                  <Paragraph>
                    Здесь собраны правила поиска, фильтрации, сортировки и работы с карточкой
                    клиента. Каждый раздел раскрывается отдельно.
                  </Paragraph>
                  <ClientDirectoryArticle onStartTraining={openClientTrainingTopic} />
                </div>
              ),
            },
          ]}
        />
      )}
    </div>
  );
}
