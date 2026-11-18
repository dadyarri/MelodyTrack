import { type BrowserContext, expect, type Page, test } from "@playwright/test";
import { startCookieServer } from "./cookie-server";

const staffId = "019a2c63-8c00-7000-8000-000000000001";
const servers = new WeakMap<BrowserContext, Awaited<ReturnType<typeof startCookieServer>>>();

test("applies the stored dark theme before the application module runs", async ({ page }) => {
  await page.emulateMedia({ colorScheme: "light" });
  await page.addInitScript(() => {
    localStorage.setItem("melodytrack.rewrite.appearance", "dark");
  });
  await page.route("**/src/main.tsx", (route) => route.abort());

  await page.goto("/");

  await expect(page.locator("html")).toHaveAttribute("data-appearance", "dark");
  await expect(page.locator("html")).toHaveCSS("color-scheme", "dark");
  await expect(page.locator("html")).toHaveCSS("background-color", "rgb(21, 18, 15)");
  await expect(page.locator('meta[name="theme-color"]')).toHaveAttribute("content", "#15120f");
  await expect(page.locator("#root")).toBeEmpty();
});

test("applies the system theme before startup when preference storage is unavailable", async ({
  page,
}) => {
  await page.emulateMedia({ colorScheme: "dark" });
  await page.addInitScript(() => {
    Object.defineProperty(window, "localStorage", {
      get() {
        throw new DOMException("Storage unavailable", "SecurityError");
      },
    });
  });
  await page.route("**/src/main.tsx", (route) => route.abort());

  await page.goto("/");

  await expect(page.locator("html")).toHaveAttribute("data-appearance", "dark");
  await expect(page.locator("html")).toHaveCSS("color-scheme", "dark");
  await expect(page.locator("#root")).toBeEmpty();
});

test.afterEach(async ({ context }) => {
  await servers.get(context)?.close();
});

// Synthetic HTTP outcomes isolate browser policy from the separately tested PostgreSQL API.
async function installApi(context: BrowserContext) {
  const server = await startCookieServer();
  servers.set(context, server);
  let clientFirstName = "Клиент";
  const client = () => ({
    id: "019a2c63-8c00-7000-8000-000000000002",
    firstName: clientFirstName,
    lastName: "Синтетический",
    middleName: null,
    birthdate: "2001-05-06",
    sourceId: null,
    sourceName: null,
    isArchived: false,
    createdAt: "2026-11-15T22:00:00Z",
    contacts: [
      {
        id: "019a2c63-8c00-7000-8000-000000000003",
        kindId: "019a2c63-8c00-7000-8000-000000000004",
        kindName: "Email",
        format: "email",
        value: "synthetic@example.invalid",
        comment: "Тестовый контакт",
        isPrimary: true,
      },
    ],
  });
  await context.route("**/api/**", async (route) => {
    const path = new URL(route.request().url()).pathname;
    if (!path.startsWith("/api/")) return route.continue();
    // Continue through the browser network stack: WebKit adds Cookie after routing.
    // Ports differ, but localhost + HTTP preserves the cookie's same-site boundary.
    if (path.startsWith("/api/auth/")) return route.continue({ url: `${server.origin}${path}` });
    if (path === "/api/changes/events") return route.continue({ url: `${server.origin}${path}` });
    const json = (body: unknown, headers = {}) => route.fulfill({ json: body, headers });
    if (path.endsWith("/clients/options"))
      return json({
        sources: [{ id: "019a2c63-8c00-7000-8000-000000000005", name: "Сайт" }],
        contactKinds: [],
      });
    if (path === `/api/clients/${client().id}`) return json(client());
    if (path.endsWith("/clients"))
      return json({
        items: [client()],
        totalCount: 1,
        totalPages: 1,
        page: 1,
        pageSize: 10,
      });
    return route.fulfill({ status: 404, json: {} });
  });
  return {
    maximumRestores: server.maximumRestores,
    waitForEventStream: server.waitForEventStream,
    setClientFirstName: (value: string) => {
      clientFirstName = value;
    },
    invalidate: server.invalidate,
  };
}

