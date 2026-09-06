import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, Button, Card, Modal, Spin, Typography } from "antd";
import { useState } from "react";
import { useNavigate } from "react-router";

import { type AliasCandidate, playerAliasApi } from "@/entities/player-alias";
import { authQueryKeys, useAuth } from "@/entities/session";
import { getApiErrorMessage } from "@/shared/api";
import { LogoutOutlined, ReloadOutlined, SaveOutlined } from "@/shared/ui/icons";

import styles from "./ClientPortalAliasPage.module.css";

export function ClientPortalAliasPage() {
  const auth = useAuth();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [candidate, setCandidate] = useState<AliasCandidate | null>(null);
  const [isConfirmationOpen, setConfirmationOpen] = useState(false);
  const initialCandidate = useQuery({
    queryKey: ["portal", "alias-candidate", "initial"],
    queryFn: () => playerAliasApi.getCandidate(),
    retry: false,
  });
  const visibleCandidate = candidate ?? initialCandidate.data;
  const anotherMutation = useMutation({
    mutationFn: (previous: AliasCandidate) => playerAliasApi.getCandidate(previous),
    onSuccess: setCandidate,
  });
  const chooseMutation = useMutation({
    mutationFn: (selectedCandidate: AliasCandidate) => playerAliasApi.choose(selectedCandidate),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: authQueryKeys.me });
      void navigate("/portal", { replace: true });
    },
    onError: () => {
      setConfirmationOpen(false);
    },
  });
  const error = initialCandidate.error ?? anotherMutation.error ?? chooseMutation.error;
  const isBusy = anotherMutation.isPending || chooseMutation.isPending;

  const requestAnotherCandidate = () => {
    if (!visibleCandidate) return;
    chooseMutation.reset();
    anotherMutation.mutate(visibleCandidate);
  };

  const confirmCandidate = () => {
    if (!visibleCandidate) return;
    chooseMutation.mutate(visibleCandidate);
  };

  return (
    <main className={styles.page}>
      <Card className={styles.card}>
        <div className={styles.topBar}>
          <Button type="text" icon={<LogoutOutlined />} onClick={() => void auth.logout()}>
            Выйти
          </Button>
        </div>

        <div className={styles.content}>
          <header className={styles.intro}>
            <Typography.Title level={1} className={styles.title}>
              Выбери имя, под которым прославишься
            </Typography.Title>
          </header>

          <section className={styles.candidateArea} aria-live="polite" aria-busy={initialCandidate.isPending || anotherMutation.isPending}>
            <div className={styles.nameplate}>
              {initialCandidate.isPending ? (
                <Spin size="large" />
              ) : visibleCandidate ? (
                <Typography.Title level={2} className={styles.alias}>
                  {visibleCandidate.value}
                </Typography.Title>
              ) : (
                <Typography.Text className={styles.unavailable}>Имя пока не найдено</Typography.Text>
              )}
              {anotherMutation.isPending ? <Spin size="small" className={styles.refreshSpinner} /> : null}
            </div>

            {error ? <Alert className={styles.error} type="error" showIcon title={getApiErrorMessage(error)} /> : null}
          </section>

          <div className={styles.actions}>
            <Button
              type="primary"
              size="large"
              icon={<SaveOutlined />}
              disabled={!visibleCandidate || isBusy}
              onClick={() => {
                chooseMutation.reset();
                setConfirmationOpen(true);
              }}
            >
              Выбрать имя
            </Button>
            <Button
              size="large"
              icon={<ReloadOutlined />}
              disabled={!visibleCandidate || chooseMutation.isPending}
              loading={anotherMutation.isPending}
              onClick={requestAnotherCandidate}
            >
              Показать другое
            </Button>
          </div>
        </div>
      </Card>

      <Modal
        open={isConfirmationOpen}
        title="Оставить это имя?"
        okText="Да, сохранить имя"
        cancelText="Вернуться"
        confirmLoading={chooseMutation.isPending}
        okButtonProps={{ icon: <SaveOutlined /> }}
        closable={!chooseMutation.isPending}
        mask={{ closable: !chooseMutation.isPending }}
        onOk={confirmCandidate}
        onCancel={() => {
          if (!chooseMutation.isPending) setConfirmationOpen(false);
        }}
      >
        <Typography.Paragraph className={styles.confirmationAlias}>{visibleCandidate?.value}</Typography.Paragraph>
        <Typography.Paragraph>После сохранения изменить игровое имя нельзя.</Typography.Paragraph>
      </Modal>
    </main>
  );
}
