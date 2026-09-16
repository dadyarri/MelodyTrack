import { useQuery } from "@tanstack/react-query";
import { App as AntdApp, Button, Typography } from "antd";
import { useEffect, useState } from "react";

import { notificationApi, notificationQueryKeys } from "@/entities/notification";
import {
  getBrowserPushSubscription,
  preparePushRegistration,
  requestPushPermission,
  serializePushSubscription,
  subscribeBrowserToPush,
  supportsPushNotifications,
} from "@/shared/lib";
import { RpgButton } from "@/shared/ui/rpg";

type PushState = "loading" | "available" | "subscribed" | "denied" | "unsupported" | "not-configured";

export function PushNotificationControl({ appearance = "staff" }: { appearance?: "staff" | "portal" }) {
  const { message } = AntdApp.useApp();
  const configuration = useQuery({
    queryKey: notificationQueryKeys.pushConfiguration(),
    queryFn: ({ signal }) => notificationApi.getPushConfiguration(signal),
    staleTime: 5 * 60_000,
  });
  const [state, setState] = useState<PushState>("loading");
  const [registration, setRegistration] = useState<ServiceWorkerRegistration | null>(null);
  const [pending, setPending] = useState(false);
  const [feedback, setFeedback] = useState("");
  const notify = (kind: "success" | "error", text: string) => {
    if (appearance === "portal") setFeedback(text);
    else void message[kind](text);
  };
  const availabilityState: PushState | null = configuration.isLoading
    ? "loading"
    : !configuration.data?.enabled || !configuration.data.publicKey
      ? "not-configured"
      : !supportsPushNotifications()
        ? "unsupported"
        : Notification.permission === "denied"
          ? "denied"
          : null;
  const displayedState = availabilityState ?? state;

  useEffect(() => {
    if (availabilityState !== null) {
      return;
    }

    let active = true;
    const setStateIfActive = (nextState: PushState) => {
      if (active) {
        setState(nextState);
      }
    };

    void preparePushRegistration()
      .then(async (preparedRegistration) => {
        if (!active || !preparedRegistration) {
          return;
        }
        setRegistration(preparedRegistration);
        const subscription = await getBrowserPushSubscription(preparedRegistration);
        if (subscription) {
          await notificationApi.subscribe(serializePushSubscription(subscription));
        }
        setStateIfActive(subscription ? "subscribed" : "available");
      })
      .catch(() => {
        setStateIfActive("unsupported");
      });

    return () => {
      active = false;
    };
  }, [availabilityState]);

  const enable = () => {
    const permissionPromise = requestPushPermission();
    setPending(true);
    void permissionPromise
      .then(async (permission) => {
        if (permission !== "granted") {
          setState("denied");
          return;
        }

        const currentRegistration = registration ?? (await preparePushRegistration());
        const publicKey = configuration.data?.publicKey;
        if (!currentRegistration || !publicKey) {
          setState("unsupported");
          return;
        }

        const subscription = await subscribeBrowserToPush(currentRegistration, publicKey);
        await notificationApi.subscribe(subscription);
        setRegistration(currentRegistration);
        setState("subscribed");
        notify("success", "Push-уведомления включены.");
      })
      .catch(() => {
        notify("error", "Не удалось включить push-уведомления. Уведомления в приложении продолжат работать.");
      })
      .finally(() => {
        setPending(false);
      });
  };

  const disable = () => {
    setPending(true);
    void (async () => {
      const currentRegistration = registration ?? (await preparePushRegistration());
      const subscription = currentRegistration ? await getBrowserPushSubscription(currentRegistration) : null;
      if (subscription) {
        await notificationApi.revokePushSubscription({ endpoint: subscription.endpoint });
        await subscription.unsubscribe();
      }
      setState("available");
      notify("success", "Push-уведомления отключены.");
    })()
      .catch(() => {
        notify("error", "Не удалось отключить push-уведомления. Попробуйте ещё раз.");
      })
      .finally(() => {
        setPending(false);
      });
  };

  if (appearance === "portal") {
    const explanations: Partial<Record<PushState, string>> = {
      "not-configured": "Push пока не настроен. Уведомления доступны здесь.",
      unsupported: "Этот браузер не поддерживает push. Уведомления доступны здесь.",
      denied: "Push запрещены в настройках браузера. Уведомления доступны здесь.",
    };
    const explanation = explanations[displayedState];
    return (
      <div>
        {explanation ? (
          <p>{explanation}</p>
        ) : (
          <RpgButton
            variant="quiet"
            pending={pending || displayedState === "loading"}
            onClick={displayedState === "subscribed" ? disable : enable}
          >
            {displayedState === "subscribed" ? "Отключить push" : "Включить push"}
          </RpgButton>
        )}
        <p role="status">{feedback}</p>
      </div>
    );
  }

  if (displayedState === "not-configured") {
    return <Typography.Text type="secondary">Push пока не настроен. Уведомления доступны здесь.</Typography.Text>;
  }
  if (displayedState === "unsupported") {
    return <Typography.Text type="secondary">Этот браузер не поддерживает push. Уведомления доступны здесь.</Typography.Text>;
  }
  if (displayedState === "denied") {
    return <Typography.Text type="secondary">Push запрещены в настройках браузера. Уведомления доступны здесь.</Typography.Text>;
  }
  if (displayedState === "subscribed") {
    return (
      <Button type="link" size="small" loading={pending} onClick={disable}>
        Отключить push
      </Button>
    );
  }

  return (
    <Button
      type="link"
      size="small"
      loading={pending || displayedState === "loading"}
      disabled={displayedState === "loading"}
      onClick={enable}
    >
      Включить push
    </Button>
  );
}