async function openProbe(page: Page) {
  await page.goto("/e2e/harness.html");
  await expect(page.getByLabel("session", { exact: true })).toHaveText("anonymous:");
}

test("renders multiple pages through Vite and changes the page and page size", async ({
  page,
  context,
}) => {
  await installApi(context);
  await openProbe(page);

  await page.getByRole("button", { name: "Show pagination", exact: true }).click();
  await page.getByRole("button", { name: "Страница 2", exact: true }).click();
  await expect(page.getByLabel("pagination", { exact: true })).toHaveText("2:10");
  await page.getByRole("combobox", { name: "Строк на странице", exact: true }).click();
  await page.getByRole("option", { name: "20 / страница", exact: true }).click();
  await expect(page.getByLabel("pagination", { exact: true })).toHaveText("1:20");
  await expect(page.getByRole("button", { name: "Страница 4", exact: true })).toBeVisible();
});

for (const reducedMotion of ["no-preference", "reduce"] as const) {
  test(`keeps dialogs and expanding content usable with motion preference ${reducedMotion}`, async ({
    page,
    context,
  }) => {
    await installApi(context);
    await page.emulateMedia({ reducedMotion });
    await openProbe(page);
    const trigger = page.getByRole("button", { name: "Motion preview", exact: true });

    await trigger.click();
    const dialog = page.getByRole("dialog", { name: "Motion preview", exact: true });
    await expect(dialog).toBeVisible();
    await expect(dialog).toHaveCSS("opacity", "1");
    await dialog.getByRole("button", { name: "Preview details", exact: true }).click();
    const done = dialog.getByRole("button", { name: "Done", exact: true });
    await expect(done).toBeVisible();
    await done.focus();
    await expect(done).toBeFocused();
    const animations = await dialog.evaluate((element) =>
      element.getAnimations({ subtree: true }).map((animation) => ({
        duration: animation.effect?.getComputedTiming().duration,
        state: animation.playState,
      })),
    );
    if (reducedMotion === "reduce") {
      expect(
        animations.filter(({ duration, state }) => state === "running" && Number(duration) > 1),
      ).toEqual([]);
    }

    await page.keyboard.press("Escape");
    await expect(dialog).toBeHidden();
    await expect(trigger).toBeFocused();
    await trigger.click();
    await expect(dialog).toBeVisible();
    await dialog.getByRole("button", { name: "Закрыть окно", exact: true }).click();
    await expect(dialog).toBeHidden();
    await expect(trigger).toBeFocused();
  });
}

test("persists an HttpOnly cookie across reload and removes it on logout", async ({
  page,
  context,
}) => {
  await installApi(context);
  await openProbe(page);
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await expect(page.getByLabel("session", { exact: true })).toHaveText(
    "authenticated:browser.synthetic",
  );
  const cookie = (await context.cookies()).find((value) => value.name === "synthetic-session");
  expect(cookie).toMatchObject({ httpOnly: true, sameSite: "Strict", path: "/api/auth" });
  expect(await page.evaluate(() => document.cookie)).not.toContain("synthetic-session");
  await page.reload();
  await expect(page.getByLabel("session", { exact: true })).toHaveText(
    "authenticated:browser.synthetic",
  );
  await page.getByRole("button", { name: "Sign out", exact: true }).click();
  await expect(page.getByLabel("session", { exact: true })).toHaveText("anonymous:");
  await page.reload();
  await expect(page.getByLabel("session", { exact: true })).toHaveText("anonymous:");
  expect((await context.cookies()).some((value) => value.name === "synthetic-session")).toBe(false);
  expect(await page.evaluate(() => JSON.stringify({ ...localStorage }))).not.toContain(
    "synthetic-refresh",
  );
});

test("serializes cookie restoration across tabs and delivers account changes", async ({
  page,
  context,
}) => {
  const api = await installApi(context);
  await openProbe(page);
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await expect(page.getByLabel("session", { exact: true })).toHaveText(
    "authenticated:browser.synthetic",
  );
  const second = await context.newPage();
  await Promise.all([page.reload(), second.goto("/e2e/harness.html")]);
  for (const tab of [page, second])
    await expect(tab.getByLabel("session", { exact: true })).toHaveText(
      "authenticated:browser.synthetic",
    );
  expect(api.maximumRestores()).toBe(1);
  await second.getByRole("button", { name: "Sign out", exact: true }).click();
  for (const tab of [page, second])
    await expect(tab.getByLabel("session", { exact: true })).toHaveText("anonymous:");
});

