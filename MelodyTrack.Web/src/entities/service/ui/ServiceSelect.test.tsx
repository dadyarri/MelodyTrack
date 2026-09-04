import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { describe, expect, it, vi } from "vitest";

import { serviceQueryKeys } from "../api/queryKeys";
import { ServiceSelect } from "./ServiceSelect";

describe("ServiceSelect", () => {
  it("does not resolve an unchanged selection again when callback identity changes", async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const service = {
      id: "service-1",
      name: "Lesson",
      description: null,
      publicName: null,
      isConsultation: false,
      price: 1500,
    };
    queryClient.setQueryData(serviceQueryKeys.reference, [service]);
    queryClient.setQueryData(serviceQueryKeys.selected(service.id), service);
    const onResolvedPriceChange = vi.fn();
    const Wrapper = ({ children }: { children: ReactNode }) => <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
    const { rerender } = render(
      <ServiceSelect
        value={service.id}
        onResolvedPriceChange={(price) => {
          onResolvedPriceChange(price);
        }}
      />,
      { wrapper: Wrapper },
    );

    await waitFor(() => {
      expect(onResolvedPriceChange).toHaveBeenCalledTimes(1);
      expect(onResolvedPriceChange).toHaveBeenLastCalledWith(1500);
    });

    rerender(
      <ServiceSelect
        value={service.id}
        onResolvedPriceChange={(price) => {
          onResolvedPriceChange(price);
        }}
      />,
    );
    expect(onResolvedPriceChange).toHaveBeenCalledTimes(1);
  });
});
