# Contributing to Permixa

Thanks for your interest in Permixa.

## Current policy (preview)

Permixa is in **public preview**. At this stage:

- **Bug reports, questions, and discussion** via [GitHub Issues](https://github.com/AbdulHaDi-Darwish-Dev/Permixa/issues) are welcome.
- **Security issues** must follow [SECURITY.md](SECURITY.md) — do not file public issues for vulnerabilities.
- **External pull requests** are currently **limited / deferred**. Substantial contributions may be declined until contribution ownership and governance (for example CLA/DCO) are decided. Please open an issue to discuss significant changes before investing large effort.

## License

By submitting a contribution you agree that your contribution is intentionally offered for inclusion under the project's **Apache License 2.0** (see [LICENSE](LICENSE)), unless a separate agreement is executed.

This statement is not a CLA and does not change future governance decisions.

## Development notes

- Target framework: **.NET 8**
- Prefer set-based database access (see repository guidelines)
- Do not introduce Redis/Resend into core packages; use optional provider packages
- Keep secrets out of source, logs, and audit metadata

## Code of conduct

A formal Code of Conduct may be added when community contribution is actively accepted.
