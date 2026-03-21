-- Backfill old inbound "unsupported" messages with richer reason text from MessageEvents.RawPayloadJson.
-- Safe behavior:
-- 1) Only touches Messages rows that currently have generic unsupported body.
-- 2) Only for inbound events where MessageType = 'unsupported'.
-- 3) Idempotent: rerun is safe.

BEGIN;

WITH candidates AS (
    SELECT
        m."Id" AS message_id,
        m."TenantId" AS tenant_id,
        m."ProviderMessageId" AS provider_message_id,
        COALESCE(
            NULLIF(TRIM(me."RawPayloadJson"::jsonb #>> '{unsupported,title}'), ''),
            NULLIF(TRIM(me."RawPayloadJson"::jsonb #>> '{unsupported,reason}'), '')
        ) AS unsupported_title,
        COALESCE(
            NULLIF(TRIM(me."RawPayloadJson"::jsonb #>> '{unsupported,message}'), ''),
            NULLIF(TRIM(me."RawPayloadJson"::jsonb #>> '{unsupported,description}'), ''),
            NULLIF(TRIM(me."RawPayloadJson"::jsonb #>> '{unsupported,details}'), ''),
            NULLIF(TRIM(me."RawPayloadJson"::jsonb #>> '{unsupported,type}'), ''),
            NULLIF(TRIM(me."RawPayloadJson"::jsonb #>> '{unsupported,code}'), ''),
            NULLIF(TRIM(me."RawPayloadJson"::jsonb #>> '{unsupported,error}'), '')
        ) AS unsupported_detail
    FROM "Messages" m
    JOIN "MessageEvents" me
      ON me."TenantId" = m."TenantId"
     AND me."ProviderMessageId" = m."ProviderMessageId"
    WHERE me."Direction" = 'inbound'
      AND LOWER(COALESCE(me."MessageType", '')) = 'unsupported'
      AND LOWER(COALESCE(m."MessageType", '')) IN ('session', 'unsupported')
      AND COALESCE(TRIM(m."Body"), '') IN (
          'Inbound unsupported message',
          'Unsupported incoming WhatsApp message type.'
      )
),
computed AS (
    SELECT
        message_id,
        CASE
            WHEN unsupported_title IS NOT NULL AND unsupported_detail IS NOT NULL
                THEN 'Unsupported message: ' || unsupported_title || ' (' || unsupported_detail || ')'
            WHEN unsupported_title IS NOT NULL
                THEN 'Unsupported message: ' || unsupported_title
            WHEN unsupported_detail IS NOT NULL
                THEN 'Unsupported message: ' || unsupported_detail
            ELSE 'Unsupported incoming WhatsApp message type.'
        END AS new_body
    FROM candidates
),
updated AS (
    UPDATE "Messages" m
    SET
        "Body" = c.new_body,
        "MessageType" = 'unsupported'
    FROM computed c
    WHERE m."Id" = c.message_id
      AND COALESCE(TRIM(m."Body"), '') <> c.new_body
    RETURNING m."Id", m."TenantId", m."ProviderMessageId", m."Body"
)
SELECT COUNT(*) AS updated_rows FROM updated;

COMMIT;

