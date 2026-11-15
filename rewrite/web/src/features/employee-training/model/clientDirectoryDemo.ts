export type DemoClient = {
  id: string;
  name: string;
  source: "recommendation" | "website";
  archived: boolean;
};

export const demoClients: readonly DemoClient[] = [
  { id: "demo-anton", name: "Иванов Антон", source: "recommendation", archived: false },
  { id: "demo-artyom", name: "Иванов Артём", source: "recommendation", archived: false },
  { id: "demo-website", name: "Иванов Артем", source: "website", archived: false },
  { id: "demo-archived", name: "Иванов Антон", source: "recommendation", archived: true },
  { id: "demo-zorina", name: "Зорина Инна", source: "recommendation", archived: false },
];

export type DemoClientQuery = {
  search: string;
  source: "all" | "recommendation" | "website";
  archive: "all" | "active" | "archived";
  direction: "asc" | "desc";
};

function normalizedName(value: string): string {
  return value.trim().replace(/\s+/gu, " ").toLocaleLowerCase("ru-RU");
}

/** Local emulation only: the working directory performs its name search in PostgreSQL. */
export function demoNameDistance(first: string, second: string): number {
  const left = Array.from(normalizedName(first));
  const right = Array.from(normalizedName(second));
  let previous = Array.from({ length: right.length + 1 }, (_, index) => index);
  for (let row = 1; row <= left.length; row += 1) {
    const current = [row];
    for (let column = 1; column <= right.length; column += 1) {
      current[column] = Math.min(
        (previous[column] ?? Number.POSITIVE_INFINITY) + 1,
        (current[column - 1] ?? Number.POSITIVE_INFINITY) + 1,
        (previous[column - 1] ?? Number.POSITIVE_INFINITY) +
          (left[row - 1] === right[column - 1] ? 0 : 1),
      );
    }
    previous = current;
  }
  return previous[right.length] ?? left.length;
}

export function selectDemoClients(query: DemoClientQuery): DemoClient[] {
  const search = normalizedName(query.search);
  return demoClients
    .filter(
      (client) =>
        (!search ||
          normalizedName(client.name).includes(search) ||
          demoNameDistance(client.name, search) <= 3) &&
        (query.source === "all" || client.source === query.source) &&
        (query.archive === "all" || client.archived === (query.archive === "archived")),
    )
    .sort(
      (left, right) =>
        left.name.localeCompare(right.name, "ru-RU") * (query.direction === "asc" ? 1 : -1),
    );
}

export function clientDirectoryExerciseComplete(
  query: DemoClientQuery,
  visibleClients: readonly DemoClient[],
): boolean {
  return (
    normalizedName(query.search) === "иванов антон" &&
    query.source === "recommendation" &&
    query.archive === "active" &&
    query.direction === "desc" &&
    visibleClients.length === 2 &&
    visibleClients[0]?.id === "demo-artyom" &&
    visibleClients[1]?.id === "demo-anton"
  );
}