test("merges real IndexedDB transactions from stale tabs without mixing owners", async ({
  page,
  context,
}) => {
  await installApi(context);
  await openProbe(page);
  const second = await context.newPage();
  await openProbe(second);
  const save = async (tab: Page, owner: string, complete: boolean) =>
    tab.evaluate(
      async ({ owner, complete }) => {
        const storage = await import("../src/features/employee-training/model/storage.ts");
        const model = await import("../src/features/employee-training/model/progress.ts");
        const progress = model.createProgress(owner);
        progress.clientDirectory.completed = complete;
        progress.articleRead = complete;
        progress.demoStep = complete ? "completed" : "signed-out";
        await storage.saveTrainingProgress(progress, new AbortController().signal);
      },
      { owner, complete },
    );
  await save(page, "owner-a", true);
  await Promise.all([save(second, "owner-a", false), save(page, "owner-b", false)]);
  await page.reload();
  const result = await page.evaluate(async () => {
    const storage = await import("../src/features/employee-training/model/storage.ts");
    return Promise.all(
      ["owner-a", "owner-b"].map((id) =>
        storage.loadTrainingProgress(id, new AbortController().signal),
      ),
    );
  });
  expect(result[0]).toMatchObject({
    staffId: "owner-a",
    articleRead: true,
    demoStep: "completed",
    clientDirectory: { completed: true },
  });
  expect(result[1]).toMatchObject({
    staffId: "owner-b",
    articleRead: false,
    demoStep: "signed-out",
    clientDirectory: { completed: false },
  });
  expect(
    await page.evaluate(async () => {
      const storage = await import("../src/features/employee-training/model/storage.ts");
      const controller = new AbortController();
      controller.abort();
      try {
        await storage.loadTrainingProgress("owner-a", controller.signal);
        return "unexpected success";
      } catch (error) {
        return (error as DOMException).name;
      }
    }),
  ).toBe("AbortError");
});

test("reports real IndexedDB transaction abort and quota errors instead of success", async ({
  page,
  context,
}) => {
  await installApi(context);
  await openProbe(page);
  for (const failure of ["abort", "quota"]) {
    const outcome = await page.evaluate(async (failure) => {
      const storage = await import("../src/features/employee-training/model/storage.ts");
      const model = await import("../src/features/employee-training/model/progress.ts");
      const original = IDBObjectStore.prototype.put;
      IDBObjectStore.prototype.put = function (...args) {
        if (failure === "quota") throw new DOMException("Synthetic quota", "QuotaExceededError");
        const request = original.apply(this, args);
        this.transaction.abort();
        return request;
      };
      try {
        await storage.saveTrainingProgress(
          model.createProgress("failed-owner"),
          new AbortController().signal,
        );
        return "unexpected success";
      } catch {
        return "rejected";
      } finally {
        IDBObjectStore.prototype.put = original;
      }
    }, failure);
    expect(outcome).toBe("rejected");
  }
  expect(
    await page.evaluate(async () => {
      const storage = await import("../src/features/employee-training/model/storage.ts");
      return storage.loadTrainingProgress("failed-owner", new AbortController().signal);
    }),
  ).toMatchObject({ articleRead: false, clientDirectory: { completed: false } });
});

