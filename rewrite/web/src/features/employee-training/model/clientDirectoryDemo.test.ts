import { describe, expect, it } from "vitest";
import {
  clientDirectoryExerciseComplete,
  type DemoClientQuery,
  demoNameFragmentMatches,
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

  it("allows up to three edits in any normalized name fragment", () => {
    expect(demoNameFragmentMatches("  БОГДАНОВА   Полина ", "багд")).toBe(true);
    expect(demoNameFragmentMatches("Богданова Полина", "бxxxанова")).toBe(true);
    expect(demoNameFragmentMatches("Богданова Полина", "xxxxанова")).toBe(false);
    expect(demoNameFragmentMatches("Богданова Полина", "гднава")).toBe(true);
    expect(demoNameFragmentMatches("Богданова Полина", "богооданова")).toBe(true);
    expect(demoNameFragmentMatches("Богданова Полина", "нова\tПал")).toBe(true);
    expect(demoNameFragmentMatches("Иванов Антон", "Иванов Артём")).toBe(true);
    expect(demoNameFragmentMatches("Артём", "Артем")).toBe(true);
    expect(demoNameFragmentMatches("😀😀😀😀", "xxxx")).toBe(false);
    expect(demoNameFragmentMatches("😀 Q", "😀😀😀😀")).toBe(true);
    expect(demoNameFragmentMatches("😀 Q", "😀😀😀😀😀")).toBe(false);
    expect(demoNameFragmentMatches("Богданова Полина", "%")).toBe(true);
    expect(demoNameFragmentMatches("Богданова Полина", "%%%%")).toBe(false);
    expect(demoNameFragmentMatches("A".repeat(260), `${"A".repeat(197)}BBB`)).toBe(true);
    expect(demoNameFragmentMatches("A".repeat(260), `${"A".repeat(196)}BBBB`)).toBe(false);
    expect(
      selectDemoClients({ ...target, search: "Иванов Бртём" }).map((client) => client.id),
    ).not.toContain("demo-anton");
  });

  it("finds normalized fragments without interpreting wildcard patterns", () => {
    expect(
      selectDemoClients({ ...target, search: "  АНТОН  " }).map((client) => client.id),
    ).toContain("demo-anton");
    expect(
      selectDemoClients({ ...target, search: "НОВ\tАН" }).map((client) => client.id),
    ).toContain("demo-anton");
    expect(selectDemoClients({ ...target, search: "%%%%" })).toEqual([]);
    expect(selectDemoClients({ ...target, search: "____" })).toEqual([]);
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
