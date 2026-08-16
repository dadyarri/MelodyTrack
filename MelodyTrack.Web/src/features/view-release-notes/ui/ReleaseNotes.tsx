import { Button, Empty, Modal, Pagination, Skeleton, Space, Typography } from "antd";
import { type ReactNode, useState } from "react";

import type { ReleaseChanges, ReleaseEntry } from "@/entities/release";

import { useReleaseHistory } from "../model/useReleaseHistory";
import styles from "./ReleaseNotes.module.css";

const changeLabels: Array<[keyof ReleaseChanges, string]> = [
  ["new", "Добавлено"],
  ["improved", "Улучшения"],
  ["fixed", "Исправления"],
  ["security", "Безопасность"],
];

export function ReleaseVersion({ compact = false }: { compact?: boolean }) {
  const query = useReleaseHistory();
  const version = query.data?.currentVersion;
  const label = version ? (compact ? `v${version}` : `Версия ${version}`) : compact ? "—" : "Версия недоступна";

  return (
    <Typography.Text type="secondary" title={version ? `Версия ${version}` : "Версия недоступна"}>
      {label}
    </Typography.Text>
  );
}

export function ReleaseNotesModal({
  open,
  automaticReleases = null,
  onClose,
}: {
  open: boolean;
  automaticReleases?: ReleaseEntry[] | null;
  onClose: () => void;
}) {
  return (
    <Modal
      open={open}
      title="Что нового"
      classNames={{ container: styles.modalCard, body: styles.modalBody }}
      footer={
        <Button type="primary" onClick={onClose}>
          Понятно
        </Button>
      }
      onCancel={onClose}
    >
      <div className={styles.modalContent} data-release-notes-content>
        {open ? automaticReleases ? <ReleaseNotesContent releases={automaticReleases} /> : <ReleaseHistory /> : null}
      </div>
    </Modal>
  );
}

function ReleaseHistory() {
  const [page, setPage] = useState(1);
  const query = useReleaseHistory(page);

  return (
    <ReleaseNotesContent query={query} releases={query.data?.releases ?? []}>
      {query.data && query.data.totalPages > 1 ? (
        <Pagination
          current={query.data.page}
          pageSize={query.data.pageSize}
          total={query.data.totalCount}
          showSizeChanger={false}
          onChange={setPage}
        />
      ) : null}
    </ReleaseNotesContent>
  );
}

function ReleaseNotesContent({
  query,
  releases,
  children,
}: {
  query?: ReturnType<typeof useReleaseHistory>;
  releases: ReleaseEntry[];
  children?: ReactNode;
}) {
  return query?.isPending ? (
    <Skeleton active paragraph={{ rows: 4 }} />
  ) : query?.isError ? (
    <Empty description="Не удалось загрузить историю обновлений" />
  ) : (
    <Space orientation="vertical" size={24} className="wide">
      {releases.map((release) => (
        <ReleaseSection key={release.version} release={release} />
      ))}
      {children}
    </Space>
  );
}

function ReleaseSection({ release }: { release: ReleaseEntry }) {
  return (
    <section>
      <Typography.Title level={4}>
        {release.parentVersion ? "Исправление " : ""}
        {release.version} — {release.codename}
      </Typography.Title>
      <br />
      <Typography.Text type="secondary">
        {new Intl.DateTimeFormat("ru-RU", { dateStyle: "long" }).format(new Date(`${release.date}T00:00:00`))}
      </Typography.Text>
      {changeLabels.map(([key, label]) =>
        release.changes[key].length > 0 ? (
          <div key={key}>
            <Typography.Text strong>{label}</Typography.Text>
            <ul>
              {release.changes[key].map((change) => (
                <li key={change}>{change}</li>
              ))}
            </ul>
          </div>
        ) : null,
      )}
    </section>
  );
}