test("aborts in-flight IndexedDB operations without persisting cancelled progress", async ({
  page,
  context,
}) => {
  await installApi(context);
  await openProbe(page);
  const outcomes = await page.evaluate(async () => {
    const storage = await import("../src/features/employee-training/model/storage.ts");
    const model = await import("../src/features/employee-training/model/progress.ts");
    const outcomes: string[] = [];
    for (const operation of ["load", "save"]) {
      const controller = new AbortController();
      const original = IDBObjectStore.prototype.get;
      IDBObjectStore.prototype.get = function (key) {
        const request = original.call(this, key);
        queueMicrotask(() => controller.abort());
        return request;
      };
      try {
        const progress = model.createProgress("cancelled-owner");
        progress.articleRead = true;
        if (operation === "load")
          await storage.loadTrainingProgress(progress.staffId, controller.signal);
        else await storage.saveTrainingProgress(progress, controller.signal);
        outcomes.push("unexpected success");
      } catch (error) {
        outcomes.push(error instanceof DOMException ? error.name : "wrong error");
      } finally {
        IDBObjectStore.prototype.get = original;
      }
    }
    const progress = await storage.loadTrainingProgress(
      "cancelled-owner",
      new AbortController().signal,
    );
    return { outcomes, articleRead: progress.articleRead };
  });
  expect(outcomes).toEqual({ outcomes: ["AbortError", "AbortError"], articleRead: false });
});

test("keeps the calendar within a narrow viewport and returns keyboard focus", async ({
  page,
  context,
}) => {
  await installApi(context);
  await page.setViewportSize({ width: 390, height: 844 });
  await openProbe(page);
  const input = page.getByRole("textbox", { name: "Дата", exact: true });
  await input.focus();
  await input.press("Alt+ArrowDown");
  const calendar = page.getByRole("dialog", { name: "Календарь" });
  await expect(calendar).toBeVisible();
  const box = await calendar.boundingBox();
  if (!box) throw new Error("Calendar has no visible bounds");
  expect(box.x).toBeGreaterThanOrEqual(0);
  expect(box.x + box.width).toBeLessThanOrEqual(390);
  await page.keyboard.press("ArrowRight");
  await page.keyboard.press("Enter");
  await expect(page.getByLabel("date", { exact: true })).toHaveText("2026-11-16");
  await input.focus();
  await input.press("Alt+ArrowDown");
  await expect(calendar).toBeVisible();
  await expect(calendar.getByRole("button", { name: /16 ноября 2026/ })).toBeFocused();
  await page.keyboard.press("Escape");
  await expect(page.getByRole("button", { name: "Дата: открыть календарь" })).toBeFocused();
});

test("keeps client editor popups keyboard-dismissable inside its mobile drawer", async ({
  page,
  context,
}) => {
  await installApi(context);
  await context.route("**/api/auth/me", (route) =>
    route.fulfill({
      json: {
        staffId,
        login: "browser.synthetic",
        role: "teacher",
        permissions: [
          { permission: "clientsRead", allowed: true, dataScope: "available", timeScope: "any" },
          { permission: "clientsWrite", allowed: true, dataScope: "available", timeScope: "any" },
        ],
      },
    }),
  );
  await page.setViewportSize({ width: 390, height: 844 });
  await page.addInitScript(() => {
    localStorage.setItem("melodytrack.rewrite.appearance", "dark");
  });
  await openProbe(page);
  await page.evaluate(async (staffId) => {
    const storage = await import("../src/features/employee-training/model/storage.ts");
    const model = await import("../src/features/employee-training/model/progress.ts");
    await storage.saveTrainingProgress(
      {
        ...model.createProgress(staffId),
        clientDirectory: { version: 1, completed: false, deferred: true },
      },
      new AbortController().signal,
    );
  }, staffId);
  await page.goto("/clients");
  await page.getByLabel("Логин", { exact: true }).fill("browser.synthetic");
  await page.getByLabel("Пароль", { exact: true }).fill("synthetic-password");
  await page.getByRole("button", { name: "Войти", exact: true }).click();
  await expect(page).toHaveURL(/\/workspace$/);
  await page.getByRole("button", { name: "Открыть меню", exact: true }).click();
  await page
    .getByRole("navigation", { name: "Разделы приложения на телефоне", exact: true })
    .getByRole("link", { name: "Клиенты" })
    .click();
  await page.getByRole("button", { name: "Изменить карточку: Синтетический Клиент" }).click();

  const drawer = page.getByRole("dialog", { name: "Изменить карточку", exact: true });
  await expect(drawer).toBeVisible();
  await expect(page.locator("html")).toHaveAttribute("data-appearance", "dark");
  const drawerBounds = await drawer.boundingBox();
  if (!drawerBounds) throw new Error("Client editor has no visible bounds");
  expect(drawerBounds.width).toBeLessThanOrEqual(390.5);

  const source = drawer.getByRole("combobox", { name: "Источник", exact: true });
  await source.click();
  const options = page.getByRole("listbox");
  await expect(options.getByRole("option", { name: "Сайт", exact: true })).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(options).toBeHidden();
  await expect(drawer).toBeVisible();
  await expect(source).toBeFocused();

  const calendarTrigger = drawer.getByRole("button", {
    name: "Дата рождения: открыть календарь",
    exact: true,
  });
  await calendarTrigger.click();
  const calendar = page.getByRole("dialog", { name: "Календарь", exact: true });
  await expect(calendar).toBeVisible();
  const calendarBounds = await calendar.boundingBox();
  if (!calendarBounds) throw new Error("Calendar has no visible bounds");
  expect(calendarBounds.x).toBeGreaterThanOrEqual(0);
  expect(calendarBounds.x + calendarBounds.width).toBeLessThanOrEqual(390);
  await page.keyboard.press("Escape");
  await expect(calendar).toBeHidden();
  await expect(drawer).toBeVisible();
  await expect(calendarTrigger).toBeFocused();

  await drawer.getByRole("button", { name: "Закрыть", exact: true }).click();
  await expect(drawer).toBeHidden();
  await expect(
    page.getByRole("dialog", { name: "Синтетический Клиент", exact: true }),
  ).toBeVisible();
});

