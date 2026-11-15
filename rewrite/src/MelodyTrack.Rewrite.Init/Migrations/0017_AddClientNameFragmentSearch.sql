CREATE FUNCTION public.client_name_fragment_matches(full_name text, query text)
RETURNS boolean
LANGUAGE plpgsql
IMMUTABLE STRICT PARALLEL SAFE
AS $$
DECLARE
    name_chars text[] := string_to_array(full_name, NULL);
    query_chars text[] := string_to_array(query, NULL);
    name_length integer := char_length(full_name);
    query_length integer := char_length(query);
    previous_row integer[];
    current_row integer[];
    name_index integer;
    query_index integer;
BEGIN
    IF query_length = 0 OR strpos(full_name, query) > 0 THEN
        RETURN true;
    END IF;
    IF name_length = 0 OR query_length > name_length + 3 THEN
        RETURN false;
    END IF;
    -- Any nonempty name contains a one-character fragment within three edits.
    IF query_length <= 3 THEN
        RETURN true;
    END IF;

    previous_row := array_fill(0, ARRAY[query_length + 1], ARRAY[0]);
    current_row := array_fill(0, ARRAY[query_length + 1], ARRAY[0]);
    FOR query_index IN 1..query_length LOOP
        previous_row[query_index] := least(query_index, 4);
    END LOOP;
    FOR name_index IN 1..name_length LOOP
        -- A free name prefix lets a match start at any character, including mid-word.
        current_row[0] := 0;
        FOR query_index IN 1..query_length LOOP
            current_row[query_index] := least(
                previous_row[query_index] + 1,
                current_row[query_index - 1] + 1,
                previous_row[query_index - 1] +
                    CASE WHEN name_chars[name_index] = query_chars[query_index] THEN 0 ELSE 1 END,
                4);
        END LOOP;
        -- A free suffix completes an approximate substring rather than the whole name.
        IF current_row[query_length] <= 3 THEN
            RETURN true;
        END IF;
        previous_row := current_row;
    END LOOP;
    RETURN false;
END;
$$;
