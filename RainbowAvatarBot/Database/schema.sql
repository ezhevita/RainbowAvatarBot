CREATE TABLE IF NOT EXISTS user_settings (
    user_id bigint PRIMARY KEY
        CONSTRAINT user_settings_user_id_check CHECK (user_id BETWEEN 1 AND 4503599627370495),
    flag_name text NOT NULL DEFAULT 'LGBT',
    opacity smallint NOT NULL DEFAULT 50 CHECK (opacity BETWEEN 0 AND 100),
    blend_mode integer NOT NULL DEFAULT 6 CHECK (blend_mode BETWEEN 0 AND 7)
);