test("calls a synthetic passkey chooser in an active user gesture and handles cancellation", async ({
  page,
  context,
}) => {
  await installApi(context);
  await openProbe(page);
  await page.evaluate(() => {
    // Linux WebKit has no native WebAuthn. This probe measures gesture propagation,
    // while the separate Chromium authenticator scenario exercises native WebAuthn.
    if (!navigator.credentials)
      Object.defineProperty(navigator, "credentials", { value: { get: () => null } });
    if (!window.PublicKeyCredential)
      Object.defineProperty(window, "PublicKeyCredential", { value: class {} });
    Object.defineProperty(navigator.credentials, "create", {
      configurable: true,
      value: () => {
        document.documentElement.dataset.activeGesture = String(navigator.userActivation.isActive);
        return Promise.reject(new DOMException("Synthetic cancellation", "NotAllowedError"));
      },
    });
  });
  await page.getByRole("button", { name: "Create passkey" }).click();
  await expect(page.getByLabel("result", { exact: true })).toHaveText("cancelled");
  expect(await page.evaluate(() => document.documentElement.dataset.activeGesture)).toBe("true");
});

test("registers and authenticates with a virtual passkey", async ({
  page,
  context,
  browserName,
}) => {
  await installApi(context);
  // Keep Chromium's native WebAuthn coverage; WebKit uses Playwright's API interception.
  await openProbe(page);
  if (browserName !== "chromium") {
    // Linux WebKit omits these interfaces; Playwright uses their prototypes for virtual responses.
    await page.evaluate(() => {
      for (const name of [
        "PublicKeyCredential",
        "AuthenticatorAttestationResponse",
        "AuthenticatorAssertionResponse",
      ]) {
        if (!(name in window))
          Object.defineProperty(window, name, { configurable: true, value: class {} });
      }
    });
    await context.credentials.install();
  }
  const cdp = browserName === "chromium" ? await context.newCDPSession(page) : undefined;
  let authenticatorId: string | undefined;
  if (cdp) {
    await cdp.send("WebAuthn.enable");
    ({ authenticatorId } = await cdp.send("WebAuthn.addVirtualAuthenticator", {
      options: {
        protocol: "ctap2",
        transport: "internal",
        hasResidentKey: true,
        hasUserVerification: true,
        isUserVerified: true,
        automaticPresenceSimulation: true,
      },
    }));
  }
  try {
    await page.getByRole("button", { name: "Create passkey" }).click();
    await expect(page.getByLabel("result", { exact: true })).toHaveText("created");
    const credentials =
      cdp && authenticatorId
        ? (await cdp.send("WebAuthn.getCredentials", { authenticatorId })).credentials.map(
            (credential) => ({
              id: Buffer.from(credential.credentialId, "base64").toString("base64url"),
              rpId: credential.rpId,
            }),
          )
        : await context.credentials.get({ rpId: "localhost" });
    expect(credentials).toHaveLength(1);
    expect(credentials[0]?.rpId).toBe("localhost");
    const assertion = await page.evaluate(async () => {
      const { getPasskeyAssertion } = await import("../src/shared/lib/passkeys.ts");
      return getPasskeyAssertion(
        JSON.stringify({ challenge: "BQYHCA", rpId: "localhost", userVerification: "preferred" }),
        new AbortController().signal,
      );
    });
    const credential = credentials[0];
    if (!credential) throw new Error("Virtual authenticator has no credential");
    expect(assertion.id).toBe(credential.id);
    expect(assertion.response).toMatchObject({
      signature: expect.any(String),
      authenticatorData: expect.any(String),
      clientDataJSON: expect.any(String),
    });
    // Playwright's API interception does not implement AbortSignal; retain native coverage.
    if (!cdp || !authenticatorId) return;
    await cdp.send("WebAuthn.setAutomaticPresenceSimulation", { authenticatorId, enabled: false });
    const aborted = await page.evaluate(async () => {
      const { getPasskeyAssertion } = await import("../src/shared/lib/passkeys.ts");
      const controller = new AbortController();
      const pending = getPasskeyAssertion(
        JSON.stringify({ challenge: "CQoLDA", rpId: "localhost" }),
        controller.signal,
      );
      controller.abort();
      return pending.then(
        () => "unexpected success",
        (error: unknown) => (error instanceof DOMException ? error.name : "wrong error"),
      );
    });
    expect(aborted).toBe("AbortError");
  } finally {
    if (cdp && authenticatorId) {
      await cdp.send("WebAuthn.removeVirtualAuthenticator", { authenticatorId });
      await cdp.detach();
    }
  }
});

