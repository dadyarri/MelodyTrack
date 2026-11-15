import { Navigate, Route, Routes } from "react-router";
import { useStaffSession } from "@/entities/session";
import { EmployeeTrainingProvider, TrainingWorkspace } from "@/features/employee-training";
import { ClientsPage } from "@/pages/clients";
import { WorkspacePage } from "@/pages/workspace";
import type { Appearance } from "@/shared/lib/appearance";
import { AppShell } from "@/widgets/app-shell";

export default function AuthenticatedApplication(props: {
  staffId: string;
  appearance: Appearance;
  onToggleAppearance: () => void;
}) {
  const { staff } = useStaffSession();
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
