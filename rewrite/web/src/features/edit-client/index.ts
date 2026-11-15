export {
  createClientCard,
  getClientCard,
  getClientNotes,
  restoreClientCard,
  updateClientCard,
  updateClientNotes,
} from "./api/clientCardApi";
export type {
  ClientCardDraft,
  ClientContactDraft,
  ClientContactKind,
} from "./ui/ClientEditorDrawer";
export { ClientEditorDrawer } from "./ui/ClientEditorDrawer";
export type { ClientNoteHistoryEntry } from "./ui/ClientNotesSection";
export { ClientNotesSection } from "./ui/ClientNotesSection";