test("composes sign-in, protected routing, client details and sign-out on a mobile screen", async ({
  page,
  context,
}) => {
  const api = await installApi(context);
  let eventRequests = 0;
  page.on("request", (request) => {
    if (new URL(request.url()).pathname === "/api/changes/events") eventRequests += 1;
  });
  await page.setViewportSize({ width: 390, height: 844 });
  await openProbe(page);
  await page.evaluate(async (staffId) => {
    const storage = await import("../src/features/employee-training/model/storage.ts");
    const model = await import("../src/features/employee-training/model/progress.ts");
    await storage.saveTrainingProgress(
      {
        ...model.createProgress(staffId),
        clientDirectory: { version: 1, completed: false, deferred: true },
      },
      new AbortController().signal,
    );
  }, staffId);
  await page.goto("/clients");
  await expect(page).toHaveURL(/\/sign-in$/);
  await page.getByLabel("Логин", { exact: true }).fill("browser.synthetic");
  await page.getByLabel("Пароль", { exact: true }).fill("synthetic-password");
  await page.getByRole("button", { name: "Показать пароль", exact: true }).click();
  await expect(page.getByLabel("Пароль", { exact: true })).toHaveAttribute("type", "text");
  await page.getByRole("button", { name: "Скрыть пароль", exact: true }).click();
  await expect(page.getByLabel("Пароль", { exact: true })).toHaveAttribute("type", "password");
  await page.getByRole("button", { name: "Войти", exact: true }).click();
  await expect(page).toHaveURL(/\/workspace$/);
  await page.getByRole("button", { name: "Открыть меню", exact: true }).click();
  const menu = page.getByRole("dialog", { name: "Меню", exact: true });
  await expect(menu.getByRole("link", { name: "Клиенты" })).not.toHaveAttribute("title");
  await page
    .getByRole("navigation", { name: "Разделы приложения на телефоне", exact: true })
    .getByRole("link", { name: "Клиенты" })
    .click();
  await expect(menu).not.toBeVisible();
  await expect(page.getByRole("button", { name: "Открыть меню", exact: true })).toBeFocused();
  await expect(page.getByRole("heading", { name: "Клиенты", exact: true })).toBeVisible();
  const search = page.getByRole("searchbox", { name: "ФИО или контакт" });
  await search.fill("Синтетический");
  await expect(page.getByRole("row", { name: /Синтетический Клиент/ })).toBeVisible();
  await page.getByRole("main").evaluate((element) => {
    (element as HTMLElement).style.minHeight = "1600px";
  });
  await page.evaluate(() => window.scrollTo(0, 160));
  const scrollPosition = await page.evaluate(() => window.scrollY);
  expect(scrollPosition).toBeGreaterThan(0);
  await api.waitForEventStream();
  const initialEventRequests = eventRequests;
  await page.clock.install();
  await page.evaluate(() => window.dispatchEvent(new Event("blur")));
  await page.clock.fastForward(120_000);
  const restored = page.waitForResponse(
    (response) => new URL(response.url()).pathname === "/api/auth/staff/restore",
  );
  const identity = page.waitForResponse(
    (response) => new URL(response.url()).pathname === "/api/auth/me",
  );
  await page.evaluate(() => {
    window.dispatchEvent(new Event("focus"));
  });
  await Promise.all([restored, identity]);
  expect(eventRequests).toBe(initialEventRequests);
  api.setClientFirstName("Обновлённый");
  api.invalidate();
  await expect(page.getByRole("row", { name: /Синтетический Обновлённый/ })).toBeVisible();
  await expect(search).toBeFocused();
  await expect(search).toHaveValue("Синтетический");
  expect(await page.evaluate(() => window.scrollY)).toBe(scrollPosition);
  expect(eventRequests).toBe(initialEventRequests);
  const cardLoaded = page.waitForResponse(
    (response) =>
      new URL(response.url()).pathname === "/api/clients/019a2c63-8c00-7000-8000-000000000002" &&
      response.status() === 200,
  );
  await page.getByRole("row", { name: /Синтетический Обновлённый/ }).click();
  await cardLoaded;
  const drawer = page.getByRole("dialog");
  await expect(drawer.getByRole("status").filter({ hasText: "Обновляем карточку…" })).toHaveCount(
    0,
  );
  await expect(drawer.getByRole("alert").filter({ hasText: "Повторить" })).toHaveCount(0);
  await expect(drawer.getByRole("button", { name: "В архив", exact: true })).toBeDisabled();
  await expect(drawer.getByText("Тестовый контакт")).toBeVisible();
  await expect(drawer.getByRole("link", { name: "synthetic@example.invalid" })).toHaveAttribute(
    "href",
    "mailto:synthetic%40example.invalid",
  );
  const box = await drawer.boundingBox();
  if (!box) throw new Error("Client drawer has no visible bounds");
  // Transformed drawer bounds can differ by a subpixel during CSS interpolation.
  expect(box.width).toBeLessThanOrEqual(390.5);
  await drawer.getByRole("button", { name: "Закрыть", exact: true }).click();
  await page.getByRole("button", { name: "Меню пользователя browser.synthetic" }).click();
  await expect(page.getByRole("menuitem", { name: "Локальные данные" })).toHaveCount(0);
  const accountMenu = page.getByRole("button", { name: "Меню пользователя browser.synthetic" });
  await page.getByRole("menuitem", { name: "Тёмная тема" }).click();
  await expect(page.locator("html")).toHaveAttribute("data-appearance", "dark");
  await accountMenu.click();
  await page.getByRole("menuitem", { name: "Выйти" }).click();
  await expect(page).toHaveURL(/\/sign-in$/);
  await page.reload();
  await expect(page.getByRole("button", { name: "Войти", exact: true })).toBeVisible();
});

