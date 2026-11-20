#!/bin/sh
# Une base et un utilisateur par service : aucun service ne lit la base d'un autre (principe d'architecture).
# Exécuté une seule fois par l'image postgres, à la création du volume.
set -eu

create_db() {
  name="$1"; password="$2"
  psql -v ON_ERROR_STOP=1 --username postgres <<SQL
CREATE ROLE ${name} LOGIN PASSWORD '${password}';
CREATE DATABASE ${name}_db OWNER ${name};
REVOKE ALL ON DATABASE ${name}_db FROM PUBLIC;
SQL
}

create_db catalog "${CATALOG_DB_PASSWORD:?}"
create_db inventory "${INVENTORY_DB_PASSWORD:?}"
create_db ordering "${ORDERING_DB_PASSWORD:?}"
create_db payment "${PAYMENT_DB_PASSWORD:?}"
create_db notification "${NOTIFICATION_DB_PASSWORD:?}"
