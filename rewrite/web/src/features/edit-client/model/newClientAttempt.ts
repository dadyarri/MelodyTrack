import type { OfflineStorageContextValue } from "@/entities/offline-storage";
import { ApiError } from "@/shared/api";
import { uuidv7 } from "@/shared/lib/uuidv7";
import type { ClientCardDraft } from "../ui/ClientEditorDrawer";
import { isNewClientCommand, type NewClientCommandDraft } from "./newClientDraft";

type Storage = Pick<OfflineStorageContextValue, "get" | "put" | "remove">;
export const newClientCommandKey = "new-client-command";

export async function prepareNewClientAttempt(
  storage: Storage,
  draft: ClientCardDraft,
): Promise<NewClientCommandDraft> {
  const saved = await storage.get(newClientCommandKey);
  if (saved !== undefined) {
    if (!isNewClientCommand(saved))
      throw new ApiError("Сохранённая отправка имеет неизвестный формат. Данные не изменены.");
    if (JSON.stringify(saved.draft) !== JSON.stringify(draft))
      throw new ApiError(
        "Предыдущая отправка ещё не подтверждена. Закройте и откройте форму, чтобы повторить её исходные данные.",
      );
    return saved;
  }
  const command = { commandId: uuidv7(), clientId: uuidv7(), draft };
  await storage.put(newClientCommandKey, { schemaVersion: 1, ...command }, "pending");
  return command;
}

export async function acknowledgeNewClientAttempt(
  storage: Storage,
  command: NewClientCommandDraft,
): Promise<void> {
  await storage.put(
    `client-create:${command.commandId}`,
    { schemaVersion: 1, ...command },
    "applied",
  );
  // Clear the form backup before the pending command: interruption can then only replay the same ID.
  await storage.remove("new-client-draft");
  await storage.remove(newClientCommandKey);
}
