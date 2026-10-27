BEGIN;

\ir schema.sql

INSERT INTO user_settings (user_id, flag_name, opacity, blend_mode)
SELECT
    key::bigint,
    COALESCE(value->>'FlagName', 'LGBT'),
    COALESCE((value->>'Opacity')::smallint, 50),
    COALESCE((value->>'BlendMode')::integer, 6)
FROM jsonb_each(:'settings_json'::jsonb)
ON CONFLICT (user_id) DO NOTHING;

COMMIT;
