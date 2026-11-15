import { ApiError, invalidResponse, isAbortError, isObject } from "../api";

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
): Promise<Record<string, unknown>> {
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

function creationOptions(optionsJson: string): PublicKeyCredentialCreationOptions {
  const value: unknown = JSON.parse(optionsJson);
  if (
    !isObject(value) ||
    typeof value.challenge !== "string" ||
    !isObject(value.rp) ||
    typeof value.rp.name !== "string" ||
    !isObject(value.user) ||
    typeof value.user.id !== "string" ||
    typeof value.user.name !== "string" ||
    typeof value.user.displayName !== "string" ||
    !Array.isArray(value.pubKeyCredParams)
  )
    throw invalidResponse();
  const pubKeyCredParams = value.pubKeyCredParams.map(
    (entry: unknown): PublicKeyCredentialParameters => {
      if (
        !isObject(entry) ||
        entry.type !== "public-key" ||
        typeof entry.alg !== "number" ||
        !Number.isInteger(entry.alg)
      )
        throw invalidResponse();
      return { type: "public-key", alg: entry.alg };
    },
  );
  let excludeCredentials: PublicKeyCredentialDescriptor[] | undefined;
  if (value.excludeCredentials !== undefined) {
    if (!Array.isArray(value.excludeCredentials)) throw invalidResponse();
    excludeCredentials = value.excludeCredentials.map(
      (entry: unknown): PublicKeyCredentialDescriptor => {
        if (!isObject(entry) || entry.type !== "public-key" || typeof entry.id !== "string")
          throw invalidResponse();
        return { type: "public-key", id: decode(entry.id) };
      },
    );
  }
  const authenticatorSelection: AuthenticatorSelectionCriteria = {};
  if (value.authenticatorSelection !== undefined) {
    if (!isObject(value.authenticatorSelection)) throw invalidResponse();
    const { authenticatorAttachment, residentKey, userVerification, requireResidentKey } =
      value.authenticatorSelection;
    if (authenticatorAttachment !== undefined && authenticatorAttachment !== null) {
      if (authenticatorAttachment !== "platform" && authenticatorAttachment !== "cross-platform")
        throw invalidResponse();
      authenticatorSelection.authenticatorAttachment = authenticatorAttachment;
    }
    if (residentKey !== undefined && residentKey !== null) {
      if (
        residentKey !== "required" &&
        residentKey !== "preferred" &&
        residentKey !== "discouraged"
      )
        throw invalidResponse();
      authenticatorSelection.residentKey = residentKey;
    }
    if (userVerification !== undefined && userVerification !== null) {
      if (
        userVerification !== "required" &&
        userVerification !== "preferred" &&
        userVerification !== "discouraged"
      )
        throw invalidResponse();
      authenticatorSelection.userVerification = userVerification;
    }
    if (requireResidentKey !== undefined && requireResidentKey !== null) {
      if (typeof requireResidentKey !== "boolean") throw invalidResponse();
      authenticatorSelection.requireResidentKey = requireResidentKey;
    }
  }
  const attestation = value.attestation;
  if (
    attestation !== undefined &&
    attestation !== "none" &&
    attestation !== "direct" &&
    attestation !== "indirect" &&
    attestation !== "enterprise"
  )
    throw invalidResponse();
  return {
    challenge: decode(value.challenge),
    rp: { name: value.rp.name, ...(typeof value.rp.id === "string" ? { id: value.rp.id } : {}) },
    user: { id: decode(value.user.id), name: value.user.name, displayName: value.user.displayName },
    pubKeyCredParams,
    authenticatorSelection,
    ...(excludeCredentials ? { excludeCredentials } : {}),
    ...(attestation ? { attestation } : {}),
    ...(typeof value.timeout === "number" ? { timeout: value.timeout } : {}),
  };
}

export async function createPasskeyRegistration(
  optionsJson: string,
  signal: AbortSignal,
): Promise<Record<string, unknown>> {
  if (!supportsPasskeys() || !navigator.credentials.create)
    throw new ApiError("Ключи доступа недоступны. Выберите пароль и аутентификатор.");
  let publicKey: PublicKeyCredentialCreationOptions;
  try {
    publicKey = creationOptions(optionsJson);
  } catch {
    throw invalidResponse();
  }
  try {
    // The prepared button calls this before any network await, preserving Safari's gesture.
    const pending = navigator.credentials.create({ publicKey, signal });
    const credential = await pending;
    if (
      !(credential instanceof PublicKeyCredential) ||
      !(credential.response instanceof AuthenticatorAttestationResponse)
    )
      throw invalidResponse();
    return {
      id: credential.id,
      rawId: encode(credential.rawId),
      type: credential.type,
      response: {
        attestationObject: encode(credential.response.attestationObject),
        clientDataJSON: encode(credential.response.clientDataJSON),
        transports:
          typeof credential.response.getTransports === "function"
            ? credential.response.getTransports()
            : [],
      },
      clientExtensionResults: credential.getClientExtensionResults(),
    };
  } catch (error) {
    if (isAbortError(error) || error instanceof ApiError) throw error;
    if (error instanceof DOMException && error.name === "NotAllowedError")
      throw new ApiError("Создание ключа отменено или истекло. Повторите попытку.");
    throw new ApiError(
      "Не удалось создать ключ доступа. Выберите другой способ или повторите попытку.",
    );
  }
}
