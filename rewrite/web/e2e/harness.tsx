import { useState } from "react";
import { createRoot } from "react-dom/client";
import { StaffSessionProvider, useStaffSession } from "../src/entities/session";
import { createPasskeyRegistration } from "../src/shared/lib/passkeys";
import { DateInput } from "../src/shared/ui/date-time";
import { PaperAccordion } from "../src/shared/ui/paper/PaperAccordion";
import { PaperButton } from "../src/shared/ui/paper/PaperButton";
import { PaperDialog } from "../src/shared/ui/paper/PaperDialog";
import { PaperPagination } from "../src/shared/ui/paper/PaperPagination";
import { usePopupScrollBoundary } from "../src/shared/ui/scroll-boundary";
import "../src/app/styles.css";

function Probe() {
  const session = useStaffSession();
  const [date, setDate] = useState<string | null>("2026-11-15");
  const [result, setResult] = useState("");
  const [pagination, setPagination] = useState<{ page: number; pageSize: number } | null>(null);
  const [motionPreviewOpen, setMotionPreviewOpen] = useState(false);
  usePopupScrollBoundary();
  return (
    <>
      <output aria-label="session">
        {session.status}:{session.staff?.login}
      </output>
      <button
        type="button"
        onClick={() => void session.signInWithPassword("browser.synthetic", "synthetic-password")}
      >
        Sign in
      </button>
      <button type="button" onClick={() => void session.signOut()}>
        Sign out
      </button>
      <button
        type="button"
        onClick={() =>
          void session
            .request(async (api, signal) => {
              const { apiOptions } = await import("../src/shared/api");
              return api.clients.get(apiOptions(signal));
            }, new AbortController().signal)
            .then(
              () => setResult("loaded"),
              () => setResult("cancelled"),
            )
        }
      >
        Read
      </button>
      <button
        type="button"
        onClick={() => {
          const pending = createPasskeyRegistration(
            JSON.stringify({
              challenge: "AQIDBA",
              rp: { name: "Synthetic", id: "localhost" },
              user: { id: "AQID", name: "synthetic", displayName: "Synthetic" },
              pubKeyCredParams: [{ type: "public-key", alg: -7 }],
              authenticatorSelection: { residentKey: "required", userVerification: "preferred" },
            }),
            new AbortController().signal,
          );
          void pending.then(
            () => setResult("created"),
            () => setResult("cancelled"),
          );
        }}
      >
        Create passkey
      </button>
      <output aria-label="result">{result}</output>
      <DateInput value={date} onChange={setDate} />
      <output aria-label="date">{date}</output>
      <PaperButton onClick={() => setMotionPreviewOpen(true)}>Motion preview</PaperButton>
      <PaperDialog
        open={motionPreviewOpen}
        onClose={() => setMotionPreviewOpen(false)}
        title="Motion preview"
      >
        <PaperAccordion
          items={[
            {
              value: "details",
              title: "Preview details",
              content: <PaperButton onClick={() => setMotionPreviewOpen(false)}>Done</PaperButton>,
            },
          ]}
        />
      </PaperDialog>
      <button type="button" onClick={() => setPagination({ page: 1, pageSize: 10 })}>
        Show pagination
      </button>
      {pagination && (
        <>
          <PaperPagination
            {...pagination}
            total={65}
            onChange={(page, pageSize) => setPagination({ page, pageSize })}
          />
          <output aria-label="pagination">
            {pagination.page}:{pagination.pageSize}
          </output>
        </>
      )}
    </>
  );
}
const root = document.getElementById("root");
if (!root) throw new Error("Missing browser test root");
createRoot(root).render(
  <StaffSessionProvider>
    <Probe />
  </StaffSessionProvider>,
);
