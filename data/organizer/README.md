# Organizer normative corpus

This directory contains the six organizer-provided PDF sources used to build
the frozen normative Knowledge snapshot. The PDF bytes are immutable source
material: do not edit, re-encode, recompress, or rename them.

The SHA-256 checksums, page counts, and expected fragment counts below are the
authoritative identity checks used by `scripts/bootstrap-knowledge.ps1`.

| File | Size (bytes) | SHA-256 | Pages | Fragments |
|---|---:|---|---:|---:|
| `Инструкция по электронному актированию.pdf` | 4,843,247 | `d14f883b14f8f117842541900300901404b37d43cdddf13c3e3266cfbd9155b3` | 93 | 567 |
| `Инструкция по формированию YML.pdf` | 1,197,287 | `753fc58d5c7ae3b15932ef658d88f4eb2886273af2e3067389c5aa0934bd9a9c` | 39 | 298 |
| `Инструкция по работе с Порталом для поставщика.pdf` | 25,943,732 | `3c52c3633b6bc84e99ca1a23336536e9408ba83b28f160ee5c02738a2f754c6b` | 370 | 1,439 |
| `Инструкция по созданию оферты и СТЕ.pdf` | 3,891,038 | `ce50227cb1bb29b9145dd0fb52181c353c03bb11e00a0ab467cf544914b20159` | 65 | 360 |
| `Инструкция по работе с машиночитаемыми доверенностями.pdf` | 1,122,812 | `755870a7454fd166146bfd64a7c50009fc834340798d28ff9cee3132265272b9` | 7 | 15 |
| `Инструкция по работе с Порталом для заказчика.pdf` | 16,045,058 | `7355b2c39b4aa4fbac758f64aeab0bdfd4a8219cc483dd5ca52e28eacb59fcde` | 256 | 1,220 |

The expected corpus is 6 documents, 830 pages, and 3,899 fragments. The
bootstrap must fail closed on any checksum, page, fragment, snapshot, or card
count drift. The files are used only for deterministic Knowledge DB bootstrap;
historical support data is not included.

## Fresh machine

```text
git clone <repository-url>
cd TenderHack
copy .env.example .env
# fill only required local secrets/runtime URLs
docker compose up -d --build
powershell -ExecutionPolicy Bypass -File .\scripts\bootstrap-knowledge.ps1
```

Then check the stack with `docker compose ps` and open
`http://localhost:8080`. The bootstrap script applies Knowledge migrations,
waits for PostgreSQL, ingests the six PDFs, publishes the expected normative
snapshot, and verifies the 20 condition cards.

RunPod inference URLs and tokens are runtime secrets and are never stored in
Git or in this directory.
