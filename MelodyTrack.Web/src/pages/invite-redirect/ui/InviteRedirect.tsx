import { Navigate, useParams } from "react-router";

export function InviteRedirect() {
  const { inviteCode = "" } = useParams();

  return <Navigate to={`/login?inviteCode=${encodeURIComponent(inviteCode)}`} replace />;
}
