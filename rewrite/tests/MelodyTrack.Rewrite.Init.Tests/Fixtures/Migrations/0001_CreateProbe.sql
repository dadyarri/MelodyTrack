CREATE TABLE generated_probe (
    id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    value text NOT NULL
);
INSERT INTO generated_probe (value) VALUES ('SQL "quoted" \ путь');
