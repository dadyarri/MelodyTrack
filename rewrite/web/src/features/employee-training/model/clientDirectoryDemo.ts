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

/** Edit distance to any fragment, with free text before and after the match. */
export function demoNameFragmentMatches(name: string, query: string): boolean {
  const text = Array.from(normalizedName(name));
  const search = Array.from(normalizedName(query));
  if (search.length === 0) return true;
  if (text.length === 0 || search.length > text.length + 3) return false;
  let previous = Array.from({ length: search.length + 1 }, (_, index) => Math.min(index, 4));
  for (const character of text) {
    const current = [0];
    for (let column = 1; column <= search.length; column += 1) {
      current[column] = Math.min(
        (previous[column] ?? 4) + 1,
        (current[column - 1] ?? 4) + 1,
        (previous[column - 1] ?? 4) + (character === search[column - 1] ? 0 : 1),
        4,
      );
    }
    if ((current[search.length] ?? 4) <= 3) return true;
    previous = current;
  }
  return false;
}

export function selectDemoClients(query: DemoClientQuery): DemoClient[] {
  const search = normalizedName(query.search);
  return demoClients
    .filter(
      (client) =>
        demoNameFragmentMatches(client.name, search) &&
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
