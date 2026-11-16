# The Veyra agent (ADR 0019): the official OpenTelemetry Collector run by the official OpAMP supervisor, unchanged,
# at the version Veyra pins and tests. Veyra configures it over OpAMP; nothing in this image is Veyra's own code.
# Docker Compose builds it beside Veyra; see deploy/docker-compose.yml.

FROM otel/opentelemetry-collector-contrib:0.140.0 AS collector

FROM otel/opentelemetry-collector-opampsupervisor:0.140.0

# The supervisor starts the Collector from this path, as Veyra's start-up file for it says.
COPY --from=collector /otelcol-contrib /otelcol-contrib

# Veyra writes the start-up file, readable only by its own user, into a folder both containers share. The agent runs
# as that same user, 1654, the unprivileged user of the .NET images, and keeps the supervisor's state beside it.
USER root
RUN mkdir -p /etc/veyra-agent /var/lib/veyra-agent && chown 1654:1654 /etc/veyra-agent /var/lib/veyra-agent
USER 1654:1654

# Waits for Veyra to write the start-up file on its first start, then hands over to the supervisor.
ENTRYPOINT ["/bin/sh", "-c", "until [ -r /etc/veyra-agent/supervisor.yaml ]; do sleep 2; done; exec opampsupervisor --config /etc/veyra-agent/supervisor.yaml"]
