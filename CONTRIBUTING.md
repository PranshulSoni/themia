# Contributing to Themia

Thanks for helping make Themia better.

## Issues

Use the [issue forms](https://github.com/crazy-rabbit-software/themia/issues/new/choose) for bug reports, feature requests and widget ideas. Include your Themia and Windows versions and what you did right before the problem.

Report security problems privately to themia@nathaniel-walser.com, not in a public issue.

## Pull requests

- Open an issue first for anything larger than a small fix, so the approach is agreed before you build it.
- Read the Engineering Principles in [CLAUDE.md](CLAUDE.md). They apply to every change.
- Behavior is pinned in each subsystem's `SPEC.md` with `[S-NN]` tags. Keep the spec and its tests in step with your change.
- Run `npm run check` before you open the pull request, and `npm run lint:rust` if you changed Rust code.
- Keep refactors and behavior changes in separate commits.

## Licensing of contributions

Themia is licensed under the [Elastic License 2.0](LICENSE). By submitting a contribution, you agree that:

1. you wrote it, or otherwise have the right to submit it under these terms;
2. it is licensed under the Elastic License 2.0; and
3. Nathaniel Walser may also use, license and relicense it under other terms, including commercial ones.

If you can't agree to this, open an issue describing the change instead.
