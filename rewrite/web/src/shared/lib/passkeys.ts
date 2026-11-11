import { ApiError, invalidResponse, isObject } from "../api";

function decode(value: string): ArrayBuffer {
  const encoded = value.replace(/-/g, "+").replace(/_/g, "/");
  const binary = atob(encoded + "=".repeat((4 - (encoded.length % 4)) % 4));
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index += 1) bytes[index] = binary.charCodeAt(index);
  return bytes.buffer;
}

function encode(value: ArrayBuffer): string {
  let binary = "";
  for (const byte of new Uint8Array(value)) binary += String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

export function supportsPasskeys(): boolean {
  return (
    typeof window !== "undefined" &&
    window.isSecureContext &&
    typeof PublicKeyCredential !== "undefined" &&
    Boolean(navigator.credentials?.get)
  );
}

function requestOptions(optionsJson: string): PublicKeyCredentialRequestOptions {
  let value: unknown;
  try {
    value = JSON.parse(optionsJson);
  } catch {
    throw invalidResponse();
  }
  if (!isObject(value) || typeof value.challenge !== "string") throw invalidResponse();
  const verification = value.userVerification;
  if (
    verification !== undefined &&
    verification !== "required" &&
    verification !== "preferred" &&
    verification !== "discouraged"
  ) {
    throw invalidResponse();
  }
  let allowCredentials: PublicKeyCredentialDescriptor[] | undefined;
  if (value.allowCredentials !== undefined) {
    if (!Array.isArray(value.allowCredentials)) throw invalidResponse();
    allowCredentials = value.allowCredentials.map(
      (entry: unknown): PublicKeyCredentialDescriptor => {
        if (!isObject(entry) || entry.type !== "public-key" || typeof entry.id !== "string")
          throw invalidResponse();
        return { type: "public-key", id: decode(entry.id) };
      },
    );
  }
  return {
    challenge: decode(value.challenge),
    ...(typeof value.rpId === "string" ? { rpId: value.rpId } : {}),
    ...(typeof value.timeout === "number" ? { timeout: value.timeout } : {}),
    ...(verification ? { userVerification: verification } : {}),
    ...(allowCredentials ? { allowCredentials } : {}),
  };
}

export async function getPasskeyAssertion(
  optionsJson: string,
  signal: AbortSignal,
): Promise<unknown> {
  if (!supportsPasskeys())
    throw new ApiError(
      "Ключи доступа недоступны в этом браузере. Используйте HTTPS или вход по паролю.",
    );
  let options: PublicKeyCredentialRequestOptions;
  try {
    options = requestOptions(optionsJson);
  } catch {
    throw invalidResponse();
  }
  try {
    // Called directly by the confirmation click, before any network await (including on Safari).
    const pending = navigator.credentials.get({ publicKey: options, signal });
    const credential = await pending;
    if (
      !(credential instanceof PublicKeyCredential) ||
      !(credential.response instanceof AuthenticatorAssertionResponse)
    ) {
      throw invalidResponse();
    }
    const response = credential.response;
    return {
      id: credential.id,
      rawId: encode(credential.rawId),
      type: credential.type,
      response: {
        authenticatorData: encode(response.authenticatorData),
        clientDataJSON: encode(response.clientDataJSON),
        signature: encode(response.signature),
        userHandle: response.userHandle ? encode(response.userHandle) : null,
      },
      clientExtensionResults: credential.getClientExtensionResults(),
    };
  } catch (error) {
    if (error instanceof DOMException && error.name === "AbortError") throw error;
    if (error instanceof ApiError) throw error;
    if (error instanceof DOMException && error.name === "NotAllowedError") {
      throw new ApiError("Подтверждение ключом доступа отменено или истекло. Начните вход заново.");
    }
    throw new ApiError(
      "Не удалось использовать ключ доступа. Проверьте устройство или выберите вход по паролю.",
    );
  }
}
