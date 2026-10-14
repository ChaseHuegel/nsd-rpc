# Improvement: nsdc generated output lacks the nullable directive

- Type: improvement
- Status: open
- Workflow: ../specs/issues.md

## Problem

nsdc 1.3.3 emits `?` nullable annotations without a `#nullable` directive.
The build fails with CS8669 on any schema that uses optional fields. The
build pipeline workaround prepends the directive to every generated file in
the `NsdcCodegen` target. See
[../../specs/codegen-pipeline.md](../../specs/codegen-pipeline.md). The
fix belongs in the needlefish compiler. Do not keep the workaround forever.

## Acceptance criteria

- [ ] nsdc emits the `#nullable` directive in generated output, or grows a flag that produces it.
- [ ] The `PrependNullableDirective` task leaves [NsdRpc/NsdRpc.csproj](../../NsdRpc/NsdRpc.csproj).
- [ ] The codegen pipeline doc drops the workaround note.
- [ ] `NSDC_VERSION` bumps in the workflows and [development.md](../../development.md), same change.
