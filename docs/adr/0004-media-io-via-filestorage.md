# Media and user-file I/O only via IFileStorage

All media/user file reads and writes go through `IFileStorage` / `S3CompatibleFileStorage`, never raw `File.*` / `Path.*` / `Directory.*`. The abstraction owns the Docker container path (`/var/opt/oet-learner/storage`), named-volume persistence, and audit, so raw I/O silently breaks production storage.
