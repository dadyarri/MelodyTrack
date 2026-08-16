const pluralRules = new Intl.PluralRules("ru");

type RussianPluralForms = {
  few: string;
  many: string;
  one: string;
};

export function pluralizeRu(count: number, forms: RussianPluralForms) {
  const category = pluralRules.select(Math.abs(count));

  switch (category) {
    case "one":
      return forms.one;
    case "few":
      return forms.few;
    default:
      return forms.many;
  }
}
