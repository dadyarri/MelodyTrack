import { useEffect } from "react";
import { Navigate, Route, Routes } from "react-router";
import { useStaffSession } from "@/entities/session";
import { EmployeeTrainingProvider, TrainingWorkspace } from "@/features/employee-training";
import { ClientsPage } from "@/pages/clients";
import { WorkspacePage } from "@/pages/workspace";
import { emitDataChange, openDataChanges, startDataChanges } from "@/shared/api/live-data";
import type { Appearance } from "@/shared/lib/appearance";
import { AppShell } from "@/widgets/app-shell";

export default function AuthenticatedApplication(props: {
  staffId: string;
  appearance: Appearance;
  onToggleAppearance: () => void;
}) {
  const { staff, request, synchronizeIdentity } = useStaffSession();
  useEffect(() => {
    const controller = new AbortController();
    const synchronize = async () => {
      await synchronizeIdentity(controller.signal);
      if (!controller.signal.aborted) emitDataChange();
    };
    const stop = startDataChanges((signal) => request(openDataChanges, signal), synchronize);
    return () => {
      controller.abort();
      stop();
    };
  }, [request, synchronizeIdentity]);
  return (
    <EmployeeTrainingProvider
      key={props.staffId}
      staffId={props.staffId}
      canUseClientDirectory={
        staff?.permissions.some(
          (permission) => permission.permission === "clientsRead" && permission.allowed,
        ) === true
      }
    >
      <Routes>
        <Route
          element={
            <AppShell appearance={props.appearance} onToggleAppearance={props.onToggleAppearance} />
          }
        >
          <Route path="/workspace" element={<WorkspacePage />} />
          <Route path="/clients" element={<ClientsPage />} />
          <Route path="/learning" element={<TrainingWorkspace />} />
          <Route path="*" element={<Navigate to="/workspace" replace />} />
        </Route>
      </Routes>
    </EmployeeTrainingProvider>
  );
}