test("redirects loopback HTML navigation while preserving the route and query", async ({
  request,
  baseURL,
}) => {
  const loopback = new URL(baseURL as string);
  loopback.hostname = "127.0.0.1";
  const response = await request.get(`${loopback.origin}/clients?q=synthetic`, {
    headers: { Accept: "text/html" },
    maxRedirects: 0,
  });
  expect(response.status()).toBe(307);
  expect(response.headers().location).toBe(`${baseURL}/clients?q=synthetic`);
  expect(response.headers()["cache-control"]).toBe("no-store");
  const apiResponse = await request.get(`${loopback.origin}/`, {
    headers: { Accept: "application/json" },
    maxRedirects: 0,
  });
  expect(apiResponse.status()).not.toBe(307);
});

async function containsNativeWheelScrollingAtPopupBoundary(page: Page, context: BrowserContext) {
  await installApi(context);
  await openProbe(page);
  await page.evaluate(() => {
    document.body.style.height = "3000px";
    const popup = document.createElement("div");
    popup.setAttribute("data-testid", "scroll-boundary-popup");
    popup.setAttribute("data-popup-scroll-boundary", "");
    popup.style.cssText =
      "position:fixed;top:100px;left:10px;width:200px;height:100px;overflow:auto";
    popup.innerHTML = '<div style="height:600px">Synthetic options</div>';
    document.body.append(popup);
  });
  await page.mouse.move(60, 150);
  await page.mouse.wheel(0, 100);
  const popup = page.getByTestId("scroll-boundary-popup");
  await expect.poll(() => popup.evaluate((element) => element.scrollTop)).toBeGreaterThan(0);
  await popup.evaluate((element) => {
    element.scrollTop = element.scrollHeight;
  });
  await page.mouse.wheel(0, 300);
  // Two animation frames allow native scrolling to settle without a wall-clock sleep.
  await page.evaluate(
    () => new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve))),
  );
  expect(await page.evaluate(() => window.scrollY)).toBe(0);
}

