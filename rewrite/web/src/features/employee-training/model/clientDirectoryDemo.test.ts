import { describe, expect, it } from "vitest";
import {
  clientDirectoryExerciseComplete,
  type DemoClientQuery,
  demoNameDistance,
  selectDemoClients,
} from "./clientDirectoryDemo";

const target: DemoClientQuery = {
  search: "Иванов Антон",
  source: "recommendation",
  archive: "active",
  direction: "desc",
};

describe("client directory training", () => {
  it("finds the two expected active recommendations in descending full name order", () => {
    const clients = selectDemoClients(target);

    expect(clients.map((client) => client.name)).toEqual(["Иванов Артём", "Иванов Антон"]);
    expect(clientDirectoryExerciseComplete(target, clients)).toBe(true);
  });

  it("keeps archived and differently sourced matches out of the target result", () => {
    const all = selectDemoClients({ ...target, source: "all", archive: "all" });

    expect(all.map((client) => client.id)).toContain("demo-website");
    expect(all.map((client) => client.id)).toContain("demo-archived");
    expect(clientDirectoryExerciseComplete({ ...target, archive: "all" }, all)).toBe(false);
  });

  it("uses whole normalized names with the three edit boundary", () => {
    expect(demoNameDistance("  ИВАНОВ   Антон ", "Иванов Антон")).toBe(0);
    expect(demoNameDistance("Иванов Антон", "Иванов Артём")).toBe(3);
    expect(demoNameDistance("Иванов Антон", "Иванов Бртём")).toBe(4);
    expect(demoNameDistance("Артём", "Артем")).toBe(1);
    expect(demoNameDistance("Иванов 😀", "Иванов")).toBe(2);
    expect(
      selectDemoClients({ ...target, search: "Иванов Бртём" }).map((client) => client.id),
    ).not.toContain("demo-anton");
    expect(selectDemoClients({ ...target, search: "Антон" })).toEqual([]);
  });

  it("requires the demonstrated result as well as the chosen controls", () => {
    const ascending = selectDemoClients({ ...target, direction: "asc" });

    expect(clientDirectoryExerciseComplete(target, ascending)).toBe(false);
    expect(
      clientDirectoryExerciseComplete({ ...target, search: "" }, selectDemoClients(target)),
    ).toBe(false);
    expect(clientDirectoryExerciseComplete(target, [])).toBe(false);
  });
});
