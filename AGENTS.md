# DumpToTXT working constraints

This file contains repository-specific routing and durable product boundaries. Keep assistant-, model-, skill-, and orchestration-specific policy outside this file.

- Use `docs/product.md` for product intent, `docs/architecture.md` for system boundaries, and `docs/development.md` for build/test/development procedures.
- Current source and fresh runtime evidence establish what is implemented. Preserve product decisions when source and documentation disagree; surface the conflict instead of silently redefining intent.
- Keep generated/build artifacts and packaging output separate from source. Do not hand-edit generated deliverables when an owning build step exists.
- Use the verification procedure in `docs/development.md` for the affected boundary and distinguish source checks from packaged/runtime evidence.
