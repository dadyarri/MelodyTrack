/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_API_BASE_URL: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}

declare module "lucide-react/dist/esm/icons/*" {
  import type { LucideIcon } from "lucide-react";

  const icon: LucideIcon;
  export default icon;
}
