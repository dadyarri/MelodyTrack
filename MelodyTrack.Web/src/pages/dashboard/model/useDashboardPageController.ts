import { keepPreviousData, useMutation, useQuery } from "@tanstack/react-query";
import dayjs from "dayjs";
import { useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router";

import { type Client, clientQueryKeys, clientsApi } from "@/entities/client";
import { analyticsQueryKeys, dashboardApi } from "@/entities/dashboard";
import { hasStatsAccess, useAuth } from "@/entities/session";
import { downloadBlob, isShortcutTarget, matchesPlainKey } from "@/shared/lib";
import { getClientHistoryActions } from "@/widgets/client-history";

const clientHistoryEventsPageSize = 8;

export function useDashboardPageController() {
  const auth = useAuth();
  const navigate = useNavigate();
  const [historyClient, setHistoryClient] = useState<Client | null>(null);
  const [historyEventsPage, setHistoryEventsPage] = useState(1);
  const timezone = Intl.DateTimeFormat().resolvedOptions().timeZone;
  const canSeeOrganization = hasStatsAccess(auth.user);
  const historyClientId = historyClient?.id;
  const dashboardQuery = useQuery({
    queryKey: analyticsQueryKeys.stats(timezone),
    queryFn: () => dashboardApi.stats(timezone),
  });
  const debtorsQuery = useQuery({
    queryKey: clientQueryKeys.debtors,
    queryFn: () => clientsApi.debtors(),
    enabled: canSeeOrganization,
  });
  const historyQuery = useQuery({
    queryKey: clientQueryKeys.history(historyClientId, historyEventsPage, clientHistoryEventsPageSize),
    queryFn: () => {
      if (!historyClientId) {
        throw new Error("History client is not selected.");
      }

      return clientsApi.history(historyClientId, {
        page: historyEventsPage,
        page_size: clientHistoryEventsPageSize,
      });
    },
    enabled: Boolean(historyClientId),
    placeholderData: keepPreviousData,
  });
  const debtorsExportMutation = useMutation({
    mutationFn: () => clientsApi.exportDebtors(),
    onSuccess: (blob) => {
      downloadBlob(blob, `debtors_${dayjs().format("YYYYMMDD_HHmmss")}.xlsx`);
    },
  });
  const exportDebtors = debtorsExportMutation.mutate;
  const clientHistoryActions = useMemo(() => getClientHistoryActions(auth.user, navigate), [auth.user, navigate]);

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.defaultPrevented || event.repeat || isShortcutTarget(event.target)) {
        return;
      }

      if (!matchesPlainKey(event, "x") || !canSeeOrganization) {
        return;
      }

      event.preventDefault();
      exportDebtors();
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => {
      window.removeEventListener("keydown", handleKeyDown);
    };
  }, [canSeeOrganization, exportDebtors]);

  const openHistoryClient = (client: Client) => {
    setHistoryEventsPage(1);
    setHistoryClient(client);
  };

  const closeHistoryClient = () => {
    setHistoryClient(null);
    setHistoryEventsPage(1);
  };

  return {
    canSeeOrganization,
    dashboardQuery,
    debtorsQuery,
    debtorsExportMutation,
    historyClient,
    historyEventsPage,
    historyQuery,
    clientHistoryActions,
    setHistoryClient: openHistoryClient,
    closeHistoryClient,
    setHistoryEventsPage,
    retry: () => dashboardQuery.refetch(),
  };
}
