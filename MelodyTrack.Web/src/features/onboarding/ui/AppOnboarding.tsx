import { App, Button, Tour, Typography } from "antd";
import { useLayoutEffect } from "react";

import { findOnboardingTarget } from "../model/targets";
import { type OnboardingController, useOnboardingController } from "../model/useOnboardingController";
import styles from "./AppOnboarding.module.css";

export type OnboardingDisplayStatus = "loading" | "active" | "idle";

export function AppOnboarding({ onStatusChange }: { onStatusChange?: (status: OnboardingDisplayStatus) => void } = {}) {
  const { message } = App.useApp();
  const controller = useOnboardingController({
    onCompleted: () => {
      void message.success("Готово! Теперь можно начинать работу.");
    },
  });
  const status = controller.isLoading ? "loading" : controller.isActive ? "active" : "idle";

  useLayoutEffect(() => {
    onStatusChange?.(status);
  }, [onStatusChange, status]);

  return <OnboardingTour controller={controller} />;
}

export function OnboardingTour({ controller }: { controller: OnboardingController }) {
  const steps = controller.steps.map((step) => {
    const targetId = step.targetId;
    return {
      title: step.title,
      description: step.description,
      placement: step.placement,
      target: targetId ? findOnboardingTarget(targetId) : null,
    };
  });

  return (
    <Tour
      rootClassName={styles.tour}
      open={controller.open}
      current={controller.currentStepIndex}
      steps={steps}
      onClose={controller.skip}
      onChange={controller.changeStep}
      mask
      closable={{ "aria-label": "Пропустить экскурсию" }}
      disabledInteraction={false}
      scrollIntoViewOptions={false}
      indicatorsRender={() => null}
      actionsRender={(_, info) => {
        const isLastStep = info.current === info.total - 1;

        return (
          <div className={styles.footer}>
            {controller.hasError ? (
              <div className={styles.error} role="alert">
                <Typography.Text type="danger">Не получилось продолжить экскурсию.</Typography.Text>
                <Button size="small" onClick={controller.retry}>
                  Повторить
                </Button>
              </div>
            ) : null}

            <div className={styles.footerRow}>
              <Typography.Text type="secondary" className={styles.progress}>
                {info.current + 1} из {info.total}
              </Typography.Text>

              <div className={styles.actions}>
                <Button onClick={controller.skip} disabled={controller.isBusy}>
                  Пропустить
                </Button>
                {info.current > 0 ? (
                  <Button
                    onClick={() => {
                      controller.changeStep(info.current - 1);
                    }}
                    disabled={controller.isBusy}
                  >
                    Назад
                  </Button>
                ) : null}
                <Button
                  type="primary"
                  onClick={() => {
                    if (isLastStep) {
                      controller.complete();
                    } else {
                      controller.changeStep(info.current + 1);
                    }
                  }}
                  disabled={controller.isBusy}
                >
                  {isLastStep ? "Готово" : "Далее"}
                </Button>
              </div>
            </div>
          </div>
        );
      }}
    />
  );
}