async function supportsTouchScrollingInPopup(page: Page, context: BrowserContext) {
  await installApi(context);
  await openProbe(page);
  await page.evaluate(() => {
    document.body.style.height = "3000px";
  });
  await page.getByRole("combobox", { name: "Scroll options" }).tap();
  const list = page.getByRole("option").first().locator("..");
  await expect(list).toHaveCSS("overflow-y", "auto");
  await expect(list).toHaveCSS("overscroll-behavior-y", "contain");
  const scrollPosition = await page.evaluate(() => window.scrollY);

  // Untrusted touch events exercise listeners, not native scrolling. Test layout separately.
  const points = await list.evaluate((element) => {
    const bounds = element.getBoundingClientRect();
    for (const type of ["touchstart", "touchmove"]) {
      window.addEventListener(
        type,
        (event) => element.setAttribute(`data-${type}-prevented`, String(event.defaultPrevented)),
        { once: true },
      );
    }
    return { x: bounds.left + bounds.width / 2, startY: bounds.bottom - 20, endY: bounds.top + 20 };
  });
  for (const type of ["touchstart", "touchmove", "touchend"]) {
    const touch = {
      identifier: 1,
      clientX: points.x,
      clientY: type === "touchstart" ? points.startY : points.endY,
    };
    await list.dispatchEvent(type, {
      bubbles: true,
      cancelable: true,
      touches: type === "touchend" ? [] : [touch],
      targetTouches: type === "touchend" ? [] : [touch],
      changedTouches: [touch],
    });
  }
  await expect(list).toHaveAttribute("data-touchstart-prevented", "false");
  await expect(list).toHaveAttribute("data-touchmove-prevented", "false");

  await page.getByRole("option", { name: "Option 40", exact: true }).scrollIntoViewIfNeeded();
  await expect.poll(() => list.evaluate((element) => element.scrollTop)).toBeGreaterThan(0);
  expect(await page.evaluate(() => window.scrollY)).toBe(scrollPosition);
  await page.getByRole("option", { name: "Option 40", exact: true }).tap();
  await expect(page.getByLabel("scroll selection", { exact: true })).toHaveText("39");
  await expect(page.getByRole("combobox", { name: "Scroll options" })).toHaveText("Option 40");
}

test("keeps the popup list scrollable and contains background scrolling", async ({
  page,
  context,
  isMobile,
}) => {
  if (isMobile) await supportsTouchScrollingInPopup(page, context);
  else await containsNativeWheelScrollingAtPopupBoundary(page, context);
});
