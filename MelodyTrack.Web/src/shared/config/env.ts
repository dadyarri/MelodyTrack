type PublicEnvironment = {
  VITE_API_BASE_URL?: string;
};

export function resolveApiBaseUrl(environment: PublicEnvironment) {
  const value = environment.VITE_API_BASE_URL?.trim();
  if (!value) {
    throw new Error("VITE_API_BASE_URL must be configured.");
  }

  if (value.startsWith("/")) {
    if (value.startsWith("//") || value.includes("?") || value.includes("#")) {
      throw new Error("VITE_API_BASE_URL must be an absolute HTTP(S) URL or a root-relative path without a query or fragment.");
    }

    return removeTrailingSlash(value);
  }

  let url: URL;
  try {
    url = new URL(value);
  } catch {
    throw new Error("VITE_API_BASE_URL must be a valid URL.");
  }

  if (url.protocol !== "http:" && url.protocol !== "https:") {
    throw new Error("VITE_API_BASE_URL must use HTTP or HTTPS.");
  }

  if (url.username || url.password || url.search || url.hash) {
    throw new Error("VITE_API_BASE_URL must not contain credentials, a query, or a fragment.");
  }

  return removeTrailingSlash(url.toString());
}

function removeTrailingSlash(value: string) {
  return value === "/" ? "" : value.replace(/\/+$/, "");
}

export const apiBaseUrl = resolveApiBaseUrl(import.meta.env);
