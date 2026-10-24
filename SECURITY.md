# Security reporting

Report vulnerabilities privately through the repository's GitHub security advisory reporting feature when enabled. Do not post credentials, full diagnostic dumps, or exploitable hardware-write payloads in public issues. If private reporting is unavailable, open a minimal issue asking for a private contact without technical exploit details.

Relevant boundaries include named-pipe ACLs, privileged service commands, backend/plugin provenance, local API authentication, profile parsing, and catalog integrity. The local event token never grants direct fan-write access. Review diagnostics before sharing; network requests are limited to explicit catalog/update and integration functions.
