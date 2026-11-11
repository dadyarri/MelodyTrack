import { Navigate, Route, Routes } from "react-router";
import { EmployeeTrainingProvider, TrainingWorkspace } from "@/features/employee-training";
import { WorkspacePage } from "@/pages/workspace";
import type { Appearance } from "@/shared/lib/appearance";
import { AppShell } from "@/widgets/app-shell";

export default function AuthenticatedApplication(props: {
  staffId: string;
  appearance: Appearance;
  onToggleAppearance: () => void;
}) {
  return (
    <EmployeeTrainingProvider key={props.staffId} staffId={props.staffId}>
      <Routes>
        <Route
          element={
            <AppShell appearance={props.appearance} onToggleAppearance={props.onToggleAppearance} />
          }
        >
          <Route path="/workspace" element={<WorkspacePage />} />
          <Route path="/learning" element={<TrainingWorkspace />} />
          <Route path="*" element={<Navigate to="/workspace" replace />} />
        </Route>
      </Routes>
    </EmployeeTrainingProvider>
  );
}
