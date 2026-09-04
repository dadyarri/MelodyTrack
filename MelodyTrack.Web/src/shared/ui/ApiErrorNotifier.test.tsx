import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { ApiErrorNotifier } from "./ApiErrorNotifier";

describe("ApiErrorNotifier", () => {
  it("lets the user dismiss a request error", async () => {
    const queryClient = new QueryClient({ defaultOptions: { mutations: { retry: false } } });
    render(
      <QueryClientProvider client={queryClient}>
        <ApiErrorNotifier />
      </QueryClientProvider>,
    );
    const mutation = queryClient.getMutationCache().build(queryClient, {
      mutationFn: () => Promise.reject(new Error("Сбой запроса")),
    });

    await mutation.execute(undefined).catch(() => undefined);

    expect(await screen.findByText("Сбой запроса")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Закрыть уведомление об ошибке" }));
    expect(screen.queryByLabelText("Ошибка приложения")).not.toBeInTheDocument();
  });
});
