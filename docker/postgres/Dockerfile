FROM postgres:16@sha256:1a6ab3f5345eb6dbe04a1349529caabdb0ab09293a09590fad07b2246bfa4b54

COPY --chmod=0755 init-roles.sh /docker-entrypoint-initdb.d/10-init-roles.sh
COPY --chmod=0755 healthcheck.sh /usr/local/bin/ledger-healthcheck
