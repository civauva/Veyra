-- Database servers and containers are blocks too (ADR 0018), so their alerts may name them.
ALTER TABLE alerting.alert DROP CONSTRAINT alert_block_kind_check;
ALTER TABLE alerting.alert
    ADD CONSTRAINT alert_block_kind_check CHECK (block_kind IN ('service', 'host', 'database', 'container'));
